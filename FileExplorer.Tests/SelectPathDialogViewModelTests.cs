using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class SelectPathDialogViewModelTests : IDisposable
{
    private readonly string _root;

    public SelectPathDialogViewModelTests()
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
    public async Task RefreshAsync_ValidDirectory_PopulatesItemsWithoutError()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var vm = new SelectPathDialogViewModel(directoriesOnly: true, startPath: _root);
        await vm.RefreshAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsLoading);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task RefreshAsync_MissingPath_SetsErrorMessageAndClearsItems()
    {
        var vm = new SelectPathDialogViewModel(directoriesOnly: true, startPath: Path.Combine(_root, "nope"));
        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task NavigateToAsync_UpdatesCurrentPathAndReloads()
    {
        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);

        var vm = new SelectPathDialogViewModel(directoriesOnly: true, startPath: _root);
        await vm.NavigateToAsync(sub);

        Assert.Equal(sub, vm.CurrentPath);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task RefreshAsync_UncPathOnNonWindows_SuggestsMounting()
    {
        if (OperatingSystem.IsWindows())
            return; // Su Windows i percorsi UNC sono supportati direttamente.

        var vm = new SelectPathDialogViewModel(directoriesOnly: true, startPath: @"\\server\share");
        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("mont", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
