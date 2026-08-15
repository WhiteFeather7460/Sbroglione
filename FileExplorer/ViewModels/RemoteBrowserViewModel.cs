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
    Justification = "Il CancellationTokenSource del download è creato e distrutto dentro " +
                    "RunDownloadAsync: il campo è solo l'appiglio per CancelDownload e resta null " +
                    "fuori dal batch, quindi la viewmodel non ha uno stato disposable da liberare.")]
public class RemoteBrowserViewModel : ViewModelBase
{
    private readonly Func<ConnectionProfile, IRemoteFileClient> _clientFactory;
    private readonly ICredentialStore _credentialStore;
    private readonly string _profilesFilePath;

    private IRemoteFileClient? _client;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    public ObservableCollection<RemoteEntryViewModel> Items { get; } = new();

    private ConnectionProfile? _selectedProfile;
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
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

    // ----- Filtri (bound alla UI) -----

    /// <summary>Sottoinsieme di <see cref="Items"/> che passa il filtro: è ciò che la lista mostra.</summary>
    public ObservableCollection<RemoteEntryViewModel> VisibleItems { get; } = new();

    private string? _filterPattern;
    public string? FilterPattern
    {
        get => _filterPattern;
        set { this.RaiseAndSetIfChanged(ref _filterPattern, value); RebuildVisibleItems(); }
    }

    private string? _filterMinSizeKb;
    public string? FilterMinSizeKb
    {
        get => _filterMinSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMinSizeKb, value); RebuildVisibleItems(); }
    }

    private string? _filterMaxSizeKb;
    public string? FilterMaxSizeKb
    {
        get => _filterMaxSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMaxSizeKb, value); RebuildVisibleItems(); }
    }

    private DateTimeOffset? _filterModifiedAfter;
    public DateTimeOffset? FilterModifiedAfter
    {
        get => _filterModifiedAfter;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedAfter, value); RebuildVisibleItems(); }
    }

    private DateTimeOffset? _filterModifiedBefore;
    public DateTimeOffset? FilterModifiedBefore
    {
        get => _filterModifiedBefore;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedBefore, value); RebuildVisibleItems(); }
    }

    private bool _onlyMissing;
    public bool OnlyMissing
    {
        get => _onlyMissing;
        set => this.RaiseAndSetIfChanged(ref _onlyMissing, value);
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
            RefreshLocalStatuses();
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

    /// <summary>Carica i profili salvati (chiamata dalla view all'avvio).</summary>
    public async Task LoadProfilesAsync()
    {
        var profiles = await ProfileStore.LoadAsync(_profilesFilePath);
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public async Task ConnectAsync()
    {
        if (SelectedProfile is null || IsBusy || IsDownloading)
            return;

        // IsBusy va alzata prima di qualsiasi await: la lettura dal keyring può essere lenta e
        // senza questo una seconda chiamata (doppio clic su "Connetti") supererebbe la guardia
        // creando un secondo client che resterebbe orfano.
        IsBusy = true;
        try
        {
            ErrorMessage = null;
            PendingFingerprint = null;

            string? password = PasswordInput;
            if (string.IsNullOrEmpty(password))
                password = await _credentialStore.GetPasswordAsync(SelectedProfile.Id);

            if (string.IsNullOrEmpty(password))
            {
                IsPasswordPromptVisible = true;
                StatusMessage = _credentialStore.IsAvailable
                    ? "Inserire la password."
                    : "Keyring di sistema non disponibile: la password va inserita a ogni connessione.";
                return;
            }

            await DisposeClientAsync();
            var client = _clientFactory(SelectedProfile);
            var error = await client.ConnectAsync(SelectedProfile, password, CancellationToken.None);

            if (error is not null)
            {
                await client.DisposeAsync();
                ErrorMessage = error.Message;
                if (error.Kind == RemoteErrorKind.HostKeyMismatch)
                    PendingFingerprint = error.Fingerprint;
                return;
            }

            _client = client;
            IsConnected = true;
            IsPasswordPromptVisible = false;

            if (!string.IsNullOrEmpty(PasswordInput) && SavePassword && _credentialStore.IsAvailable)
                await _credentialStore.SetPasswordAsync(SelectedProfile.Id, PasswordInput);
            PasswordInput = null;

            CurrentPath = "/";
            DestinationFolder = SelectedProfile.LastDestinationFolder;
            // Chiamata interna: IsBusy è già alzata da questo metodo.
            await LoadListingCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        // Senza questa guardia la disconnessione libererebbe il client mentre un elenco o un
        // download lo stanno ancora usando, lasciando l'operazione in corso senza connessione.
        if (IsBusy || IsDownloading)
            return;

        await DisposeClientAsync();
        IsConnected = false;
        Items.Clear();
        VisibleItems.Clear();   // è la collezione mostrata dalla lista: senza questo resterebbe a video
        StatusMessage = "Disconnesso.";
    }

    public async Task OpenDirectoryAsync(RemoteEntryViewModel entry)
    {
        if (!entry.IsDirectory || _client is null || IsBusy || IsDownloading)
            return;

        CurrentPath = entry.Item.FullPath;
        await LoadListingAsync();
    }

    public async Task NavigateUpAsync()
    {
        if (_client is null || IsBusy || IsDownloading || CurrentPath == "/")
            return;

        int lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadListingAsync();
    }

    public Task RefreshAsync() => _client is null ? Task.CompletedTask : LoadListingAsync();

    public async Task AcceptFingerprintAsync()
    {
        if (SelectedProfile is null || PendingFingerprint is null)
            return;

        SelectedProfile.AcceptedHostKeyFingerprint = PendingFingerprint;
        PendingFingerprint = null;
        await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        await ConnectAsync();
    }

    public void RejectFingerprint()
    {
        PendingFingerprint = null;
        StatusMessage = "Connessione rifiutata: host key non accettata.";
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
        if (_client is null || IsBusy || IsDownloading)
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
            RefreshLocalStatuses();
            // Persiste la destinazione appena usata (LastDestinationFolder del profilo).
            await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        }
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
    /// Elenco richiesto dai comandi di navigazione: ignora la richiesta se un'operazione è già
    /// in corso, così due comandi ravvicinati non si sovrappongono sullo stesso client.
    /// </summary>
    private async Task LoadListingAsync()
    {
        if (_client is null || IsBusy || IsDownloading)
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

        RefreshLocalStatuses();
        StatusMessage = $"{Items.Count} elementi in {CurrentPath}";
        RebuildVisibleItems();
    }

    /// <summary>Ricalcola la colonna "Su disco" per i file di primo livello.</summary>
    protected void RefreshLocalStatuses()
    {
        foreach (var entry in Items)
        {
            if (entry.IsDirectory || string.IsNullOrWhiteSpace(DestinationFolder))
            {
                entry.LocalStatus = null;
                continue;
            }
            entry.LocalStatus = DownloadService.GetLocalStatus(
                entry.Item, Path.Combine(DestinationFolder, entry.Name));
        }
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    /// <summary>Client corrente (per il task download).</summary>
    protected IRemoteFileClient? Client => _client;

    /// <summary>Percorso del file profili (per il task download/editor).</summary>
    protected string ProfilesFilePath => _profilesFilePath;
}
