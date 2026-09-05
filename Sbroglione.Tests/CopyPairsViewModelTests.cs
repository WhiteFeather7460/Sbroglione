using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class CopyPairsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;
    private readonly string _originalJournalPath;
    private readonly string _originalProfilesPath;
    private readonly string _originalLanguage = LocalizationService.CurrentLanguage;

    public CopyPairsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copypairs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
        AppSettingsStore.Current = new AppSettings();
        _originalJournalPath = CopyJournalStore.CurrentPath;
        CopyJournalStore.CurrentPath = Path.Combine(_root, "copy-journal.json");
        _originalProfilesPath = CopyProfileStore.CurrentPath;
        CopyProfileStore.CurrentPath = Path.Combine(_root, "copy-profiles.json");
        // Senza loop del dispatcher i Post andrebbero persi: esecuzione sincrona nei test.
        UiDispatch.Override = action => action();
        // Asserzioni sotto assumono stringhe IT: LocalizationService.CurrentLanguage è
        // stato statico condiviso, non garantito dall'ordine di esecuzione dei test.
        LocalizationService.Apply(LocalizationService.Italian);
    }

    public void Dispose()
    {
        UiDispatch.Override = null;
        LocalizationService.Apply(_originalLanguage);
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        CopyJournalStore.CurrentPath = _originalJournalPath;
        CopyProfileStore.CurrentPath = _originalProfilesPath;
        InputDialogHelper.Override = null;
        ConfirmDialogHelper.Override = null;
        NetworkCredentialDialogHelper.Override = null;
        NetworkCredentialConnectorFactory.OverrideFactory = null;
        FileSystemService.CheckUncRootAccessOverride = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// I callback di avanzamento arrivano da threadpool e in parallelo: il servizio può
    /// consegnare cumulativi fuori ordine (prima 6, poi 5). Il clamp lato contabilità deve
    /// scartare quello stantio, così l'avanzamento pubblicato non torna mai indietro.
    /// </summary>
    [Fact]
    public void DirectoryProgress_StaleCumulativeReport_DoesNotRegressProgress()
    {
        var pair = new FolderFilePairViewModel();
        var destination = new DestinationProgressViewModel("/dest");
        var tracker = new SpeedTracker(() => 0);      // orologio fermo: fuori scopo qui
        var publisher = new CopyPairsViewModel.DirectoryCopyProgressPublisher(
            pair, destination, tracker, new UiProgressThrottle(TimeSpan.Zero));   // niente throttle nei test

        publisher.Report(new CopyProgress(CopiedBytes: 6, TotalBytes: 10, TotalFiles: 3));
        Assert.Equal(string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), 3), destination.Status);
        Assert.Equal(0.6, destination.Progress, 3);

        publisher.Report(new CopyProgress(CopiedBytes: 5, TotalBytes: 10, TotalFiles: 3));
        Assert.Equal(0.6, destination.Progress, 3);          // cumulativo stantio: ignorato

        publisher.Report(new CopyProgress(CopiedBytes: 9, TotalBytes: 10, TotalFiles: 3));
        Assert.Equal(0.9, destination.Progress, 3);
        Assert.Equal(3, publisher.KnownFileCount);
    }

    /// <summary>
    /// Anche due Post partiti in ordine possono essere eseguiti fuori ordine dal
    /// dispatcher: il secondo clamp, quello lato UI, deve tenere il massimo.
    /// </summary>
    [Fact]
    public void DirectoryProgress_UiPostsRunOutOfOrder_KeepHighestProgress()
    {
        var posted = new List<Action>();
        UiDispatch.Override = posted.Add;             // marshaling differito, non inline

        var pair = new FolderFilePairViewModel();
        var destination = new DestinationProgressViewModel("/dest");
        var tracker = new SpeedTracker(() => 0);
        var publisher = new CopyPairsViewModel.DirectoryCopyProgressPublisher(
            pair, destination, tracker, new UiProgressThrottle(TimeSpan.Zero));

        publisher.Report(new CopyProgress(CopiedBytes: 4, TotalBytes: 10, TotalFiles: 2));
        publisher.Report(new CopyProgress(CopiedBytes: 8, TotalBytes: 10, TotalFiles: 2));
        Assert.Equal(0, destination.Progress);               // nulla applicato finché i Post non girano

        posted.Reverse();                             // il dispatcher li esegue al contrario
        foreach (Action action in posted)
            action();

        Assert.Equal(0.8, destination.Progress, 3);          // mai regredito a 0.4
        Assert.Equal(string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), 2), destination.Status);
    }

    [Fact]
    public async Task StartCopy_Directory_OneDestinationFails_OtherSucceedsAndPairIsError()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceDir = Path.Combine(_root, "dir-fail-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");

        string goodDestination = Path.Combine(_root, "dir-fail-good");
        // A differenza di una cartella padre mancante (che farebbe fallire la pre-creazione
        // in StartCopyAsync prima ancora di raggiungere CopyDirectoryToManyAsync), questa
        // cartella destinazione esiste già: la pre-creazione la trova e non fa nulla. Il file
        // "a.txt" al suo interno viene invece tenuto aperto con handle esclusivo, quindi solo
        // la copia del singolo file dentro questa destinazione fallisce con IOException.
        string badDestination = Path.Combine(_root, "dir-fail-bad");
        Directory.CreateDirectory(badDestination);
        string badFile = Path.Combine(badDestination, "a.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = goodDestination };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, badDestination));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        using (new FileStream(badFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await vm.StartCopyAsync(pair);
        }

        Assert.True(File.Exists(Path.Combine(goodDestination, "a.txt")));
        var goodEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == goodDestination);
        var badEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == badDestination);
        Assert.Equal(CopyStateKind.Success, goodEntry.StateKind);
        Assert.Equal(CopyStateKind.Error, badEntry.StateKind);
        Assert.Equal(CopyStateKind.Error, pair.StateKind);
    }

    [Fact]
    public async Task StartCopy_SingleFile_ChecksumEnabled_VerifiesAndMarksSuccess()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceFile = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "contenuto di prova");
        string destinationFile = Path.Combine(_root, "dest.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destinationFile };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal("contenuto di prova", await File.ReadAllTextAsync(destinationFile));
    }

    [Fact]
    public async Task StartCopy_SingleFile_ChecksumDisabled_SkipsVerificationButCopiesFile()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "source2.txt");
        await File.WriteAllTextAsync(sourceFile, "altro contenuto");
        string destinationFile = Path.Combine(_root, "dest2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destinationFile };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Null(pair.IsVerified);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.Completed"), pair.Status);
        Assert.Equal("altro contenuto", await File.ReadAllTextAsync(destinationFile));
    }

    [Fact]
    public async Task StartCopy_Directory_CopiesAllFilesWithResolvedParallelism()
    {
        string sourceDir = Path.Combine(_root, "srcdir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "b");
        string destinationDir = Path.Combine(_root, "dstdir");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(File.Exists(Path.Combine(destinationDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(destinationDir, "b.txt")));
    }

    [Fact]
    public async Task StartCopy_WhitelistExtensionFilter_OnlyCopiesMatchingFiles()
    {
        string sourceRoot = Path.Combine(_root, "vm-wl-src");
        string destinationRoot = Path.Combine(_root, "vm-wl-dst");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.jpg"), "img");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.txt"), "text");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceRoot,
            DestinationPath = destinationRoot,
            ExtensionFilterMode = ExtensionFilterMode.Whitelist,
            ExtensionFilterText = "jpg"
        };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.True(File.Exists(Path.Combine(destinationRoot, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(destinationRoot, "b.txt")));
    }

    [Fact]
    public async Task StartCopy_Directory_WhitelistFilterChecksumEnabled_VerifiesFilteredTreeAndMarksSuccess()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceDir = Path.Combine(_root, "vwl-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.jpg"), "img");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "text");
        string destinationDir = Path.Combine(_root, "vwl-dst");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceDir,
            DestinationPath = destinationDir,
            ExtensionFilterMode = ExtensionFilterMode.Whitelist,
            ExtensionFilterText = "jpg"
        };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.True(File.Exists(Path.Combine(destinationDir, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(destinationDir, "b.txt")));
    }

    [Fact]
    public async Task StartCopy_Directory_ChecksumEnabled_VerifiesTreeAndMarksSuccess()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceDir = Path.Combine(_root, "vsrc");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "b.txt"), "bbb");
        string destinationDir = Path.Combine(_root, "vdst");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal(string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedVerifiedFormat"), 2), pair.Status);
    }

    [Fact]
    public async Task StartCopy_Directory_ChecksumDisabled_SkipsVerification()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceDir = Path.Combine(_root, "vsrc2");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string destinationDir = Path.Combine(_root, "vdst2");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Null(pair.IsVerified);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.Completed"), pair.Status);
    }

    [Fact]
    public async Task StartCopy_SingleFile_WithExtraDestination_CopiesToBoth()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "multi-src.txt");
        await File.WriteAllTextAsync(sourceFile, "multi");
        string primary = Path.Combine(_root, "multi-d1.txt");
        string extra = Path.Combine(_root, "multi-d2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = primary };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Equal("multi", await File.ReadAllTextAsync(primary));
        Assert.Equal("multi", await File.ReadAllTextAsync(extra));
    }

    [Fact]
    public async Task StartCopy_SingleFile_WithExtraDestination_ChecksumEnabled_VerifiesAllDestinations()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceFile = Path.Combine(_root, "multi-vf-src.txt");
        await File.WriteAllTextAsync(sourceFile, "verifica multi");
        string primary = Path.Combine(_root, "multi-vf-d1.txt");
        string extra = Path.Combine(_root, "multi-vf-d2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = primary };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.Completed"), pair.Status);
        Assert.Equal("verifica multi", await File.ReadAllTextAsync(extra));
    }

    [Fact]
    public async Task StartCopy_SingleFile_WithExtraDestination_PopulatesDestinationsProgress()
    {
        string sourceFile = Path.Combine(_root, "dp-source.txt");
        await File.WriteAllTextAsync(sourceFile, "dati");
        string destination1 = Path.Combine(_root, "dp-dest1.txt");
        string destination2 = Path.Combine(_root, "dp-dest2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destination1 };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, destination2));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(2, pair.DestinationsProgress.Count);
        Assert.All(pair.DestinationsProgress, d => Assert.Equal(CopyStateKind.Success, d.StateKind));
        Assert.All(pair.DestinationsProgress, d => Assert.Equal(1, d.Progress, 3));
        Assert.Equal(CopyStateKind.Success, pair.StateKind);
    }

    [Fact]
    public async Task StartCopy_SingleFile_SetsFinalPairSpeedText()
    {
        // Issue 2 (final whole-branch review): una copia di file singolo veloce può finire
        // prima che RecomputePairAggregate scatti mai (nessuno snapshot di velocità
        // pubblicato), quindi pair.SpeedText deve essere impostato esplicitamente a fine
        // copia, come già fa CopyDirectoryAsync — altrimenti la riga di velocità non compare
        // mai per questo percorso.
        string sourceFile = Path.Combine(_root, "speed-source.txt");
        await File.WriteAllTextAsync(sourceFile, "dati di prova per la velocità finale");
        string destination = Path.Combine(_root, "speed-dest.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destination };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.NotNull(pair.SpeedText);
    }

    [Fact]
    public async Task StartCopy_SingleFile_OneDestinationUnwritable_MarksThatDestinationErrorAndPairError()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "dp-fail-source.txt");
        await File.WriteAllTextAsync(sourceFile, "dati");
        string goodDestination = Path.Combine(_root, "dp-fail-good.txt");
        // Destinazione bloccata da un handle esclusivo tenuto aperto per tutta la copia:
        // FileCopyService.CopyFileToManyAsync apre ogni destinazione con
        // new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None),
        // quindi questo file "in uso" fa fallire solo questa destinazione con IOException.
        // A differenza di una cartella padre mancante, questo fallimento sopravvive alla
        // pre-creazione delle cartelle in StartCopyAsync (il file esiste già, la cartella
        // padre è _root che esiste già) e al redirect "destinazione è una cartella" di
        // CopySingleFileAsync (qui la destinazione è un file, non una cartella, quindi non
        // c'è alcun redirect verso un percorso interno che tornerebbe scrivibile).
        string badDestination = Path.Combine(_root, "dp-fail-locked.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = goodDestination };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, badDestination));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        using (new FileStream(badDestination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await vm.StartCopyAsync(pair);
        }

        var goodEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == goodDestination);
        var badEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == badDestination);
        Assert.Equal(CopyStateKind.Success, goodEntry.StateKind);
        Assert.Equal(CopyStateKind.Error, badEntry.StateKind);
        Assert.NotNull(badEntry.ErrorMessage);
        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal("dati", await File.ReadAllTextAsync(goodDestination));
    }

    [Fact]
    public async Task StartCopy_Directory_WithExtraDestination_VerifiesEveryDestination()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceDir = Path.Combine(_root, "multi-vsrc");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string primary = Path.Combine(_root, "multi-vd1");
        string extra = Path.Combine(_root, "multi-vd2");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = primary };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal("aaa", await File.ReadAllTextAsync(Path.Combine(extra, "a.txt")));
    }

    [Fact]
    public async Task Constructor_JournalWithLeftoverRecord_RestoresInterruptedPair()
    {
        string sourceDir = Path.Combine(_root, "jrn-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");

        await CopyJournalStore.AddAsync(new CopyJobRecord
        {
            SourcePath = sourceDir,
            DestinationPath = Path.Combine(_root, "jrn-dst"),
            ExtraDestinations = { Path.Combine(_root, "jrn-dst2") }
        });

        var vm = new CopyPairsViewModel();
        await vm.JournalRestore;

        var pair = Assert.Single(vm.PathPairs);
        Assert.Equal(sourceDir, pair.SourcePath);
        Assert.Equal(CopyStateKind.Warning, pair.StateKind);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.Interrupted"), pair.Status);
        Assert.True(pair.SkipUnchanged);
        Assert.Single(pair.ExtraDestinations);
        Assert.Empty(await CopyJournalStore.LoadAsync()); // journal svuotato dopo il ripristino
    }

    [Fact]
    public async Task StartCopy_SuccessfulCopy_LeavesJournalEmpty()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "jrn-file.txt");
        await File.WriteAllTextAsync(sourceFile, "contenuto");
        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceFile,
            DestinationPath = Path.Combine(_root, "jrn-file-dst.txt")
        };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.JournalRestore;
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Empty(await CopyJournalStore.LoadAsync());
    }

    [Fact]
    public async Task ThrottleEnabled_RoundTripsThroughSettings()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleEnabled = true;
        Assert.True(AppSettingsStore.Current.ThrottleEnabled);
        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;

        viewModel.ThrottleEnabled = false;
        Assert.False(AppSettingsStore.Current.ThrottleEnabled);
        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;
    }

    [Fact]
    public async Task ThrottleMBps_ClampsToRange()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleMBps = 5000;
        Assert.Equal(1000, AppSettingsStore.Current.ThrottleMBps);
        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;

        viewModel.ThrottleMBps = 0;
        Assert.Equal(1, AppSettingsStore.Current.ThrottleMBps);
        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;
    }

    [Fact]
    public async Task ThrottleSetters_ExposeAwaitableSaveTask()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleEnabled = !viewModel.ThrottleEnabled;

        Assert.NotNull(viewModel.LastSaveTask);
        await viewModel.LastSaveTask!;
    }

    [Fact]
    public void Dispose_UnsubscribesFromThrottleChanged()
    {
        var vm = new CopyPairsViewModel();
        vm.Dispose();

        bool raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(CopyPairsViewModel.ThrottleEnabled);
        AppSettingsStore.RaiseThrottleChanged();

        Assert.False(raised);
    }

    [Fact]
    public void ThrottleChangedFromSettings_RaisesPropertyChangedOnCopyPairs()
    {
        var copyViewModel = new CopyPairsViewModel();
        var settingsViewModel = new SettingsViewModel();

        var raised = new List<string?>();
        copyViewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        settingsViewModel.ThrottleEnabled = !settingsViewModel.ThrottleEnabled;

        Assert.Contains(nameof(CopyPairsViewModel.ThrottleEnabled), raised);
        Assert.Contains(nameof(CopyPairsViewModel.ThrottleMBps), raised);
    }

    [Fact]
    public async Task CopyDirectory_UpdatesSpeedText()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fe-speed-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(tempDir, "src");
        string destination = Path.Combine(tempDir, "dst");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[512 * 1024]);

        try
        {
            var viewModel = new CopyPairsViewModel();
            var pair = new FolderFilePairViewModel { SourcePath = source, DestinationPath = destination };
            await pair.SourceStateRefresh;

            await viewModel.StartCopyAsync(pair);

            // A copia finita il testo velocità riporta la media (formato "media …/s").
            Assert.NotNull(pair.SpeedText);
            Assert.Contains("media", pair.SpeedText!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SimulatePair_PopulatesSummary()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fe-simcmd-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(tempDir, "src");
        string destination = Path.Combine(tempDir, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[10]);

        try
        {
            var viewModel = new CopyPairsViewModel();
            var pair = new FolderFilePairViewModel { SourcePath = source, DestinationPath = destination };
            await pair.SourceStateRefresh;

            await viewModel.SimulatePairAsync(pair);

            Assert.NotNull(pair.SimulationSummary);
            Assert.Contains("1 file", pair.SimulationSummary!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FormatEta_OverOneDay_ShowsDays()
    {
        double twoDays = 2 * 24 * 3600 + 3 * 3600 + 4 * 60 + 5; // 2g 3:04:05
        Assert.Equal("2g 3:04:05", CopyPairsViewModel.FormatEta(twoDays));
    }

    [Fact]
    public async Task Constructor_LoadsPersistedProfilesSortedByName()
    {
        await CopyProfileStore.SaveAsync(new[]
        {
            new CopyProfile { Name = "beta" },
            new CopyProfile { Name = "Alfa" }
        });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        Assert.Equal(new[] { "Alfa", "beta" }, vm.Profiles.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveProfile_CreatesProfileAndPersistsIt()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Backup foto");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var pair = new FolderFilePairViewModel
        {
            SourcePath = Path.Combine(_root, "src"),
            DestinationPath = Path.Combine(_root, "dst"),
            SkipUnchanged = true
        };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, Path.Combine(_root, "extra")));
        vm.PathPairs.Add(pair);

        await vm.SaveProfileAsync();

        var profile = Assert.Single(vm.Profiles);
        Assert.Equal("Backup foto", profile.Name);
        Assert.Same(profile, vm.SelectedProfile);
        var stored = Assert.Single(profile.Pairs);
        Assert.Equal(pair.SourcePath, stored.SourcePath);
        Assert.Equal(pair.DestinationPath, stored.DestinationPath);
        Assert.Equal(Path.Combine(_root, "extra"), Assert.Single(stored.ExtraDestinations));
        Assert.True(stored.SkipUnchanged);

        List<CopyProfile> persisted = await CopyProfileStore.LoadAsync();
        Assert.Equal("Backup foto", Assert.Single(persisted).Name);
    }

    [Fact]
    public async Task SaveProfile_PersistsExtensionFilter()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Filtrato");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var pair = new FolderFilePairViewModel
        {
            SourcePath = Path.Combine(_root, "src"),
            DestinationPath = Path.Combine(_root, "dst"),
            ExtensionFilterMode = ExtensionFilterMode.Whitelist,
            ExtensionFilterText = "jpg,png"
        };
        vm.PathPairs.Add(pair);

        await vm.SaveProfileAsync();

        var profile = Assert.Single(vm.Profiles);
        var stored = Assert.Single(profile.Pairs);
        Assert.Equal(ExtensionFilterMode.Whitelist, stored.ExtensionFilterMode);
        Assert.Equal("jpg,png", stored.ExtensionFilterText);
    }

    [Fact]
    public async Task SaveProfile_InsertsAlphabeticallyBetweenExistingProfiles()
    {
        await CopyProfileStore.SaveAsync(new[]
        {
            new CopyProfile { Name = "Alfa" },
            new CopyProfile { Name = "Zeta" }
        });

        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Mid");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/a", DestinationPath = "/b" });
        await vm.SaveProfileAsync();

        Assert.Equal(new[] { "Alfa", "Mid", "Zeta" }, vm.Profiles.Select(p => p.Name));

        List<CopyProfile> persisted = await CopyProfileStore.LoadAsync();
        Assert.Equal(new[] { "Alfa", "Mid", "Zeta" }, persisted.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveProfile_SameName_OverwritesExistingProfile()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Sync progetti");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/a", DestinationPath = "/b" });
        await vm.SaveProfileAsync();

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/c", DestinationPath = "/d" });
        await vm.SaveProfileAsync();

        var profile = Assert.Single(vm.Profiles);
        Assert.Equal("Sync progetti", profile.Name);
        Assert.Equal(2, profile.Pairs.Count);
    }

    [Fact]
    public async Task SaveProfile_CancelledDialog_DoesNothing()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>(null);

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/a", DestinationPath = "/b" });

        await vm.SaveProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task ApplyProfile_ReplacesPathPairs()
    {
        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/vecchia", DestinationPath = "/coppia" });

        var profile = new CopyProfile
        {
            Name = "Preset",
            Pairs =
            {
                new CopyProfilePair
                {
                    SourcePath = "/src1",
                    DestinationPath = "/dst1",
                    ExtraDestinations = { "/extra1" },
                    SkipUnchanged = true
                },
                new CopyProfilePair { SourcePath = "/src2", DestinationPath = "/dst2" }
            }
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ApplyProfile();

        Assert.Equal(2, vm.PathPairs.Count);
        Assert.Equal("/src1", vm.PathPairs[0].SourcePath);
        Assert.Equal("/dst1", vm.PathPairs[0].DestinationPath);
        Assert.True(vm.PathPairs[0].SkipUnchanged);
        Assert.Equal("/extra1", Assert.Single(vm.PathPairs[0].ExtraDestinations).Path);
        Assert.Equal("/src2", vm.PathPairs[1].SourcePath);
    }

    [Fact]
    public async Task ApplyProfile_RestoresExtensionFilter()
    {
        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var profile = new CopyProfile
        {
            Name = "Preset filtro",
            Pairs =
            {
                new CopyProfilePair
                {
                    SourcePath = "/src1",
                    DestinationPath = "/dst1",
                    ExtensionFilterMode = ExtensionFilterMode.Blacklist,
                    ExtensionFilterText = "tmp"
                }
            }
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ApplyProfile();

        var restored = Assert.Single(vm.PathPairs);
        Assert.Equal(ExtensionFilterMode.Blacklist, restored.ExtensionFilterMode);
        Assert.Equal("tmp", restored.ExtensionFilterText);
    }

    [Fact]
    public async Task ApplyProfile_PairIsCopying_DoesNotReplacePairs()
    {
        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var copying = new FolderFilePairViewModel
        {
            SourcePath = "/vecchia",
            DestinationPath = "/coppia",
            IsCopying = true
        };
        vm.PathPairs.Add(copying);

        var profile = new CopyProfile
        {
            Name = "Preset",
            Pairs = { new CopyProfilePair { SourcePath = "/s", DestinationPath = "/d" } }
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ApplyProfile();

        Assert.Same(copying, Assert.Single(vm.PathPairs));
    }

    [Fact]
    public async Task DeleteProfile_Confirmed_RemovesAndPersists()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(true);

        await CopyProfileStore.SaveAsync(new[] { new CopyProfile { Name = "Da eliminare" } });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.SelectedProfile = Assert.Single(vm.Profiles);

        await vm.DeleteProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task DeleteProfile_NotConfirmed_KeepsProfile()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);

        await CopyProfileStore.SaveAsync(new[] { new CopyProfile { Name = "Da tenere" } });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.SelectedProfile = Assert.Single(vm.Profiles);

        await vm.DeleteProfileAsync();

        Assert.Single(vm.Profiles);
        Assert.NotNull(vm.SelectedProfile);
    }

    #region Accesso UNC / credenziali di rete

    private sealed class FakeConnector : INetworkCredentialConnector
    {
        public bool IsSupported { get; init; } = true;
        public Func<string, string, string, bool, int> ConnectFunc { get; init; } = (_, _, _, _) => 0;
        public int ConnectCallCount { get; private set; }
        public List<(string Root, string Username, string Password, bool Persist)> Calls { get; } = new();

        public int Connect(string uncRoot, string username, string password, bool persist)
        {
            ConnectCallCount++;
            Calls.Add((uncRoot, username, password, persist));
            return ConnectFunc(uncRoot, username, password, persist);
        }
    }

    [Fact]
    public async Task EnsureUncAccess_NonUncPaths_SkipsCheckEntirely()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => throw new InvalidOperationException(
            "non deve essere chiamato per percorsi non UNC");

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"C:\local\source", @"D:\local\dest" });

        Assert.True(result);
    }

    [Fact]
    public async Task EnsureUncAccess_AccessOk_ReturnsTrue_NoPrompt()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.Ok);
        NetworkCredentialDialogHelper.Override = _ => throw new InvalidOperationException(
            "non deve chiedere credenziali se l'accesso è già ok");

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.True(result);
    }

    /// <summary>
    /// Unavailable non è un problema di credenziali (server spento, nome errato): nessun
    /// prompt, il fallimento reale emerge poi dall'operazione vera.
    /// </summary>
    [Fact]
    public async Task EnsureUncAccess_Unavailable_ReturnsTrue_NoPrompt()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.Unavailable);
        NetworkCredentialDialogHelper.Override = _ => throw new InvalidOperationException(
            "non deve chiedere credenziali se la radice è irraggiungibile");

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.True(result);
    }

    [Fact]
    public async Task EnsureUncAccess_ConnectorNotSupported_SkipsPromptAndReturnsTrue()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        NetworkCredentialConnectorFactory.OverrideFactory = () => new FakeConnector { IsSupported = false };
        NetworkCredentialDialogHelper.Override = _ => throw new InvalidOperationException(
            "non deve chiedere credenziali se il connector non è supportato (non-Windows)");

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.True(result); // comportamento invariato fuori da Windows.
    }

    [Fact]
    public async Task EnsureUncAccess_AccessDenied_UserCancels_ReturnsFalseWithErrorStatus()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        NetworkCredentialConnectorFactory.OverrideFactory = () => new FakeConnector();
        NetworkCredentialDialogHelper.Override = _ => Task.FromResult<NetworkCredentialResult?>(null);

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.False(result);
        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsCancelled"), pair.Status);
    }

    [Fact]
    public async Task EnsureUncAccess_AccessDenied_ConnectFails_ReturnsFalseWithErrorStatus()
    {
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        var connector = new FakeConnector { ConnectFunc = (_, _, _, _) => 1326 }; // ERROR_LOGON_FAILURE
        NetworkCredentialConnectorFactory.OverrideFactory = () => connector;
        NetworkCredentialDialogHelper.Override = _ =>
            Task.FromResult<NetworkCredentialResult?>(new NetworkCredentialResult("user", "wrong", false));

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.False(result);
        Assert.Equal(1, connector.ConnectCallCount);
        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal(
            string.Format(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsFailedFormat"), 1326),
            pair.Status);
    }

    [Fact]
    public async Task EnsureUncAccess_AccessDenied_ConnectSucceedsButRetryStillDenied_ReturnsFalse_NoSecondPrompt()
    {
        int checkCallCount = 0;
        FileSystemService.CheckUncRootAccessOverride = _ =>
        {
            checkCallCount++;
            return Task.FromResult(UncAccessResult.AccessDenied); // negato anche dopo la connessione
        };
        var connector = new FakeConnector();
        NetworkCredentialConnectorFactory.OverrideFactory = () => connector;
        int promptCallCount = 0;
        NetworkCredentialDialogHelper.Override = _ =>
        {
            promptCallCount++;
            return Task.FromResult<NetworkCredentialResult?>(new NetworkCredentialResult("user", "pass", false));
        };

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.False(result);
        Assert.Equal(1, connector.ConnectCallCount);
        Assert.Equal(1, promptCallCount); // un solo tentativo: niente loop di prompt.
        Assert.Equal(2, checkCallCount); // probe iniziale + un retry dopo la connessione.
        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsFailed"), pair.Status);
    }

    [Fact]
    public async Task EnsureUncAccess_AccessDenied_ConnectSucceedsAndRetryOk_ReturnsTrue()
    {
        var checkResults = new Queue<UncAccessResult>(new[] { UncAccessResult.AccessDenied, UncAccessResult.Ok });
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(checkResults.Dequeue());
        var connector = new FakeConnector();
        NetworkCredentialConnectorFactory.OverrideFactory = () => connector;
        NetworkCredentialDialogHelper.Override = _ =>
            Task.FromResult<NetworkCredentialResult?>(new NetworkCredentialResult("user", "pass", true));

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" });

        Assert.True(result);
        Assert.Equal(1, connector.ConnectCallCount);
        Assert.Equal((@"\\server\share", "user", "pass", true), Assert.Single(connector.Calls));
        Assert.Empty(checkResults); // entrambi i probe consumati.
    }

    /// <summary>
    /// Nessuna cache "già connesso in questa sessione": ogni chiamata rifà il probe, così
    /// credenziali scadute o cambiate lato server rifanno scattare il prompt subito dopo.
    /// </summary>
    [Fact]
    public async Task EnsureUncAccess_CalledTwice_RechecksEveryTime_NoSessionCache()
    {
        int checkCallCount = 0;
        FileSystemService.CheckUncRootAccessOverride = _ =>
        {
            checkCallCount++;
            return Task.FromResult(UncAccessResult.Ok);
        };

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        Assert.True(await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" }));
        Assert.True(await vm.EnsureUncAccessAsync(pair, new[] { @"\\server\share\file.txt" }));

        Assert.Equal(2, checkCallCount);
    }

    [Fact]
    public async Task EnsureUncAccess_MultipleUncRootsAmongPaths_ChecksEachDistinctRootOnce()
    {
        var checkedRoots = new List<string>();
        FileSystemService.CheckUncRootAccessOverride = root =>
        {
            checkedRoots.Add(root);
            return Task.FromResult(UncAccessResult.Ok);
        };

        var vm = new CopyPairsViewModel();
        var pair = new FolderFilePairViewModel();

        bool result = await vm.EnsureUncAccessAsync(pair, new[]
        {
            @"\\server\share\a.txt",
            @"\\server\share\sub\b.txt", // stessa radice di sopra: non ricontrollata due volte.
            @"\\other\share2\c.txt",
            @"C:\local\d.txt" // non UNC: ignorato.
        });

        Assert.True(result);
        Assert.Equal(new[] { @"\\server\share", @"\\other\share2" }, checkedRoots);
    }

    /// <summary>
    /// End-to-end: con una destinazione UNC negata e il prompt annullato, StartCopyAsync
    /// esce prima di qualsiasi I/O. La sorgente è locale ed esistente (altrimenti CanStart
    /// sarebbe false e ci si fermerebbe prima, su un ramo diverso da quello sotto test).
    /// </summary>
    [Fact]
    public async Task EnsureUncAccess_ReturnsFalse_AbortsStartCopyBeforeTouchingFileSystem()
    {
        string sourceDir = Path.Combine(_root, "unc-abort-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string localDestination = Path.Combine(_root, "unc-abort-dest"); // volutamente inesistente

        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        NetworkCredentialConnectorFactory.OverrideFactory = () => new FakeConnector();
        NetworkCredentialDialogHelper.Override = _ => Task.FromResult<NetworkCredentialResult?>(null);

        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceDir,
            DestinationPath = localDestination
        };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, @"\\server\share\dest"));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();

        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsCancelled"), pair.Status);
        Assert.False(pair.IsCopying);
        // Prove che nessuna operazione reale è partita: niente cartella destinazione creata,
        // niente voce di journal scritta, nessun avanzamento per destinazione.
        Assert.False(Directory.Exists(localDestination));
        Assert.Empty(await CopyJournalStore.LoadAsync());
        Assert.Empty(pair.DestinationsProgress);
    }

    /// <summary>
    /// Stesso end-to-end su SimulatePairAsync: si esce prima ancora di impostare lo stato
    /// "Simulazione in corso" e senza produrre alcun riepilogo.
    /// </summary>
    [Fact]
    public async Task EnsureUncAccess_ReturnsFalse_AbortsSimulateBeforeTouchingFileSystem()
    {
        string sourceDir = Path.Combine(_root, "unc-abort-sim-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string localDestination = Path.Combine(_root, "unc-abort-sim-dest");

        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        NetworkCredentialConnectorFactory.OverrideFactory = () => new FakeConnector();
        NetworkCredentialDialogHelper.Override = _ => Task.FromResult<NetworkCredentialResult?>(null);

        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceDir,
            DestinationPath = localDestination
        };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, @"\\server\share\dest"));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();

        await vm.SimulatePairAsync(pair);

        Assert.Equal(CopyStateKind.Error, pair.StateKind);
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsCancelled"), pair.Status);
        Assert.Null(pair.SimulationSummary);
        Assert.False(Directory.Exists(localDestination));
    }

    /// <summary>
    /// Task 5: il probe UNC deve girare PRIMA del gate CanStart. Una sorgente UNC con
    /// SourceExists ancora false (perché il refresh in background usa File/Directory.Exists,
    /// indistinguibile da "accesso negato") deve comunque arrivare al prompt di rete.
    /// </summary>
    [Fact]
    public async Task EnsureUncAccess_RunsBeforeCanStartGate_SoUncSourceReachesPrompt()
    {
        // Sorgente UNC "inesistente" solo perché SourceExists non è ancora stato aggiornato
        // dopo la connessione: il probe UNC deve girare PRIMA del controllo CanStart, non dopo.
        FileSystemService.CheckUncRootAccessOverride = _ => Task.FromResult(UncAccessResult.AccessDenied);
        NetworkCredentialConnectorFactory.OverrideFactory = () => new FakeConnector();
        NetworkCredentialDialogHelper.Override = _ => Task.FromResult<NetworkCredentialResult?>(null); // annulla

        string destinationFile = Path.Combine(_root, "dest-from-unc.txt");
        var pair = new FolderFilePairViewModel
        {
            SourcePath = @"\\server\share\source.txt",
            DestinationPath = destinationFile
        };
        await pair.SourceStateRefresh; // SourceExists diventa false: il path UNC non è raggiungibile da qui

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        // Deve fallire per il motivo di rete (credenziali annullate), non per "percorsi non validi":
        // prova che EnsureUncAccessAsync gira prima del gate CanStart.
        Assert.Equal(LocalizationService.Tr("Str.CopyPairs.NetworkCredentialsCancelled"), pair.Status);
        Assert.NotEqual(LocalizationService.Tr("Str.CopyPairs.InvalidPaths"), pair.Status);
    }

    #endregion
}
