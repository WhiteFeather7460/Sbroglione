using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Copia file": gestisce la lista di coppie sorgente/destinazione e
/// avvia/annulla le copie con verifica checksum.
/// </summary>
public class CopyPairsViewModel : ViewModelBase
{
    public ObservableCollection<FolderFilePairViewModel> PathPairs { get; } = new();

    /// <summary>True se c'è almeno una coppia in lista (pilota l'empty state).</summary>
    public bool HasPairs => PathPairs.Count > 0;

    private readonly Dictionary<FolderFilePairViewModel, CancellationTokenSource> _ctsByPair = new();

    public ReactiveCommand<Unit, Unit> AddPairCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseSourceCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseDestinationCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> StartCopyCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> CancelCopyCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> AddExtraDestinationCommand { get; }
    public ReactiveCommand<ExtraDestinationViewModel, Unit> RemoveExtraDestinationCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> SimulateCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }

    /// <summary>Profili di copia salvati, ordinati per nome.</summary>
    public ObservableCollection<CopyProfile> Profiles { get; } = new();

    private CopyProfile? _selectedProfile;

    /// <summary>Profilo selezionato nella barra profili (null se nessuno).</summary>
    public CopyProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }

    public CopyPairsViewModel()
    {
        PathPairs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasPairs));

        AddPairCommand = ReactiveCommand.Create(() => PathPairs.Add(new FolderFilePairViewModel()));

        BrowseSourceCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseDestinationAsync);

        // Start/Cancel: la validazione CanStart è valutata sulla singola riga via binding (IsEnabled).
        StartCopyCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(StartCopyAsync);
        CancelCopyCommand = ReactiveCommand.Create<FolderFilePairViewModel>(CancelCopy);

        AddExtraDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(AddExtraDestinationAsync);
        RemoveExtraDestinationCommand = ReactiveCommand.Create<ExtraDestinationViewModel>(
            extra => extra.Owner.ExtraDestinations.Remove(extra));

        SimulateCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(SimulatePairAsync);

        SaveProfileCommand = ReactiveCommand.CreateFromTask(SaveProfileAsync);
        ApplyProfileCommand = ReactiveCommand.Create(ApplyProfile);
        DeleteProfileCommand = ReactiveCommand.CreateFromTask(DeleteProfileAsync);

        AppSettingsStore.ThrottleChanged += () =>
        {
            this.RaisePropertyChanged(nameof(ThrottleEnabled));
            this.RaisePropertyChanged(nameof(ThrottleMBps));
        };

        JournalRestore = RestoreInterruptedJobsAsync();
        ProfilesLoad = LoadProfilesAsync();
    }

    /// <summary>
    /// Task del ripristino delle copie interrotte, avviato dal costruttore.
    /// I test lo attendono; la UI non ne ha bisogno.
    /// </summary>
    public Task JournalRestore { get; }

    /// <summary>
    /// Task del caricamento profili, avviato dal costruttore.
    /// I test lo attendono; la UI non ne ha bisogno.
    /// </summary>
    public Task ProfilesLoad { get; }

    /// <summary>Task dell'ultimo salvataggio profili. Solo per i test.</summary>
    internal Task? LastProfilesSaveTask { get; private set; }

    /// <summary>Toggle rapido del limite di banda (scrive le impostazioni, effetto immediato sulle copie in corso).</summary>
    public bool ThrottleEnabled
    {
        get => AppSettingsStore.Current.ThrottleEnabled;
        set
        {
            if (AppSettingsStore.Current.ThrottleEnabled == value)
                return;

            AppSettingsStore.Current.ThrottleEnabled = value;
            this.RaisePropertyChanged();
            AppSettingsStore.RaiseThrottleChanged();
            LastSaveTask = SaveSettingsBestEffortAsync();
        }
    }

    /// <summary>Limite MB/s modificabile al volo dalla scheda Copia.</summary>
    public int ThrottleMBps
    {
        get => AppSettingsStore.Current.ThrottleMBps;
        set
        {
            int clamped = Math.Clamp(value, 1, 1000);
            if (AppSettingsStore.Current.ThrottleMBps == clamped)
                return;

            AppSettingsStore.Current.ThrottleMBps = clamped;
            this.RaisePropertyChanged();
            AppSettingsStore.RaiseThrottleChanged();
            LastSaveTask = SaveSettingsBestEffortAsync();
        }
    }

    /// <summary>Task dell'ultimo salvataggio impostazioni avviato dai setter del throttle. Solo per i test.</summary>
    internal Task? LastSaveTask { get; private set; }

    private static async Task SaveSettingsBestEffortAsync()
    {
        try
        {
            await AppSettingsStore.SaveCurrentAsync();
        }
        catch (Exception)
        {
            // best effort: il limite resta attivo in memoria anche se il salvataggio fallisce.
        }
    }

    /// <summary>
    /// Ripropone come coppie "interrotte" le voci rimaste nel journal
    /// (copie in corso al momento di un crash/chiusura), poi svuota il journal.
    /// </summary>
    private async Task RestoreInterruptedJobsAsync()
    {
        List<CopyJobRecord> jobs = await CopyJournalStore.LoadAsync();
        if (jobs.Count == 0)
            return;

        foreach (var job in jobs)
        {
            var pair = new FolderFilePairViewModel
            {
                SourcePath = job.SourcePath,
                DestinationPath = job.DestinationPath,
                SkipUnchanged = true,
                Status = "Interrotto — premere Avvia per riprendere",
                StateKind = CopyStateKind.Warning
            };

            foreach (var extra in job.ExtraDestinations)
                pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));

            PathPairs.Add(pair);
        }

        try
        {
            // Svuotato solo dopo che le coppie sono state ripristinate in memoria:
            // un fallimento qui causa al più un'offerta duplicata al prossimo avvio.
            await CopyJournalStore.ClearAsync();
        }
        catch (Exception)
        {
            // best effort.
        }
    }

    private async Task LoadProfilesAsync()
    {
        List<CopyProfile> profiles = await CopyProfileStore.LoadAsync();
        foreach (var profile in profiles)
            Profiles.Add(profile);
    }

    /// <summary>
    /// Salva le coppie correnti come profilo: chiede il nome; se coincide (case-insensitive)
    /// con un profilo esistente lo sovrascrive, altrimenti lo inserisce mantenendo l'ordine.
    /// </summary>
    public async Task SaveProfileAsync()
    {
        await ProfilesLoad;

        string? name = await InputDialogHelper.ShowAsync(
            "Salva profilo", "Nome del profilo di copia:", SelectedProfile?.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();

        List<CopyProfilePair> pairs = PathPairs
            .Where(p => !string.IsNullOrWhiteSpace(p.SourcePath) || !string.IsNullOrWhiteSpace(p.DestinationPath))
            .Select(p => new CopyProfilePair
            {
                SourcePath = p.SourcePath ?? string.Empty,
                DestinationPath = p.DestinationPath ?? string.Empty,
                ExtraDestinations = p.ExtraDestinations.Select(e => e.Path).ToList(),
                SkipUnchanged = p.SkipUnchanged
            })
            .ToList();

        CopyProfile? existing = Profiles.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Pairs = pairs;
            SelectedProfile = existing;
        }
        else
        {
            var profile = new CopyProfile { Name = name, Pairs = pairs };
            int index = 0;
            while (index < Profiles.Count &&
                   StringComparer.OrdinalIgnoreCase.Compare(Profiles[index].Name, profile.Name) < 0)
                index++;
            Profiles.Insert(index, profile);
            SelectedProfile = profile;
        }

        LastProfilesSaveTask = SaveProfilesBestEffortAsync();
        await LastProfilesSaveTask;
    }

    /// <summary>
    /// Sostituisce le coppie correnti con quelle del profilo selezionato.
    /// No-op se nessun profilo è selezionato o se una copia è in corso.
    /// </summary>
    public void ApplyProfile()
    {
        if (SelectedProfile is not { } profile)
            return;

        if (PathPairs.Any(p => p.IsCopying))
            return; // nessuna sostituzione mentre una copia è in corso.

        PathPairs.Clear();
        foreach (var stored in profile.Pairs)
        {
            var pair = new FolderFilePairViewModel
            {
                SourcePath = stored.SourcePath,
                DestinationPath = stored.DestinationPath,
                SkipUnchanged = stored.SkipUnchanged
            };
            foreach (var extra in stored.ExtraDestinations)
                pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
            PathPairs.Add(pair);
        }
    }

    /// <summary>Elimina il profilo selezionato previa conferma.</summary>
    public async Task DeleteProfileAsync()
    {
        await ProfilesLoad;

        if (SelectedProfile is not { } profile)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Elimina profilo",
            $"Eliminare il profilo \"{profile.Name}\"?",
            "Elimina");
        if (!confirmed)
            return;

        Profiles.Remove(profile);
        SelectedProfile = null;

        LastProfilesSaveTask = SaveProfilesBestEffortAsync();
        await LastProfilesSaveTask;
    }

    private async Task SaveProfilesBestEffortAsync()
    {
        try
        {
            await CopyProfileStore.SaveAsync(Profiles.ToList());
        }
        catch (Exception)
        {
            // best effort: i profili restano in memoria anche se il salvataggio fallisce.
        }
    }

    private async Task BrowseSourceAsync(FolderFilePairViewModel pair)
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: false, pair.SourcePath);
        if (!string.IsNullOrEmpty(selected))
            pair.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, pair.DestinationPath);
        if (!string.IsNullOrEmpty(selected))
            pair.DestinationPath = selected;
    }

    private async Task AddExtraDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, pair.DestinationPath);
        if (!string.IsNullOrEmpty(selected))
            pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, selected));
    }

    public async Task StartCopyAsync(FolderFilePairViewModel pair)
    {
        if (!pair.CanStart)
        {
            pair.Status = "Percorsi non validi";
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        IReadOnlyList<string> destinations = pair.AllDestinations;

        var journalRecord = new CopyJobRecord
        {
            SourcePath = pair.SourcePath!,
            DestinationPath = pair.DestinationPath!,
            ExtraDestinations = pair.ExtraDestinations.Select(e => e.Path).ToList(),
            StartedUtc = DateTime.UtcNow
        };

        try
        {
            await CopyJournalStore.AddAsync(journalRecord);
        }
        catch (Exception)
        {
            // best effort: senza voce nel journal si perde solo l'offerta di ripresa dopo un crash.
        }

        var cts = new CancellationTokenSource();
        _ctsByPair[pair] = cts;

        try
        {
            // Le cartelle che conterranno le destinazioni vengono create in ogni caso
            // (in background: su percorsi di rete può bloccare).
            await Task.Run(() =>
            {
                foreach (var destination in destinations)
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            });

            pair.IsCopying = true;
            pair.Progress = 0;
            pair.Status = "Copia in corso…";
            pair.StateKind = CopyStateKind.Copying;
            pair.IsVerified = null;
            pair.SimulationSummary = null;
            pair.SpeedText = null;
            pair.SpeedSamples = null;

            if (await FileSystemService.GetPathTypeAsync(pair.SourcePath) == PathType.Directory)
            {
                // La copia di cartelle verifica il checksum dell'intero albero (se abilitato).
                await CopyDirectoryAsync(pair, destinations, cts.Token);
                return;
            }

            await CopySingleFileAsync(pair, destinations, cts.Token);
        }
        catch (OperationCanceledException)
        {
            pair.Status = "Annullato";
            pair.StateKind = CopyStateKind.Cancelled;
        }
        catch (Exception ex)
        {
            pair.Status = $"Errore: {ex.Message}";
            pair.StateKind = CopyStateKind.Error;
        }
        finally
        {
            try
            {
                await CopyJournalStore.RemoveAsync(journalRecord.Id);
            }
            catch (Exception)
            {
                // best effort: una voce residua causa solo una nuova offerta di ripresa al prossimo avvio.
            }

            pair.IsCopying = false;

            if (_ctsByPair.Remove(pair, out var toDispose))
                toDispose.Dispose();
        }
    }

    /// <summary>Dry-run della coppia: cosa verrebbe copiato, sovrascritto, saltato, e se lo spazio basta.</summary>
    [SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Metodo pubblico invocato da SimulateCommand con la stessa forma di istanza di " +
                        "StartCopyAsync/CancelCopy; renderlo static costringerebbe i test a chiamarlo per " +
                        "nome di tipo invece che sull'istanza del viewmodel, rompendo il pattern condiviso.")]
    public async Task SimulatePairAsync(FolderFilePairViewModel pair)
    {
        if (!pair.CanStart)
        {
            pair.Status = "Percorsi non validi";
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        IReadOnlyList<string> destinations = pair.AllDestinations;
        pair.Status = "Simulazione…";

        try
        {
            var result = await CopySimulationService.SimulateAsync(
                pair.SourcePath!, destinations, pair.SkipUnchanged, CancellationToken.None);

            var lines = new List<string>
            {
                $"Da copiare: {result.TotalFiles} file, {SizeFormatter.Format(result.TotalBytes)}" +
                (result.SkippedFiles > 0 ? $" (di cui {result.SkippedFiles} invariati, saltati)" : string.Empty)
            };

            foreach (var destination in result.Destinations)
            {
                string space = destination.FreeBytes is null
                    ? "spazio libero sconosciuto"
                    : $"liberi {SizeFormatter.Format(destination.FreeBytes.Value)}" +
                      (destination.Fits == false ? " — SPAZIO INSUFFICIENTE" : string.Empty);
                lines.Add($"{destination.Root}: {destination.OverwriteCount} sovrascritture, {space}");
            }

            pair.SimulationSummary = string.Join(Environment.NewLine, lines);

            bool anyDoesNotFit = result.Destinations.Any(d => d.Fits == false);
            pair.Status = anyDoesNotFit ? "Simulazione: spazio insufficiente" : "Simulazione completata";
            pair.StateKind = anyDoesNotFit ? CopyStateKind.Warning : CopyStateKind.Ready;
        }
        catch (Exception ex)
        {
            pair.Status = $"Errore simulazione: {ex.Message}";
            pair.StateKind = CopyStateKind.Error;
        }
    }

    private void CancelCopy(FolderFilePairViewModel pair)
    {
        if (_ctsByPair.TryGetValue(pair, out var cts))
            cts.Cancel();
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        $"{SizeFormatter.Format((long)bytesPerSecond)}/s";

    internal static string FormatEta(double? etaSeconds)
    {
        if (etaSeconds is null || !double.IsFinite(etaSeconds.Value))
            return "—";
        var time = TimeSpan.FromSeconds(Math.Min(etaSeconds.Value, TimeSpan.MaxValue.TotalSeconds - 1));
        if (time.TotalDays >= 1)
            return $"{(int)time.TotalDays}g {time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)}";
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void PublishSpeed(FolderFilePairViewModel pair, SpeedSnapshot snapshot)
    {
        pair.SpeedText =
            $"{FormatSpeed(snapshot.CurrentBytesPerSecond)} · media {FormatSpeed(snapshot.AverageBytesPerSecond)}" +
            $" · picco {FormatSpeed(snapshot.PeakBytesPerSecond)} · ETA {FormatEta(snapshot.EtaSeconds)}";
        pair.SpeedSamples = snapshot.Samples;
    }

    private static async Task CopySingleFileAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        // Se la sorgente è un file e una destinazione è una cartella, il file viene copiato dentro la cartella.
        var destinationFiles = new List<string>();
        foreach (var destination in destinations)
        {
            bool intoFolder = await FileSystemService.GetPathTypeAsync(destination) == PathType.Directory;
            destinationFiles.Add(intoFolder
                ? Path.Combine(destination, Path.GetFileName(pair.SourcePath!))
                : destination);
        }

        long totalBytes = new FileInfo(pair.SourcePath!).Length;
        long copiedBytes = 0;

        var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        tracker.Start(totalBytes);

        await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, deltaBytes =>
        {
            copiedBytes += deltaBytes;
            pair.Progress = totalBytes > 0 ? (double)copiedBytes / totalBytes : 1;
            tracker.Report(copiedBytes);
            if (tracker.TryTakeSnapshot(out var snapshot))
                PublishSpeed(pair, snapshot);
        }, ct, AppSettingsStore.Current.BufferSizeBytes);

        pair.SpeedText = $"media {FormatSpeed(tracker.AverageBytesPerSecond)} · picco {FormatSpeed(tracker.PeakBytesPerSecond)}";

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        // Verifica checksum di tutte le destinazioni.
        pair.Status = "Verifica checksum…";
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);

        bool allMatch = true;
        foreach (var destinationFile in destinationFiles)
        {
            string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
            pair.DestinationChecksum = destinationHash;
            allMatch &= string.Equals(pair.SourceChecksum, destinationHash, StringComparison.OrdinalIgnoreCase);
        }

        pair.IsVerified = allMatch;
        pair.Progress = 1;
        pair.Status = allMatch ? "Completato" : "Completato (checksum non corrisponde)";
        pair.StateKind = allMatch ? CopyStateKind.Success : CopyStateKind.Warning;
    }

    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        int knownFileCount = -1;
        var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in destinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: progress =>
            {
                if (knownFileCount != progress.TotalFiles)
                {
                    knownFileCount = progress.TotalFiles;
                    pair.Status = progress.TotalFiles == 0
                        ? "Nessun file da copiare"
                        : $"Copia cartella… ({progress.TotalFiles} file)";
                    tracker.Start(progress.TotalBytes);
                }

                pair.Progress = progress.Fraction;
                tracker.Report(progress.CopiedBytes);
                if (tracker.TryTakeSnapshot(out var snapshot))
                    PublishSpeed(pair, snapshot);
            },
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged);

        if (knownFileCount > 0)
            pair.SpeedText = $"media {FormatSpeed(tracker.AverageBytesPerSecond)} · picco {FormatSpeed(tracker.PeakBytesPerSecond)}";

        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        pair.Status = "Verifica checksum…";
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in destinations)
        {
            var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
                pair.SourcePath!,
                destination,
                parallelism,
                progress => pair.Status = $"Verifica checksum… ({progress.VerifiedFiles}/{progress.TotalFiles})",
                ct);

            totalVerified = verifyResult.TotalFiles;
            mismatchedTotal += verifyResult.MismatchedFiles.Count;
            missingTotal += verifyResult.MissingFiles.Count;
        }

        pair.Progress = 1;
        pair.IsVerified = mismatchedTotal == 0 && missingTotal == 0;

        if (pair.IsVerified == true)
        {
            pair.Status = $"Completato e verificato ({totalVerified} file)";
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {mismatchedTotal} file diversi, {missingTotal} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
    }
}
