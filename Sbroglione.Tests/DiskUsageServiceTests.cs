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
}
