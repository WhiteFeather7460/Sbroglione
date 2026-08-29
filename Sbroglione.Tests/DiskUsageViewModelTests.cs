using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class DiskUsageViewModelTests : IDisposable
{
    private readonly string _root;

    public DiskUsageViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-usagevm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // Senza loop del dispatcher i Post andrebbero persi: esecuzione sincrona nei test.
        UiDispatch.Override = action => action();
    }

    public void Dispose()
    {
        UiDispatch.Override = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ScanAsync_PopulatesCurrentNode()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "f.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();

        Assert.NotNull(vm.CurrentNode);
        Assert.Equal(_root, vm.CurrentNode!.FullPath);
        Assert.False(vm.IsScanning);
        Assert.False(vm.CanNavigateUp);
    }

    [Fact]
    public async Task DrillDownAndNavigateUp_MoveThroughTheTree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();
        var sub = vm.CurrentNode!.Children.Single(c => c.IsDirectory);

        vm.DrillDown(sub);
        Assert.Equal(sub, vm.CurrentNode);
        Assert.True(vm.CanNavigateUp);

        vm.NavigateUp();
        Assert.Equal(_root, vm.CurrentNode!.FullPath);
        Assert.False(vm.CanNavigateUp);
    }

    [Fact]
    public async Task DrillDown_OnFileNode_DoesNothing()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "f.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();
        var fileNode = vm.CurrentNode!.Children.Single();

        vm.DrillDown(fileNode);

        Assert.Equal(_root, vm.CurrentNode!.FullPath);
    }

    [Fact]
    public async Task ScanAsync_ShowsStructureBeforeFullScanCompletes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "top.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[50]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        var updateCount = 0;
        var sawPendingChildOnFirstUpdate = false;

        vm.StructureUpdated += () =>
        {
            updateCount++;
            if (updateCount == 1)
            {
                var sub = vm.CurrentNode!.Children.Single(c => c.IsDirectory);
                sawPendingChildOnFirstUpdate = sub.IsPending;
            }
        };

        await vm.ScanAsync();

        Assert.True(updateCount >= 2);
        Assert.True(sawPendingChildOnFirstUpdate);
        Assert.Equal(150, vm.CurrentNode!.SizeBytes);
        Assert.False(vm.CurrentNode!.Children.Single(c => c.IsDirectory).IsPending);
    }
}
