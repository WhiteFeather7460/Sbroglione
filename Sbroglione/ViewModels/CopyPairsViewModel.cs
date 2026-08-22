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
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>
/// Scheda "Copia file": gestisce la lista di coppie sorgente/destinazione e
/// avvia/annulla le copie con verifica checksum.
/// </summary>
public class CopyPairsViewModel : ViewModelBase, IDisposable
{
    private readonly Action _throttleChangedHandler;

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

        _throttleChangedHandler = () =>
        {
            this.RaisePropertyChanged(nameof(ThrottleEnabled));
            this.RaisePropertyChanged(nameof(ThrottleMBps));
        };
        AppSettingsStore.ThrottleChanged += _throttleChangedHandler;

        JournalRestore = RestoreInterruptedJobsAsync();
        ProfilesLoad = LoadProfilesAsync();
    }

    /// <summary>
    /// Rimuove l'handler dall'evento statico <see cref="AppSettingsStore.ThrottleChanged"/>:
    /// senza questo, ogni istanza resterebbe rootata per sempre (leak).
    /// </summary>
    public void Dispose()
    {
        AppSettingsStore.ThrottleChanged -= _throttleChangedHandler;
        GC.SuppressFinalize(this);
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
                Status = LocalizationService.Tr("Str.CopyPairs.Interrupted"),
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
            LocalizationService.Tr("Str.CopyPairs.SaveProfileTitle"),
            LocalizationService.Tr("Str.CopyPairs.SaveProfileMessage"),
            SelectedProfile?.Name);
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
            LocalizationService.Tr("Str.CopyPairs.DeleteProfileTitle"),
            string.Format(LocalizationService.Tr("Str.CopyPairs.DeleteProfileMessageFormat"), profile.Name),
            LocalizationService.Tr("Str.Common.Delete"));
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
            pair.Status = LocalizationService.Tr("Str.CopyPairs.InvalidPaths");
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        IReadOnlyList<string> destinations = pair.AllDestinations;

        if (pair.ClearDestinationBeforeCopy)
        {
            bool confirmed = await ConfirmDialogHelper.ShowAsync(
                LocalizationService.Tr("Str.CopyPairs.ClearDestinationTitle"),
                string.Format(
                    LocalizationService.Tr("Str.CopyPairs.ClearDestinationMessageFormat"),
                    string.Join(Environment.NewLine, destinations)),
                LocalizationService.Tr("Str.Common.Delete"));
            if (!confirmed)
                return;

            foreach (var destination in destinations)
                await FileCopyService.ClearDirectoryContentsAsync(destination, CancellationToken.None);
        }

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
            pair.Status = LocalizationService.Tr("Str.CopyPairs.CopyInProgress");
            pair.StateKind = CopyStateKind.Copying;
            pair.IsVerified = null;
            pair.SimulationSummary = null;
            pair.SpeedText = null;
            pair.SpeedSamples = null;
            foreach (var item in pair.FilesToProcess)
                item.Status = FileCopyStatus.Pending;
            pair.DestinationsProgress.Clear();
            foreach (var destination in destinations)
                pair.DestinationsProgress.Add(new DestinationProgressViewModel(destination));

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
            pair.Status = LocalizationService.Tr("Str.Common.Cancelled");
            pair.StateKind = CopyStateKind.Cancelled;
        }
        catch (Exception ex)
        {
            pair.Status = string.Format(LocalizationService.Tr("Str.Common.ErrorFormat"), ex.Message);
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
            pair.Status = LocalizationService.Tr("Str.CopyPairs.InvalidPaths");
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        IReadOnlyList<string> destinations = pair.AllDestinations;
        pair.Status = LocalizationService.Tr("Str.CopyPairs.Simulating");

        try
        {
            var result = await CopySimulationService.SimulateAsync(
                pair.SourcePath!, destinations, pair.SkipUnchanged, CancellationToken.None);

            var lines = new List<string>
            {
                string.Format(LocalizationService.Tr("Str.CopyPairs.SimulateToCopyFormat"), result.TotalFiles, SizeFormatter.Format(result.TotalBytes)) +
                (result.SkippedFiles > 0 ? string.Format(LocalizationService.Tr("Str.CopyPairs.SimulateSkippedFormat"), result.SkippedFiles) : string.Empty)
            };

            foreach (var destination in result.Destinations)
            {
                string space = destination.FreeBytes is null
                    ? LocalizationService.Tr("Str.CopyPairs.FreeSpaceUnknown")
                    : string.Format(LocalizationService.Tr("Str.CopyPairs.FreeSpaceFormat"), SizeFormatter.Format(destination.FreeBytes.Value)) +
                      (destination.Fits == false ? LocalizationService.Tr("Str.CopyPairs.NotEnoughSpace") : string.Empty);
                lines.Add(string.Format(LocalizationService.Tr("Str.CopyPairs.OverwriteLineFormat"), destination.Root, destination.OverwriteCount, space));
            }

            pair.SimulationSummary = string.Join(Environment.NewLine, lines);

            bool anyDoesNotFit = result.Destinations.Any(d => d.Fits == false);
            pair.Status = anyDoesNotFit
                ? LocalizationService.Tr("Str.CopyPairs.SimulateNotEnoughSpace")
                : LocalizationService.Tr("Str.CopyPairs.SimulateComplete");
            pair.StateKind = anyDoesNotFit ? CopyStateKind.Warning : CopyStateKind.Ready;
        }
        catch (FileNotFoundException)
        {
            // CopySimulationService.Simulate: sorgente assente. Il percorso è già noto qui
            // (pair.SourcePath), niente bisogno di ex.Message — che porta comunque un testo
            // diagnostico non tradotto (confine Service→ViewModel, vedi commento nel Service).
            pair.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.SimulateSourceNotFoundFormat"), pair.SourcePath);
            pair.StateKind = CopyStateKind.Error;
        }
        catch (Exception ex)
        {
            pair.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.SimulateErrorFormat"), ex.Message);
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
        pair.SpeedText = string.Format(
            LocalizationService.Tr("Str.CopyPairs.SpeedSummaryFormat"),
            FormatSpeed(snapshot.CurrentBytesPerSecond),
            FormatSpeed(snapshot.AverageBytesPerSecond),
            FormatSpeed(snapshot.PeakBytesPerSecond),
            FormatEta(snapshot.EtaSeconds));
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

        // DestinationsProgress è stata popolata da StartCopyAsync nello stesso ordine di
        // `destinations`: gli indici corrispondono a `destinationFiles`.
        var vmByResolvedPath = new Dictionary<string, DestinationProgressViewModel>();
        var trackers = new Dictionary<string, (SpeedTracker Tracker, MonotonicProgressGate TrackerGate, MonotonicProgressGate UiGate, UiProgressThrottle UiThrottle, StrongCopiedBytes CopiedBytes)>();
        for (int i = 0; i < destinations.Count; i++)
        {
            var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            tracker.Start(totalBytes);
            vmByResolvedPath[destinationFiles[i]] = pair.DestinationsProgress[i];
            trackers[destinationFiles[i]] = (tracker, new MonotonicProgressGate(), new MonotonicProgressGate(), new UiProgressThrottle(), new StrongCopiedBytes());
        }

        var copyResult = await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, (destinationFile, deltaBytes) =>
        {
            var (tracker, trackerGate, uiGate, uiThrottle, copiedBytes) = trackers[destinationFile];
            long total = Interlocked.Add(ref copiedBytes.Value, deltaBytes);
            if (!trackerGate.TryAdvance(total))
                return;

            tracker.Report(total);
            bool haveSnapshot = tracker.TryTakeSnapshot(out SpeedSnapshot snapshot);
            if (!haveSnapshot && !uiThrottle.ShouldPublish())
                return;

            double fraction = totalBytes > 0 ? (double)total / totalBytes : 1;
            var target = vmByResolvedPath[destinationFile];
            UiDispatch.Post(() =>
            {
                if (uiGate.TryAdvance(fraction))
                    target.Progress = fraction;
                if (haveSnapshot)
                    PublishDestinationSpeed(pair, target, snapshot);
                RecomputePairAggregate(pair);
            });
        }, ct, AppSettingsStore.Current.BufferSizeBytes);

        foreach (var destinationFile in copyResult.SucceededDestinations)
        {
            var target = vmByResolvedPath[destinationFile];
            var tracker = trackers[destinationFile].Tracker;
            target.SpeedText = string.Format(
                LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"),
                FormatSpeed(tracker.AverageBytesPerSecond),
                FormatSpeed(tracker.PeakBytesPerSecond));
            target.StateKind = CopyStateKind.Success;
        }

        foreach (var (destinationFile, error) in copyResult.FailedDestinations)
        {
            var target = vmByResolvedPath[destinationFile];
            target.StateKind = CopyStateKind.Error;
            target.ErrorMessage = error.Message;
            target.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.DestinationErrorFormat"), error.Message);
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.StateKind = AggregatePairState(pair);
            pair.Status = pair.StateKind == CopyStateKind.Error
                ? string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"), copyResult.SucceededDestinations.Count, destinations.Count)
                : LocalizationService.Tr("Str.CopyPairs.Completed");
            return;
        }

        // Verifica checksum solo sulle destinazioni riuscite.
        pair.Status = LocalizationService.Tr("Str.CopyPairs.VerifyingChecksum");
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);

        bool allMatch = true;
        foreach (var destinationFile in copyResult.SucceededDestinations)
        {
            string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
            pair.DestinationChecksum = destinationHash;
            bool matches = string.Equals(pair.SourceChecksum, destinationHash, StringComparison.OrdinalIgnoreCase);
            allMatch &= matches;
            if (!matches)
                vmByResolvedPath[destinationFile].StateKind = CopyStateKind.Warning;
        }

        pair.IsVerified = allMatch && copyResult.FailedDestinations.Count == 0;
        pair.Progress = 1;
        pair.StateKind = AggregatePairState(pair);
        pair.Status = pair.StateKind switch
        {
            CopyStateKind.Error => string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"), copyResult.SucceededDestinations.Count, destinations.Count),
            CopyStateKind.Warning => LocalizationService.Tr("Str.CopyPairs.CompletedChecksumMismatch"),
            _ => LocalizationService.Tr("Str.CopyPairs.Completed")
        };
    }

    /// <summary>Contenitore per un contatore byte mutabile per destinazione, target di <see cref="Interlocked.Add(ref long, long)"/>.</summary>
    private sealed class StrongCopiedBytes
    {
        public long Value;
    }

    private static void PublishDestinationSpeed(FolderFilePairViewModel pair, DestinationProgressViewModel target, SpeedSnapshot snapshot)
    {
        target.SpeedText = string.Format(
            LocalizationService.Tr("Str.CopyPairs.SpeedSummaryFormat"),
            FormatSpeed(snapshot.CurrentBytesPerSecond),
            FormatSpeed(snapshot.AverageBytesPerSecond),
            FormatSpeed(snapshot.PeakBytesPerSecond),
            FormatEta(snapshot.EtaSeconds));
        target.CurrentBytesPerSecond = snapshot.CurrentBytesPerSecond;
    }

    /// <summary>Ricalcola gli aggregati del pair dalle sue destinazioni: la più lenta pilota il progresso, la somma la velocità mostrata.</summary>
    private static void RecomputePairAggregate(FolderFilePairViewModel pair)
    {
        if (pair.DestinationsProgress.Count == 0)
            return;

        pair.Progress = pair.DestinationsProgress.Min(d => d.Progress);
        double totalSpeed = pair.DestinationsProgress.Sum(d => d.CurrentBytesPerSecond);
        pair.SpeedText = totalSpeed > 0
            ? string.Format(LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"), FormatSpeed(totalSpeed), FormatSpeed(totalSpeed))
            : pair.SpeedText;
    }

    /// <summary>Priorità Error > Warning > Success sulle destinazioni del pair.</summary>
    private static CopyStateKind AggregatePairState(FolderFilePairViewModel pair)
    {
        if (pair.DestinationsProgress.Any(d => d.StateKind == CopyStateKind.Error))
            return CopyStateKind.Error;
        if (pair.DestinationsProgress.Any(d => d.StateKind == CopyStateKind.Warning))
            return CopyStateKind.Warning;
        return CopyStateKind.Success;
    }

    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        var publisher = new DirectoryCopyProgressPublisher(pair, tracker);

        // Lookup per aggiornare lo stato per-file nella lista "File da elaborare": vuoto
        // (no-op) se l'Expander non è mai stato aperto, FilesToProcess non è ancora caricata.
        var filesByPath = pair.FilesToProcess.ToDictionary(f => f.FullPath, f => f);

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in destinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        // Stopgap analogo a quello introdotto per CopyFileToManyAsync (Task 1): i callback di
        // CopyDirectoryToManyAsync sono ora per-destinazione, ma questo metodo aggiorna un solo
        // widget di stato per la coppia. Fino a quando questa vista non gestirà destinazioni
        // multiple con progresso indipendente (task successivo), filtriamo sulla prima
        // destinazione, che riceve gli stessi eventi, nello stesso ordine, delle altre.
        string firstDestination = destinations[0];
        await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: (destination, progress) =>
            {
                if (destination == firstDestination)
                    publisher.Report(progress);
            },
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged,
            onFileStarted: (destination, sourceFile) =>
            {
                if (destination != firstDestination)
                    return;
                UiDispatch.Post(() =>
                {
                    // filesByPath è vuoto se l'Expander "File da elaborare" non è mai stato
                    // aperto: il widget "In copia adesso" deve funzionare comunque, quindi
                    // qui costruiamo un item al volo invece di dipendere da quel listing.
                    var item = filesByPath.TryGetValue(sourceFile, out var existing)
                        ? existing
                        : new FileSystemItem { Name = Path.GetFileName(sourceFile), FullPath = sourceFile };
                    item.Status = FileCopyStatus.Copying;
                    pair.CopyingFiles.Add(item);
                });
            },
            onFileCompleted: (destination, sourceFile) =>
            {
                if (destination != firstDestination)
                    return;
                if (filesByPath.TryGetValue(sourceFile, out var item))
                    UiDispatch.Post(() => item.Status = FileCopyStatus.Done);
                UiDispatch.Post(() =>
                {
                    var toRemove = pair.CopyingFiles.FirstOrDefault(f => f.FullPath == sourceFile);
                    if (toRemove is not null)
                        pair.CopyingFiles.Remove(toRemove);
                });
            });

        int knownFileCount = publisher.KnownFileCount;
        if (knownFileCount > 0)
            pair.SpeedText = string.Format(
                LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"),
                FormatSpeed(tracker.AverageBytesPerSecond),
                FormatSpeed(tracker.PeakBytesPerSecond));

        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = LocalizationService.Tr("Str.CopyPairs.Completed");
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        pair.Status = LocalizationService.Tr("Str.CopyPairs.VerifyingChecksum");
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in destinations)
        {
            // Un gate e un throttle per destinazione: il contatore verificati riparte da 1
            // a ogni destinazione, quindi lo stato monotono non va condiviso tra i cicli.
            var verifyThrottle = new UiProgressThrottle();
            var verifyGate = new MonotonicProgressGate();

            var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
                pair.SourcePath!,
                destination,
                parallelism,
                progress =>
                {
                    if (!verifyThrottle.ShouldPublish())
                        return;

                    int verified = progress.VerifiedFiles;
                    int total = progress.TotalFiles;
                    UiDispatch.Post(() =>
                    {
                        if (verifyGate.TryAdvance(verified))
                            pair.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.VerifyingChecksumProgressFormat"), verified, total);
                    });
                },
                ct);

            totalVerified = verifyResult.TotalFiles;
            mismatchedTotal += verifyResult.MismatchedFiles.Count;
            missingTotal += verifyResult.MissingFiles.Count;
        }

        pair.Progress = 1;
        pair.IsVerified = mismatchedTotal == 0 && missingTotal == 0;

        if (pair.IsVerified == true)
        {
            pair.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedVerifiedFormat"), totalVerified);
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.VerifyFailedFormat"), mismatchedTotal, missingTotal);
            pair.StateKind = CopyStateKind.Warning;
        }
    }

    /// <summary>
    /// Contabilità e pubblicazione del progresso di una copia cartella: clamp monotono,
    /// tracker di velocità, throttle e marshaling sul thread UI in un punto solo.
    /// Classe (e non lambda) per avere un seam testabile: i callback del servizio arrivano
    /// da threadpool e in parallelo, quindi i cumulativi possono presentarsi fuori ordine
    /// (prima 6, poi 5) — condizione impossibile da provocare in modo deterministico
    /// passando da una copia reale.
    /// Un'istanza per copia.
    /// </summary>
    internal sealed class DirectoryCopyProgressPublisher
    {
        private readonly FolderFilePairViewModel _pair;
        private readonly SpeedTracker _tracker;
        private readonly UiProgressThrottle _uiThrottle;
        private readonly MonotonicProgressGate _trackerGate = new();
        private readonly MonotonicProgressGate _uiGate = new();
        private int _knownFileCount = -1;

        /// <param name="uiThrottle">
        /// Solo per i test: un throttle senza attesa fa passare ogni report, così le
        /// asserzioni riguardano il clamp e non la finestra temporale del throttle.
        /// </param>
        public DirectoryCopyProgressPublisher(
            FolderFilePairViewModel pair,
            SpeedTracker tracker,
            UiProgressThrottle? uiThrottle = null)
        {
            _pair = pair;
            _tracker = tracker;
            _uiThrottle = uiThrottle ?? new UiProgressThrottle();
        }

        /// <summary>Numero di file annunciato dal primo report; -1 se non è ancora arrivato.</summary>
        public int KnownFileCount => Volatile.Read(ref _knownFileCount);

        public void Report(CopyProgress progress)
        {
            // I callback arrivano da threadpool e in parallelo: il first-report deve
            // vincere una sola volta (altrimenti tracker.Start girerebbe più volte).
            bool firstReport = Interlocked.CompareExchange(ref _knownFileCount, progress.TotalFiles, -1) == -1;
            if (firstReport)
                _tracker.Start(progress.TotalBytes);

            // Cumulativi fuori ordine (Interlocked.Add e Invoke non sono atomici tra
            // loro nel servizio): scartati, così il tracker non torna indietro.
            bool advanced = _trackerGate.TryAdvance(progress.CopiedBytes);
            if (advanced)
                _tracker.Report(progress.CopiedBytes);

            SpeedSnapshot snapshot = default;
            bool haveSnapshot = advanced && _tracker.TryTakeSnapshot(out snapshot);
            if (!firstReport && !haveSnapshot && (!advanced || !_uiThrottle.ShouldPublish()))
                return;

            double fraction = progress.Fraction;
            int totalFiles = progress.TotalFiles;
            FolderFilePairViewModel pair = _pair;
            MonotonicProgressGate uiGate = _uiGate;
            UiDispatch.Post(() =>
            {
                if (firstReport)
                    pair.Status = totalFiles == 0
                        ? LocalizationService.Tr("Str.CopyPairs.NoFilesToCopy")
                        : string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), totalFiles);
                // Secondo clamp lato UI: anche due Post partiti in ordine possono essere
                // eseguiti fuori ordine dal dispatcher.
                if (advanced && uiGate.TryAdvance(fraction))
                    pair.Progress = fraction;
                if (haveSnapshot)
                    PublishSpeed(pair, snapshot);
            });
        }
    }
}
