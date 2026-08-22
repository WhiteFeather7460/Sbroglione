using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class LocalPaneViewModelTests : IDisposable
{
    private readonly string _root;

    public LocalPaneViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sbroglione-localpane-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task RefreshAsync_ListsFilesAndFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_root, "f.txt"), "x");
        var vm = new LocalPaneViewModel(_root);

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task NavigateToAsync_UpdatesCurrentPathAndReloads()
    {
        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        var vm = new LocalPaneViewModel(_root);
        await vm.RefreshAsync();

        await vm.NavigateToAsync(sub);

        Assert.Equal(sub, vm.CurrentPath);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task NavigateUpAsync_GoesToParent()
    {
        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        var vm = new LocalPaneViewModel(sub);
        await vm.RefreshAsync();

        await vm.NavigateUpAsync();

        Assert.Equal(_root, vm.CurrentPath);
    }

    [Fact]
    public async Task CreateFolderAsync_CreatesAndRefreshes()
    {
        var vm = new LocalPaneViewModel(_root);
        await vm.RefreshAsync();

        await vm.CreateFolderAsync("nuova");

        Assert.Null(vm.ErrorMessage);
        Assert.True(Directory.Exists(Path.Combine(_root, "nuova")));
        Assert.Contains(vm.Items, i => i.Name == "nuova");
    }

    [Fact]
    public async Task CreateFolderAsync_AlreadyExists_SetsErrorMessage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "esistente"));
        var vm = new LocalPaneViewModel(_root);
        await vm.RefreshAsync();

        await vm.CreateFolderAsync("esistente");

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task RenameSelectedAsync_RenamesSelectedItem()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vecchio"));
        var vm = new LocalPaneViewModel(_root);
        await vm.RefreshAsync();
        vm.SelectedItem = vm.Items.Single(i => i.Name == "vecchio");

        await vm.RenameSelectedAsync("nuovo");

        Assert.Null(vm.ErrorMessage);
        Assert.True(Directory.Exists(Path.Combine(_root, "nuovo")));
        Assert.Contains(vm.Items, i => i.Name == "nuovo");
    }

    [Fact]
    public async Task DeleteSelectedAsync_DeletesSelectedItem()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dacancellare"));
        var vm = new LocalPaneViewModel(_root);
        await vm.RefreshAsync();
        vm.SelectedItem = vm.Items.Single(i => i.Name == "dacancellare");

        await vm.DeleteSelectedAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(_root, "dacancellare")));
        Assert.Empty(vm.Items);
    }
}
