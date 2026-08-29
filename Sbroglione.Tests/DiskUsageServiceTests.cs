using Sbroglione.Models;
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
    public async Task BuildTreeLayeredAsync_TwoLevelTree_SumsSizesAndClearsIsPending()
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

    [Fact]
    public async Task BuildTreeLayeredAsync_MatchesBuildTreeAsync_ForMultiBranchTree()
    {
        for (var i = 0; i < 5; i++)
        {
            var branch = Path.Combine(_root, $"branch{i}");
            Directory.CreateDirectory(branch);
            for (var j = 0; j < 4; j++)
            {
                var leaf = Path.Combine(branch, $"leaf{j}");
                Directory.CreateDirectory(leaf);
                await File.WriteAllBytesAsync(Path.Combine(leaf, "f.bin"), new byte[10 * (i + 1) + j]);
            }
        }

        var expected = await DiskUsageService.BuildTreeAsync(_root, null, CancellationToken.None);
        var actual = await DiskUsageService.BuildTreeLayeredAsync(_root, null, CancellationToken.None);

        Assert.Equal(expected.SizeBytes, actual.SizeBytes);
        AssertSameShape(expected, actual);
    }

    private static void AssertSameShape(DiskUsageNode expected, DiskUsageNode actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.IsDirectory, actual.IsDirectory);
        Assert.Equal(expected.SizeBytes, actual.SizeBytes);
        Assert.Equal(expected.Children.Count, actual.Children.Count);

        var expectedOrdered = expected.Children.OrderBy(c => c.Name).ToList();
        var actualOrdered = actual.Children.OrderBy(c => c.Name).ToList();
        for (var i = 0; i < expectedOrdered.Count; i++)
            AssertSameShape(expectedOrdered[i], actualOrdered[i]);
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_UnauthorizedDirectory_StillClearsIsPending()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return; // il trucco dei permessi qui sotto è POSIX-only

        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            var tree = await DiskUsageService.BuildTreeLayeredAsync(_root, null, CancellationToken.None);
            var lockedNode = tree.Children.Single(c => c.Name == "locked");
            Assert.False(lockedNode.IsPending);
            Assert.Empty(lockedNode.Children);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_CancelledDuringSecondLayer_ThrowsOperationCanceledException()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[10]);

        using var cts = new CancellationTokenSource();
        var layer = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DiskUsageService.BuildTreeLayeredAsync(_root, _ =>
            {
                layer++;
                if (layer == 1)
                    cts.Cancel();
                return Task.CompletedTask;
            }, cts.Token));
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_AwaitsCallbackBeforeStartingNextLayer()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[10]);

        var childCountDuringDelay = -1;

        var root = await DiskUsageService.BuildTreeLayeredAsync(_root, async node =>
        {
            if (childCountDuringDelay == -1)
            {
                var sub = node.Children.Single(c => c.IsDirectory);
                await Task.Delay(50);
                childCountDuringDelay = sub.Children.Count;
            }
        }, CancellationToken.None);

        Assert.Equal(0, childCountDuringDelay);
        Assert.NotEmpty(root.Children.Single(c => c.IsDirectory).Children);
    }

    [Fact]
    public async Task BuildTreeLayeredAsync_EmptyDirectory_CallbackStillCalledOnce()
    {
        var callCount = 0;
        await DiskUsageService.BuildTreeLayeredAsync(_root, _ => { callCount++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, callCount);
    }
}
