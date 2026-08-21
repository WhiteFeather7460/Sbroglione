using System.Collections.ObjectModel;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class DuplicatesViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly Func<string, string, string, Task<bool>>? _previousOverride;

    public DuplicatesViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-dupvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousOverride = ConfirmDialogHelper.Override;
        // Senza loop del dispatcher i Post andrebbero persi: esecuzione sincrona nei test.
        UiDispatch.Override = action => action();
    }

    public void Dispose()
    {
        UiDispatch.Override = null;
        ConfirmDialogHelper.Override = _previousOverride;
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
        Assert.Equal(LocalizationService.Tr("Str.Duplicates.OneGroupFound"), vm.StatusText);
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

    [Fact]
    public async Task ConfirmAndDeleteFile_WhenDeclined_DoesNotDelete()
    {
        string file1 = Path.Combine(_root, "a.bin");
        string file2 = Path.Combine(_root, "b.bin");
        await File.WriteAllBytesAsync(file1, new byte[4]);
        await File.WriteAllBytesAsync(file2, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, "deadbeef", new[] { file1, file2 }.ToList()));
        viewModel.Groups.Add(group);

        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);

        await viewModel.ConfirmAndDeleteFileAsync(group.Files[0]);

        Assert.True(File.Exists(file1));
        Assert.Equal(2, group.Files.Count);
    }

    [Fact]
    public async Task ConfirmAndDeleteFile_WhenConfirmed_DeletesAndPassesFilePathInMessage()
    {
        string file1 = Path.Combine(_root, "c.bin");
        string file2 = Path.Combine(_root, "d.bin");
        await File.WriteAllBytesAsync(file1, new byte[4]);
        await File.WriteAllBytesAsync(file2, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, "deadbeef", new[] { file1, file2 }.ToList()));
        viewModel.Groups.Add(group);

        string? receivedMessage = null;
        ConfirmDialogHelper.Override = (_, message, _) =>
        {
            receivedMessage = message;
            return Task.FromResult(true);
        };

        await viewModel.ConfirmAndDeleteFileAsync(group.Files[0]);

        Assert.False(File.Exists(file1));
        Assert.NotNull(receivedMessage);
        Assert.Contains(file1, receivedMessage!);
    }

    [Fact]
    public async Task ConfirmAndKeepFirst_AsksOnceWithCount_AndDeletesRestOnConfirm()
    {
        string file1 = Path.Combine(_root, "k1.bin");
        string file2 = Path.Combine(_root, "k2.bin");
        string file3 = Path.Combine(_root, "k3.bin");
        foreach (var f in new[] { file1, file2, file3 })
            await File.WriteAllBytesAsync(f, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, "deadbeef", new[] { file1, file2, file3 }.ToList()));
        viewModel.Groups.Add(group);

        int calls = 0;
        ConfirmDialogHelper.Override = (_, message, _) =>
        {
            calls++;
            Assert.Contains("2 file", message);
            return Task.FromResult(true);
        };

        await viewModel.ConfirmAndKeepFirstAsync(group);

        Assert.Equal(1, calls);
        Assert.True(File.Exists(file1));
        Assert.False(File.Exists(file2));
        Assert.False(File.Exists(file3));
    }

    [Fact]
    public void HasGroups_TracksCollectionSwapAndRemoval()
    {
        var vm = new DuplicatesViewModel();
        Assert.False(vm.HasGroups);

        var group = new DuplicateGroupViewModel(new DuplicateGroup(10, "deadbeef", new[] { "/a/f1", "/b/f1" }));
        vm.Groups = new ObservableCollection<DuplicateGroupViewModel> { group };
        Assert.True(vm.HasGroups);

        vm.Groups.Remove(group);
        Assert.False(vm.HasGroups);          // il CollectionChanged della NUOVA collection è agganciato
    }
}
