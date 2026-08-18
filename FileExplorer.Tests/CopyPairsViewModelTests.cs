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
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        CopyJournalStore.CurrentPath = _originalJournalPath;
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
    public void ThrottleEnabled_RoundTripsThroughSettings()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleEnabled = true;
        Assert.True(AppSettingsStore.Current.ThrottleEnabled);

        viewModel.ThrottleEnabled = false;
        Assert.False(AppSettingsStore.Current.ThrottleEnabled);
    }

    [Fact]
    public void ThrottleMBps_ClampsToRange()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleMBps = 5000;
        Assert.Equal(1000, AppSettingsStore.Current.ThrottleMBps);

        viewModel.ThrottleMBps = 0;
        Assert.Equal(1, AppSettingsStore.Current.ThrottleMBps);
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
}
