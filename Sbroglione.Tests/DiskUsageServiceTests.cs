using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DiskUsageServiceTests : IDisposable
{
    private readonly string _root;

    public DiskUsageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task BuildTreeAsync_SumsSizesRecursively()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "top.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[50]);

        var tree = await DiskUsageService.BuildTreeAsync(_root, null, CancellationToken.None);

        Assert.True(tree.IsDirectory);
        Assert.Equal(150, tree.SizeBytes);
        Assert.Equal(2, tree.Children.Count);

        var sub = tree.Children.Single(c => c.IsDirectory);
        Assert.Equal("sub", sub.Name);
        Assert.Equal(50, sub.SizeBytes);
        Assert.Equal("inner.bin", Assert.Single(sub.Children).Name);
    }

    [Fact]
    public async Task BuildTreeAsync_EmptyDirectory_ZeroSizeNoChildren()
    {
        var tree = await DiskUsageService.BuildTreeAsync(_root, null, CancellationToken.None);

        Assert.Equal(0, tree.SizeBytes);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_MatchesBuildTreeAsync_FinalSizesAndShape()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "top.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[50]);

        var tree = await DiskUsageService.BuildTreeLayeredAsync(_root, null, CancellationToken.None);

        Assert.True(tree.IsDirectory);
        Assert.False(tree.IsPending);
        Assert.Equal(150, tree.SizeBytes);
        Assert.Equal(2, tree.Children.Count);

        var sub = tree.Children.Single(c => c.IsDirectory);
        Assert.False(sub.IsPending);
        Assert.Equal("sub", sub.Name);
        Assert.Equal(50, sub.SizeBytes);
        Assert.Equal("inner.bin", Assert.Single(sub.Children).Name);
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_FirstLayerCallback_SeesRootChildrenButDeeperOnesStillPending()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "top.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[50]);

        var seenFirstCallbackChildrenCount = -1;
        var seenFirstCallbackSubPending = false;
        var callbackCount = 0;

        await DiskUsageService.BuildTreeLayeredAsync(_root, root =>
        {
            callbackCount++;
            if (callbackCount == 1)
            {
                seenFirstCallbackChildrenCount = root.Children.Count;
                seenFirstCallbackSubPending = root.Children.Single(c => c.IsDirectory).IsPending;
            }
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(2, seenFirstCallbackChildrenCount);
        Assert.True(seenFirstCallbackSubPending);
        Assert.True(callbackCount >= 2);
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_Cancellation_ThrowsOperationCanceledException()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DiskUsageService.BuildTreeLayeredAsync(_root, null, cts.Token));
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_EmptyDirectory_ZeroSizeNoChildrenNotPending()
    {
        var tree = await DiskUsageService.BuildTreeLayeredAsync(_root, null, CancellationToken.None);

        Assert.Equal(0, tree.SizeBytes);
        Assert.Empty(tree.Children);
        Assert.False(tree.IsPending);
    }
}
