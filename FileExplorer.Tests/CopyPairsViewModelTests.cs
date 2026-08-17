using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class CopyPairsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;

    public CopyPairsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copypairs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
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
}
