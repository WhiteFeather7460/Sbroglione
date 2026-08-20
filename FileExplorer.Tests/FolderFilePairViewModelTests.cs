using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class FolderFilePairViewModelTests : IDisposable
{
    private readonly string _root;

    public FolderFilePairViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public async Task ExistingSource_EnablesCanStart()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "hello");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = file,
            DestinationPath = Path.Combine(_root, "dest")
        };
        await pair.SourceStateRefresh;

        Assert.True(pair.SourceExists);
        Assert.True(pair.CanStart);
    }

    [Fact]
    public async Task MissingSource_DisablesCanStart()
    {
        var pair = new FolderFilePairViewModel
        {
            SourcePath = Path.Combine(_root, "nope.txt"),
            DestinationPath = Path.Combine(_root, "dest")
        };
        await pair.SourceStateRefresh;

        Assert.False(pair.SourceExists);
        Assert.False(pair.CanStart);
    }

    [Fact]
    public async Task DirectorySource_LoadsFilesToProcess()
    {
        string dir = Path.Combine(_root, "src");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "b");

        var pair = new FolderFilePairViewModel { SourcePath = dir, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        Assert.Equal(2, pair.FilesToProcess.Count);
    }

    [Fact]
    public async Task FilesToProcess_NotLoadedUntilExpanded()
    {
        string dir = Path.Combine(_root, "src1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir };
        await pair.SourceStateRefresh;
        Assert.Empty(pair.FilesToProcess);             // niente listing finché l'Expander è chiuso

        pair.IsFilesExpanded = true;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task FilesToProcess_ReloadsOnSourceChangeWhileExpanded()
    {
        string dir1 = Path.Combine(_root, "src2");
        string dir2 = Path.Combine(_root, "src3");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "b.txt"), "x");

        var pair = new FolderFilePairViewModel { SourcePath = dir1, IsFilesExpanded = true };
        await pair.SourceStateRefresh;
        await pair.FilesLoad;

        pair.SourcePath = dir2;
        await pair.SourceStateRefresh;
        await pair.FilesLoad;
        Assert.Single(pair.FilesToProcess);
    }

    [Fact]
    public async Task SameSourceAndDestination_DisablesCanStart()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "hello");

        var pair = new FolderFilePairViewModel
        {
            SourcePath = file,
            DestinationPath = file
        };
        await pair.SourceStateRefresh;

        Assert.False(pair.CanStart);
    }
}
