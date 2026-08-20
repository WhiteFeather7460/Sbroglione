using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class CopyPairsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;
    private readonly string _originalJournalPath;
    private readonly string _originalProfilesPath;

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
    }

    public void Dispose()
    {
        UiDispatch.Override = null;
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        CopyJournalStore.CurrentPath = _originalJournalPath;
        CopyProfileStore.CurrentPath = _originalProfilesPath;
        InputDialogHelper.Override = null;
        ConfirmDialogHelper.Override = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
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
        Assert.Equal("Completato", pair.Status);
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
        Assert.Equal("Completato e verificato (2 file)", pair.Status);
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
        Assert.Equal("Completato", pair.Status);
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
        Assert.Equal("Completato", pair.Status);
        Assert.Equal("verifica multi", await File.ReadAllTextAsync(extra));
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
        Assert.Equal("Interrotto — premere Avvia per riprendere", pair.Status);
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
}
