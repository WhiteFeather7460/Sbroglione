using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Tests;

public sealed class DiskUsageNodeTests
{
    [Fact]
    public void PropagateSizeIncrease_AddsToSelfAndAllAncestors()
    {
        var root = new DiskUsageNode { Name = "root", IsDirectory = true };
        var mid = new DiskUsageNode { Name = "mid", IsDirectory = true, Parent = root };
        var leaf = new DiskUsageNode { Name = "leaf", IsDirectory = true, Parent = mid };

        leaf.PropagateSizeIncrease(100);

        Assert.Equal(100, leaf.SizeBytes);
        Assert.Equal(100, mid.SizeBytes);
        Assert.Equal(100, root.SizeBytes);
    }

    [Fact]
    public async Task PropagateSizeIncrease_ConcurrentCallsOnSiblings_SumCorrectlyOnSharedAncestor()
    {
        var root = new DiskUsageNode { Name = "root", IsDirectory = true };
        var siblings = Enumerable.Range(0, 50)
            .Select(_ => new DiskUsageNode { Name = "child", IsDirectory = true, Parent = root })
            .ToList();

        await Task.WhenAll(siblings.Select(s => Task.Run(() => s.PropagateSizeIncrease(10))));

        Assert.Equal(500, root.SizeBytes);
        Assert.All(siblings, s => Assert.Equal(10, s.SizeBytes));
    }
}
