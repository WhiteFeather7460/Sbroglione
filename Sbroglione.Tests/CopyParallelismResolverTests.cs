// Sbroglione.Tests/CopyParallelismResolverTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class CopyParallelismResolverTests
{
    [Theory]
    [InlineData(DiskType.Hdd, DiskType.Ssd)]
    [InlineData(DiskType.Ssd, DiskType.Hdd)]
    [InlineData(DiskType.Hdd, DiskType.Hdd)]
    public void Resolve_Auto_EitherDiskIsHdd_ReturnsOne(DiskType source, DiskType destination)
    {
        var settings = new AppSettings { AutoParallelism = true };
        Assert.Equal(1, CopyParallelismResolver.Resolve(settings, source, destination));
    }

    [Theory]
    [InlineData(DiskType.Ssd, DiskType.Ssd)]
    [InlineData(DiskType.Ssd, DiskType.Unknown)]
    [InlineData(DiskType.Unknown, DiskType.Unknown)]
    public void Resolve_Auto_NeitherDiskIsHdd_ReturnsProcessorBasedValue(DiskType source, DiskType destination)
    {
        var settings = new AppSettings { AutoParallelism = true };
        int expected = Math.Max(2, Environment.ProcessorCount - 1);
        Assert.Equal(expected, CopyParallelismResolver.Resolve(settings, source, destination));
    }

    [Fact]
    public void Resolve_Manual_ReturnsConfiguredValue()
    {
        var settings = new AppSettings { AutoParallelism = false, ManualParallelism = 6 };
        Assert.Equal(6, CopyParallelismResolver.Resolve(settings, DiskType.Hdd, DiskType.Hdd));
    }

    [Fact]
    public void Resolve_Manual_ClampsBelowOneToOne()
    {
        var settings = new AppSettings { AutoParallelism = false, ManualParallelism = 0 };
        Assert.Equal(1, CopyParallelismResolver.Resolve(settings, DiskType.Ssd, DiskType.Ssd));
    }
}
