using FileExplorer.Models;
using FileExplorer.Views;

namespace FileExplorer.Tests;

public sealed class TreemapControlTests
{
    [Fact]
    public void CapNodes_KeepsLargestAndAggregatesRest()
    {
        var children = Enumerable.Range(1, 500)
            .Select(i => new DiskUsageNode { Name = $"f{i}", SizeBytes = i })
            .ToList();

        var (visible, hiddenCount, hiddenBytes) = TreemapControl.CapNodes(children, maxTiles: 400);

        Assert.Equal(400, visible.Count);
        Assert.Equal(100, hiddenCount);
        Assert.Equal(Enumerable.Range(1, 100).Sum(i => (long)i), hiddenBytes); // i 100 più piccoli
        Assert.Equal(500, visible[0].SizeBytes);                               // ordinati per dimensione desc
    }

    [Fact]
    public void CapNodes_BelowLimit_ReturnsAllWithoutAggregation()
    {
        var children = Enumerable.Range(1, 10)
            .Select(i => new DiskUsageNode { Name = $"f{i}", SizeBytes = i })
            .ToList();

        var (visible, hiddenCount, hiddenBytes) = TreemapControl.CapNodes(children, maxTiles: 400);

        Assert.Equal(10, visible.Count);
        Assert.Equal(0, hiddenCount);
        Assert.Equal(0, hiddenBytes);
    }
}
