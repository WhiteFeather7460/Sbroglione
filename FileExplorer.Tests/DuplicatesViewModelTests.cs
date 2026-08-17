using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class DuplicatesViewModelTests : IDisposable
{
    private readonly string _root;

    public DuplicatesViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-dupvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ScanAsync_FindsGroups_AndUpdatesStatus()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "x1.txt"), "doppio");
        await File.WriteAllTextAsync(Path.Combine(_root, "x2.txt"), "doppio");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();

        var group = Assert.Single(vm.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.False(vm.IsScanning);
        Assert.Equal("1 gruppi di duplicati", vm.StatusText);
    }

    [Fact]
    public async Task DeleteFileAsync_RemovesFileFromDiskAndDissolvesPair()
    {
        string keep = Path.Combine(_root, "k.txt");
        string remove = Path.Combine(_root, "r.txt");
        await File.WriteAllTextAsync(keep, "doppio");
        await File.WriteAllTextAsync(remove, "doppio");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();
        var target = Assert.Single(vm.Groups).Files.First(f => f.FilePath == remove);

        await vm.DeleteFileAsync(target);

        Assert.False(File.Exists(remove));
        Assert.True(File.Exists(keep));
        Assert.Empty(vm.Groups); // rimasto un solo file: gruppo dissolto
    }

    [Fact]
    public async Task KeepFirstAsync_DeletesAllButFirst()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "triplo");
        await File.WriteAllTextAsync(Path.Combine(_root, "b.txt"), "triplo");
        await File.WriteAllTextAsync(Path.Combine(_root, "c.txt"), "triplo");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();
        var group = Assert.Single(vm.Groups);
        string first = group.Files[0].FilePath;

        await vm.KeepFirstAsync(group);

        Assert.True(File.Exists(first));
        Assert.Single(Directory.GetFiles(_root)); // sopravvive solo il primo
        Assert.Empty(vm.Groups);
    }
}
