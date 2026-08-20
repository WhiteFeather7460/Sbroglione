using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Server remoto": connessione FTP/FTPS/SFTP, navigazione e download con filtri.
/// </summary>
[SuppressMessage(
    "Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "I CancellationTokenSource di download e upload sono creati e distrutti dentro " +
                    "RunDownloadAsync/RunUploadAsync: i campi sono solo l'appiglio per Cancel* e restano " +
                    "null fuori dal batch. Il CancellationTokenSource del debounce filtri (_filterCts) " +
                    "segue invece un pattern Cancel+Dispose+sostituisci a ogni nuovo set di un filtro " +
                    "e non torna mai null: nel peggiore dei casi (viewmodel distrutta a debounce in " +
                    "corso) resta un singolo CTS non liberato, trascurabile e non un leak crescente.")]
public class RemoteBrowserViewModel : ViewModelBase
{
    private readonly Func<ConnectionProfile, IRemoteFileClient> _clientFactory;
    private readonly ICredentialStore _credentialStore;
    private readonly string _profilesFilePath;

    private IRemoteFileClient? _client;
    private bool _profilesLoaded;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    public ObservableCollection<RemoteEntryViewModel> Items { get; } = new();

    private ConnectionProfile? _selectedProfile;
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            // Cambiare profilo con il banner host key aperto significa che la fingerprint in
            // sospeso appartiene a un altro server: va buttata, altrimenti un "Accetta" successivo
            // la fisserebbe sul profilo sbagliato (TOFU aggirato).
            if (!ReferenceEquals(_selectedProfile, value))
                ClearPendingFingerprint();
            this.RaiseAndSetIfChanged(ref _selectedProfile, value);
        }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private string _currentPath = "/";
    public string CurrentPath
    {
        get => _currentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    private bool _isPasswordPromptVisible;
    public bool IsPasswordPromptVisible
    {
        get => _isPasswordPromptVisible;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordPromptVisible, value);
    }

    private string? _passwordInput;
    public string? PasswordInput
    {
        get => _passwordInput;
        set => this.RaiseAndSetIfChanged(ref _passwordInput, value);
    }

    public bool CanSavePassword => _credentialStore.IsAvailable;

    private bool _savePassword;
    public bool SavePassword
    {
        get => _savePassword;
        set => this.RaiseAndSetIfChanged(ref _savePassword, value);
    }

    private string? _pendingFingerprint;
    public string? PendingFingerprint
    {
        get => _pendingFingerprint;
        private set => this.RaiseAndSetIfChanged(ref _pendingFingerprint, value);
    }

    /// <summary>
    /// Profilo a cui appartiene <see cref="PendingFingerprint"/>: catturato quando la fingerprint
    /// viene proposta, così l'accettazione non può finire su un profilo diverso.
    /// </summary>
    private ConnectionProfile? _pendingFingerprintProfile;

    // ----- Filtri (bound alla UI) -----

    /// <summary>Sottoinsieme di <see cref="Items"/> che passa il filtro: è ciò che la lista mostra.</summary>
    public ObservableCollection<RemoteEntryViewModel> VisibleItems { get; } = new();

    /// <summary>Debounce applicato a <see cref="ScheduleRebuild"/>; i test lo azzerano.</summary>
    internal static TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(200);
    private CancellationTokenSource? _filterCts;

    /// <summary>Task dell'ultimo rebuild filtri programmato; attendibile nei test.</summary>
    public Task FilterRefresh { get; private set; } = Task.CompletedTask;

    private string? _filterPattern;
    public string? FilterPattern
    {
        get => _filterPattern;
        set { this.RaiseAndSetIfChanged(ref _filterPattern, value); ScheduleRebuild(); }
    }

    private string? _filterMinSizeKb;
    public string? FilterMinSizeKb
    {
        get => _filterMinSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMinSizeKb, value); ScheduleRebuild(); }
    }

    private string? _filterMaxSizeKb;
    public string? FilterMaxSizeKb
    {
        get => _filterMaxSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMaxSizeKb, value); ScheduleRebuild(); }
    }

    private DateTimeOffset? _filterModifiedAfter;
    public DateTimeOffset? FilterModifiedAfter
    {
        get => _filterModifiedAfter;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedAfter, value); ScheduleRebuild(); }
    }

    private DateTimeOffset? _filterModifiedBefore;
    public DateTimeOffset? FilterModifiedBefore
    {
        get => _filterModifiedBefore;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedBefore, value); ScheduleRebuild(); }
    }

    private bool _onlyMissing;
    public bool OnlyMissing
    {
        get => _onlyMissing;
        set { this.RaiseAndSetIfChanged(ref _onlyMissing, value); ScheduleRebuild(); }
    }

    private bool _includeSubfolders;
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => this.RaiseAndSetIfChanged(ref _includeSubfolders, value);
    }

    private bool _overwriteAlways;
    public bool OverwriteAlways
    {
        get => _overwriteAlways;
        set => this.RaiseAndSetIfChanged(ref _overwriteAlways, value);
    }

    // ----- Download -----

    private string? _destinationFolder;

    /// <summary>Cartella locale di destinazione: al set aggiorna il profilo e gli stati "Su disco".</summary>
    public string? DestinationFolder
    {
        get => _destinationFolder;
        set
        {
            this.RaiseAndSetIfChanged(ref _destinationFolder, value);
            if (SelectedProfile is not null)
                SelectedProfile.LastDestinationFolder = value;
            // Il setter non può essere async: fire-and-forget, coerente con l'esecuzione su
            // threadpool di RefreshLocalStatusesAsync (nessuna urgenza di completamento sincrono).
            _ = RefreshLocalStatusesAsync();
        }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    /// <summary>Avanzamento del batch, da 0 a 1.</summary>
    private double _downloadProgressValue;
    public double DownloadProgressValue
    {
        get => _downloadProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _downloadProgressValue, value);
    }

    private string? _downloadStatusText;
    public string? DownloadStatusText
    {
        get => _downloadStatusText;
        private set => this.RaiseAndSetIfChanged(ref _downloadStatusText, value);
    }

    private CancellationTokenSource? _downloadCts;

    // ----- Upload -----

    private bool _uploadOverwriteAlways;
    public bool UploadOverwriteAlways
    {
        get => _uploadOverwriteAlways;
        set => this.RaiseAndSetIfChanged(ref _uploadOverwriteAlways, value);
    }

    private bool _isUploading;
    public bool IsUploading
    {
        get => _isUploading;
        private set => this.RaiseAndSetIfChanged(ref _isUploading, value);
    }

    private double _uploadProgressValue;
    public double UploadProgressValue
    {
        get => _uploadProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _uploadProgressValue, value);
    }

    private string? _uploadStatusText;
    public string? UploadStatusText
    {
        get => _uploadStatusText;
        private set => this.RaiseAndSetIfChanged(ref _uploadStatusText, value);
    }

    private CancellationTokenSource? _uploadCts;

    /// <summary>Costruttore per la view: dipendenze reali.</summary>
    public RemoteBrowserViewModel()
        : this(RemoteClientFactory.Create, CredentialStoreFactory.Create(), ProfileStore.DefaultPath)
    {
    }

    /// <summary>Costruttore testabile con dipendenze iniettate.</summary>
    public RemoteBrowserViewModel(
        Func<ConnectionProfile, IRemoteFileClient> clientFactory,
        ICredentialStore credentialStore,
        string profilesFilePath)
    {
        _clientFactory = clientFactory;
        _credentialStore = credentialStore;
        _profilesFilePath = profilesFilePath;
        _savePassword = credentialStore.IsAvailable;
    }

    /// <summary>
    /// Carica i profili salvati (chiamata dalla view all'avvio). È idempotente: l'evento Loaded
    /// riscatta a ogni rientro della view nel visual tree (cambio scheda) e una ricarica
    /// azzererebbe selezione e stato mentre una connessione è attiva.
    /// </summary>
    public async Task LoadProfilesAsync()
    {
        if (_profilesLoaded)
            return;

        // Alzata prima dell'await: due Loaded ravvicinati non devono caricare due volte.
        _profilesLoaded = true;

        var profiles = await ProfileStore.LoadAsync(_profilesFilePath);
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public async Task ConnectAsync()
    {
        // Il profilo è catturato qui e non più riletto: la combo non è disabilitata durante
        // IsBusy, quindi un cambio di selezione a connessione avviata sposterebbe fingerprint
        // in sospeso e password salvata sul profilo sbagliato.
        var profile = SelectedProfile;
        if (profile is null || IsBusy || IsDownloading || IsUploading)
            return;

        // IsBusy va alzata prima di qualsiasi await: la lettura dal keyring può essere lenta e
        // senza questo una seconda chiamata (doppio clic su "Connetti") supererebbe la guardia
        // creando un secondo client che resterebbe orfano.
        IsBusy = true;
        try
        {
            ErrorMessage = null;
            ClearPendingFingerprint();

            string? password = PasswordInput;
            if (string.IsNullOrEmpty(password))
                password = await _credentialStore.GetPasswordAsync(profile.Id);

            if (string.IsNullOrEmpty(password))
            {
                IsPasswordPromptVisible = true;
                StatusMessage = _credentialStore.IsAvailable
                    ? "Inserire la password."
                    : "Keyring di sistema non disponibile: la password va inserita a ogni connessione.";
                return;
            }

            await DisposeClientAsync();
            var client = _clientFactory(profile);
            var error = await client.ConnectAsync(profile, password, CancellationToken.None);

            if (error is not null)
            {
                await client.DisposeAsync();
                ErrorMessage = error.Message;
                if (error.Kind == RemoteErrorKind.HostKeyMismatch)
                {
                    PendingFingerprint = error.Fingerprint;
                    _pendingFingerprintProfile = profile;
                }
                else if (error.Kind == RemoteErrorKind.AuthFailed)
                {
                    // Senza il prompt la password sbagliata resterebbe quella del keyring e
                    // l'utente non avrebbe alcun modo di reinserirla: vicolo cieco.
                    PasswordInput = null;
                    IsPasswordPromptVisible = true;
                    StatusMessage = "Autenticazione fallita: reinserisci la password.";
                }
                return;
            }

            _client = client;
            IsConnected = true;
            IsPasswordPromptVisible = false;

            CurrentPath = "/";
            DestinationFolder = profile.LastDestinationFolder;
            // Chiamata interna: IsBusy è già alzata da questo metodo.
            await LoadListingCoreAsync();

            // Dopo l'elenco: LoadListingCoreAsync azzera ErrorMessage e cancellerebbe
            // l'eventuale avviso di keyring non scrivibile.
            await TrySavePasswordAsync(profile);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Salva la password nel keyring senza poter far cadere il chiamante: gli handler della view
    /// sono async void, quindi un'eccezione del backend (API Windows, CLI keyring) terminerebbe
    /// il processo. Il messaggio d'errore è fisso: non riporta mai né la password né dettagli
    /// del backend che potrebbero contenerla. Il profilo arriva dal chiamante (quello con cui la
    /// connessione è stata davvero fatta): rileggere la selezione qui salverebbe la password sotto
    /// il profilo sbagliato se l'utente cambia combo a connessione avviata.
    /// </summary>
    private async Task TrySavePasswordAsync(ConnectionProfile profile)
    {
        string? password = PasswordInput;
        PasswordInput = null;

        if (string.IsNullOrEmpty(password) || !SavePassword || !_credentialStore.IsAvailable)
            return;

        try
        {
            await _credentialStore.SetPasswordAsync(profile.Id, password);
        }
        catch (Exception)
        {
            ErrorMessage = "Connessione riuscita, ma il salvataggio della password nel keyring "
                           + "è fallito: andrà reinserita alla prossima connessione.";
        }
    }

    public async Task DisconnectAsync()
    {
        // Senza questa guardia la disconnessione libererebbe il client mentre un elenco o un
        // download lo stanno ancora usando, lasciando l'operazione in corso senza connessione.
        if (IsBusy || IsDownloading || IsUploading)
            return;

        await DisposeClientAsync();
        IsConnected = false;
        Items.Clear();
        VisibleItems.Clear();   // è la collezione mostrata dalla lista: senza questo resterebbe a video
        StatusMessage = "Disconnesso.";
    }

    public async Task OpenDirectoryAsync(RemoteEntryViewModel entry)
    {
        if (!entry.IsDirectory || _client is null || IsBusy || IsDownloading || IsUploading)
            return;

        CurrentPath = entry.Item.FullPath;
        await LoadListingAsync();
    }

    public async Task NavigateUpAsync()
    {
        if (_client is null || IsBusy || IsDownloading || IsUploading || CurrentPath == "/")
            return;

        int lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadListingAsync();
    }

    public Task RefreshAsync() => _client is null ? Task.CompletedTask : LoadListingAsync();

    /// <summary>Azzera la fingerprint in sospeso e il profilo a cui era stata associata.</summary>
    private void ClearPendingFingerprint()
    {
        PendingFingerprint = null;
        _pendingFingerprintProfile = null;
    }

    public async Task AcceptFingerprintAsync()
    {
        // La fingerprint va scritta sul profilo che l'ha proposta: se nel frattempo la selezione
        // è cambiata lo stato pending è già stato azzerato e qui non si scrive nulla.
        var profile = _pendingFingerprintProfile;
        if (profile is null || PendingFingerprint is null || !ReferenceEquals(profile, SelectedProfile))
            return;

        profile.AcceptedHostKeyFingerprint = PendingFingerprint;
        ClearPendingFingerprint();
        await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        await ConnectAsync();
    }

    public void RejectFingerprint()
    {
        ClearPendingFingerprint();
        StatusMessage = "Connessione rifiutata: host key non accettata.";
    }

    /// <summary>
    /// Elimina il profilo selezionato: se è quello connesso disconnette prima, poi lo rimuove
    /// dalla lista, persiste i profili rimasti e cancella la password dal keyring. La cancellazione
    /// dal keyring è best effort: un keyring che rifiuta l'operazione non deve impedire la rimozione.
    /// </summary>
    public async Task DeleteProfileAsync()
    {
        var profile = SelectedProfile;
        if (profile is null || IsBusy || IsDownloading || IsUploading)
            return;

        // Selezione e rimozione vanno fatte prima di qualsiasi await: altrimenti un doppio clic
        // su "Elimina" supererebbe la guardia una seconda volta, disconnettendo due volte lo
        // stesso client e lanciando due SaveAsync concorrenti sullo stesso file.
        SelectedProfile = null;   // il setter azzera anche l'eventuale stato host key in sospeso
        Profiles.Remove(profile);

        if (IsConnected)
            await DisconnectAsync();

        await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());

        try
        {
            await _credentialStore.DeletePasswordAsync(profile.Id);
        }
        catch (Exception)
        {
            // Profilo già rimosso e persistito: la password orfana nel keyring non è un motivo
            // per fallire l'operazione (e l'handler chiamante è async void).
        }

        StatusMessage = $"Profilo \"{profile.Name}\" eliminato.";
    }

    /// <summary>
    /// Scarica le voci selezionate. Una directory selezionata contribuisce con i suoi file solo
    /// se <see cref="IncludeSubfolders"/> è attiva, altrimenti viene ignorata.
    /// </summary>
    public Task DownloadSelectedAsync(IReadOnlyList<RemoteEntryViewModel> selected)
        => StartDownloadAsync(() => CollectSelectedFilesAsync(selected));

    /// <summary>Scarica la cartella corrente, ricorsivamente se <see cref="IncludeSubfolders"/> è attiva.</summary>
    public Task DownloadCurrentDirectoryAsync()
        => StartDownloadAsync(CollectCurrentDirectoryFilesAsync);

    /// <summary>Annulla il batch in corso: il download termina con "Download annullato."</summary>
    public void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>
    /// Guardia unica dei download: <see cref="IsDownloading"/> va alzata prima di qualsiasi await
    /// perché anche la raccolta dei file (elenco ricorsivo) usa lo stesso client della navigazione.
    /// </summary>
    private async Task StartDownloadAsync(Func<Task<IReadOnlyList<RemoteItem>?>> collectFiles)
    {
        if (_client is null || IsBusy || IsDownloading || IsUploading)
            return;

        IsDownloading = true;
        try
        {
            var files = await collectFiles();
            if (files is null)   // errore di listing: già riportato in ErrorMessage
                return;

            await RunDownloadAsync(files);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>File delle voci selezionate; null se un elenco ricorsivo fallisce.</summary>
    private async Task<IReadOnlyList<RemoteItem>?> CollectSelectedFilesAsync(
        IReadOnlyList<RemoteEntryViewModel> selected)
    {
        var files = new List<RemoteItem>();
        foreach (var entry in selected)
        {
            if (!entry.IsDirectory)
            {
                files.Add(entry.Item);
            }
            else if (IncludeSubfolders && _client is not null)
            {
                var result = await _client.ListRecursiveAsync(entry.Item.FullPath, CancellationToken.None);
                if (result.Error is not null)
                {
                    ErrorMessage = result.Error.Message;
                    return null;
                }
                files.AddRange(result.Items);
            }
        }
        return files;
    }

    /// <summary>File della cartella corrente; null se l'elenco ricorsivo fallisce.</summary>
    private async Task<IReadOnlyList<RemoteItem>?> CollectCurrentDirectoryFilesAsync()
    {
        if (_client is null)
            return null;

        if (!IncludeSubfolders)
            return Items.Where(i => !i.IsDirectory).Select(i => i.Item).ToList();

        var result = await _client.ListRecursiveAsync(CurrentPath, CancellationToken.None);
        if (result.Error is not null)
        {
            ErrorMessage = result.Error.Message;
            return null;
        }
        return result.Items;
    }

    /// <summary>Esegue il batch: presuppone <see cref="IsDownloading"/> già gestita dal chiamante.</summary>
    private async Task RunDownloadAsync(IReadOnlyList<RemoteItem> files)
    {
        if (_client is null)
            return;

        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            ErrorMessage = "Scegliere una cartella di destinazione prima di scaricare.";
            return;
        }

        ErrorMessage = null;
        _downloadCts = new CancellationTokenSource();

        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadProgressValue = p.TotalFiles == 0 ? 0 : (double)p.FileIndex / p.TotalFiles;
            DownloadStatusText = $"{p.FileIndex}/{p.TotalFiles} — {p.CurrentFile}";
        });

        try
        {
            var report = await DownloadService.DownloadAsync(
                _client, files, CurrentPath, DestinationFolder, BuildFilter(),
                OverwriteAlways, progress, _downloadCts.Token);

            StatusMessage =
                $"Scaricati {report.Downloaded.Count}, saltati {report.Skipped.Count}, falliti {report.Failed.Count}.";
            if (report.Failed.Count > 0)
                ErrorMessage = $"{report.Failed.Count} file falliti. Primo errore: {report.Failed[0].Reason}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download annullato.";
        }
        finally
        {
            DownloadStatusText = null;
            DownloadProgressValue = 0;
            _downloadCts.Dispose();
            _downloadCts = null;
            await RefreshLocalStatusesAsync();
            // Persiste la destinazione appena usata (LastDestinationFolder del profilo).
            await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        }
    }

    /// <summary>Carica i file locali indicati (percorsi assoluti) nella cartella corrente, senza struttura.</summary>
    public Task UploadFilesAsync(IReadOnlyList<string> localPaths)
    {
        // La destinazione remota è catturata qui, prima di qualsiasi await: se l'utente naviga
        // altrove mentre l'upload si prepara, i file devono comunque finire dove erano stati chiesti.
        string remoteBasePath = CurrentPath;
        // Filtro difensivo: questo metodo è pubblico e una cartella passata per errore
        // produrrebbe una voce remota fasulla.
        var entries = localPaths
            .Where(path => !Directory.Exists(path))
            .Select(path => new UploadEntry(path, Path.GetFileName(path)))
            .ToList();
        return RunUploadAsync(entries, remoteBasePath);
    }

    /// <summary>
    /// Carica il contenuto di una cartella locale nella cartella corrente, ricorsivamente se
    /// <see cref="IncludeSubfolders"/> è attiva, preservando la struttura relativa.
    /// </summary>
    public async Task UploadFolderAsync(string localFolderPath)
    {
        // Catturata prima dell'enumerazione (potenzialmente lenta): una navigazione remota nel
        // frattempo non deve dirottare l'upload su un'altra cartella.
        string remoteBasePath = CurrentPath;
        var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        // L'enumerazione ricorsiva della cartella è I/O sincrono: fuori dal thread UI, che
        // altrimenti si bloccherebbe su cartelle grandi mentre IncludeSubfolders è attiva.
        var entries = await Task.Run(() => Directory.EnumerateFiles(localFolderPath, "*", searchOption)
            .Select(path => new UploadEntry(
                path, Path.GetRelativePath(localFolderPath, path).Replace(Path.DirectorySeparatorChar, '/')))
            .ToList());
        await RunUploadAsync(entries, remoteBasePath);
    }

    /// <summary>Annulla il batch di upload in corso: termina con "Caricamento annullato."</summary>
    public void CancelUpload() => _uploadCts?.Cancel();

    /// <summary>Guardia unica degli upload: mai in corso insieme a un download o un'altra operazione sul client.</summary>
    private async Task RunUploadAsync(IReadOnlyList<UploadEntry> entries, string remoteBasePath)
    {
        if (_client is null || IsBusy || IsDownloading || IsUploading)
            return;

        // Caso a sé rispetto alle guardie di rientranza qui sopra: è un esito che riguarda
        // l'utente (cartella vuota, o senza file al primo livello) e va comunicato.
        if (entries.Count == 0)
        {
            StatusMessage = "Nessun file da caricare.";
            return;
        }

        IsUploading = true;
        ErrorMessage = null;
        _uploadCts = new CancellationTokenSource();

        var progress = new Progress<UploadProgress>(p =>
        {
            UploadProgressValue = p.TotalFiles == 0 ? 0 : (double)p.FileIndex / p.TotalFiles;
            UploadStatusText = $"{p.FileIndex}/{p.TotalFiles} — {p.CurrentFile}";
        });

        try
        {
            var report = await UploadService.UploadAsync(
                _client, entries, remoteBasePath, UploadOverwriteAlways, progress, _uploadCts.Token);

            StatusMessage =
                $"Caricati {report.Uploaded.Count}, saltati {report.Skipped.Count}, falliti {report.Failed.Count}.";
            if (report.Failed.Count > 0)
                ErrorMessage = $"{report.Failed.Count} file falliti. Primo errore: {report.Failed[0].Reason}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Caricamento annullato.";
        }
        finally
        {
            UploadStatusText = null;
            UploadProgressValue = 0;
            _uploadCts.Dispose();
            _uploadCts = null;
            IsUploading = false;
        }

        // Rientra nella cartella corrente per mostrare i file appena caricati: fuori dal blocco
        // IsUploading, così LoadListingAsync (che guarda anche IsUploading) non si blocca da solo.
        // LoadListingCoreAsync sovrascrive StatusMessage/ErrorMessage con l'esito dell'elenco: li
        // catturiamo prima e li ripristiniamo dopo, così il riepilogo dell'upload resta visibile.
        // Se però l'elenco fresco produce un proprio ErrorMessage (es. la cartella non è più
        // raggiungibile), quello ha priorità: è più recente e più rilevante del vecchio errore di
        // upload, e non va perso sotto un messaggio ormai superato.
        string? finalStatusMessage = StatusMessage;
        string? finalErrorMessage = ErrorMessage;
        await RefreshAsync();
        StatusMessage = finalStatusMessage;
        if (finalErrorMessage is not null && ErrorMessage is null)
            ErrorMessage = finalErrorMessage;
    }

    /// <summary>Traduce i campi della UI in criteri di filtro (i KB diventano byte).</summary>
    private DownloadFilter BuildFilter() => new()
    {
        NamePattern = FilterPattern,
        MinSize = ParseKb(FilterMinSizeKb),
        MaxSize = ParseKb(FilterMaxSizeKb),
        ModifiedAfter = FilterModifiedAfter?.DateTime,
        ModifiedBefore = FilterModifiedBefore?.DateTime,
        OnlyMissing = OnlyMissing,
        Recursive = IncludeSubfolders
    };

    /// <summary>
    /// KB → byte; testo vuoto, non numerico, negativo o così grande da traboccare = nessun limite
    /// (un overflow darebbe una soglia negativa e ribalterebbe il senso del filtro).
    /// </summary>
    private static long? ParseKb(string? text) =>
        long.TryParse(text, out long kb) && kb >= 0 && kb <= long.MaxValue / 1024 ? kb * 1024 : null;

    /// <summary>Riallinea <see cref="VisibleItems"/> a <see cref="Items"/> applicando il filtro.</summary>
    private void RebuildVisibleItems()
    {
        var filter = BuildFilter();
        VisibleItems.Clear();
        foreach (var entry in Items)
        {
            if (filter.Matches(entry.Item))
                VisibleItems.Add(entry);
        }
    }

    /// <summary>
    /// Programma un rebuild di <see cref="VisibleItems"/> dopo <see cref="FilterDebounce"/>: più
    /// set ravvicinati (es. l'utente che digita nel filtro) cancellano il rebuild precedente
    /// invece di accodarne uno per ogni carattere.
    /// </summary>
    private void ScheduleRebuild()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = _filterCts = new CancellationTokenSource();
        FilterRefresh = RebuildAfterDebounceAsync(cts.Token);
    }

    private async Task RebuildAfterDebounceAsync(CancellationToken ct)
    {
        try { await Task.Delay(FilterDebounce, ct); }
        catch (OperationCanceledException) { return; }
        UiDispatch.Post(RebuildVisibleItems);
    }

    /// <summary>
    /// Elenco richiesto dai comandi di navigazione: ignora la richiesta se un'operazione è già
    /// in corso, così due comandi ravvicinati non si sovrappongono sullo stesso client.
    /// </summary>
    private async Task LoadListingAsync()
    {
        if (_client is null || IsBusy || IsDownloading || IsUploading)
            return;

        IsBusy = true;
        try
        {
            await LoadListingCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Elenco vero e proprio: presuppone che <see cref="IsBusy"/> sia già gestita dal chiamante.</summary>
    private async Task LoadListingCoreAsync()
    {
        if (_client is null)
            return;

        ErrorMessage = null;
        var result = await _client.ListDirectoryAsync(CurrentPath, CancellationToken.None);
        Items.Clear();
        VisibleItems.Clear();

        if (result.Error is not null)
        {
            ErrorMessage = result.Error.Message;
            return;
        }

        foreach (var item in result.Items
                     .OrderByDescending(i => i.IsDirectory)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(new RemoteEntryViewModel(item));
        }

        await RefreshLocalStatusesAsync();
        StatusMessage = $"{Items.Count} elementi in {CurrentPath}";
        RebuildVisibleItems();
    }

    /// <summary>
    /// Ricalcola la colonna "Su disco" per i file di primo livello. Lo stat delle destinazioni
    /// gira su threadpool (può toccare percorsi di rete lenti): lo snapshot di <see cref="Items"/>
    /// è preso sul thread UI, il calcolo su threadpool, l'assegnazione finale di nuovo sul thread
    /// UI (via la continuazione dell'await, che nell'app cattura il contesto Avalonia).
    /// </summary>
    private async Task RefreshLocalStatusesAsync()
    {
        string? destination = DestinationFolder;
        var entries = Items.ToList();                  // snapshot sul thread UI

        var statuses = await Task.Run(() => entries.Select(entry =>
            entry.IsDirectory || string.IsNullOrWhiteSpace(destination)
                ? (LocalFileStatus?)null
                : DownloadService.GetLocalStatus(entry.Item, Path.Combine(destination, entry.Name)))
            .ToList());

        for (int i = 0; i < entries.Count; i++)        // continuation: di nuovo sul thread UI
            entries[i].LocalStatus = statuses[i];
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    /// <summary>
    /// Percorso del file profili: la view lo usa per salvare dall'editor, così view e viewmodel
    /// scrivono sempre sullo stesso file anche quando non è quello predefinito (test compresi).
    /// </summary>
    internal string ProfilesFilePath => _profilesFilePath;
}
