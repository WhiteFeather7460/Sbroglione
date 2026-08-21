using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DiskTypeServiceTests
{
    private const string SampleMounts =
        "/dev/sda2 / ext4 rw,relatime 0 0\n" +
        "/dev/sda1 /boot/efi vfat rw,relatime 0 0\n" +
        "/dev/sdb1 /mnt/data ext4 rw,relatime 0 0\n" +
        "tmpfs /tmp tmpfs rw 0 0\n";

    [Fact]
    public void ResolveLinuxBlockDevice_PathUnderSpecificMount_ReturnsThatDevice()
    {
        string? device = DiskTypeService.ResolveLinuxBlockDevice(SampleMounts, "/mnt/data/subfolder/file.txt");
        Assert.Equal("/dev/sdb1", device);
    }

    [Fact]
    public void ResolveLinuxBlockDevice_PathNotUnderAnySpecificMount_FallsBackToRoot()
    {
        string? device = DiskTypeService.ResolveLinuxBlockDevice(SampleMounts, "/home/user/file.txt");
        Assert.Equal("/dev/sda2", device);
    }

    [Fact]
    public void ResolveLinuxBlockDevice_NoDeviceMountsPresent_ReturnsNull()
    {
        string? device = DiskTypeService.ResolveLinuxBlockDevice("server:/export /mnt/nfs nfs rw 0 0\n", "/mnt/nfs/file");
        Assert.Null(device);
    }

    [Theory]
    [InlineData("/dev/sda1", "sda")]
    [InlineData("/dev/sda", "sda")]
    [InlineData("/dev/nvme0n1p1", "nvme0n1")]
    [InlineData("/dev/nvme0n1", "nvme0n1")]
    public void ExtractLinuxDiskName_RecognizedDevices_ReturnsDiskName(string device, string expected)
    {
        Assert.Equal(expected, DiskTypeService.ExtractLinuxDiskName(device));
    }

    [Fact]
    public void ExtractLinuxDiskName_MapperDevice_ReturnsNull()
    {
        Assert.Null(DiskTypeService.ExtractLinuxDiskName("/dev/mapper/vg-root"));
    }

    [Fact]
    public void ResolveLinuxCacheKey_PathsOnDifferentPhysicalDisks_ReturnDifferentKeys()
    {
        // Bug reprodotto: prima della fix, la chiave di cache era sempre Path.GetPathRoot("/xxx") == "/",
        // quindi "/" e "/mnt/data" (dischi fisici diversi: sda e sdb) collassavano sulla stessa voce.
        string rootKey = DiskTypeService.ResolveLinuxCacheKey(SampleMounts, "/home/user/file.txt");
        string dataKey = DiskTypeService.ResolveLinuxCacheKey(SampleMounts, "/mnt/data/subfolder/file.txt");

        Assert.Equal("sda", rootKey);
        Assert.Equal("sdb", dataKey);
        Assert.NotEqual(rootKey, dataKey);
    }

    [Fact]
    public void ResolveLinuxCacheKey_UnrecognizedDeviceName_FallsBackToRawDevice()
    {
        const string mounts = "/dev/mapper/vg-root / ext4 rw,relatime 0 0\n";
        string key = DiskTypeService.ResolveLinuxCacheKey(mounts, "/home/user/file.txt");
        Assert.Equal("/dev/mapper/vg-root", key);
    }

    [Fact]
    public void ResolveLinuxCacheKey_NoDeviceMatch_FallsBackToPathRoot()
    {
        const string mounts = "server:/export /mnt/nfs nfs rw 0 0\n";
        string key = DiskTypeService.ResolveLinuxCacheKey(mounts, "/mnt/nfs/file.txt");
        Assert.Equal("/", key);
    }

    [Theory]
    [InlineData("0", DiskType.Ssd)]
    [InlineData("0\n", DiskType.Ssd)]
    [InlineData("1", DiskType.Hdd)]
    [InlineData("1\n", DiskType.Hdd)]
    [InlineData("", DiskType.Unknown)]
    [InlineData("garbage", DiskType.Unknown)]
    public void ParseRotationalFlag_ReturnsExpectedType(string content, DiskType expected)
    {
        Assert.Equal(expected, DiskTypeService.ParseRotationalFlag(content));
    }

    [Theory]
    [InlineData(3, DiskType.Hdd)]
    [InlineData(4, DiskType.Ssd)]
    [InlineData(0, DiskType.Unknown)]
    [InlineData(99, DiskType.Unknown)]
    public void ParseWindowsMediaType_ReturnsExpectedType(int mediaType, DiskType expected)
    {
        Assert.Equal(expected, DiskTypeService.ParseWindowsMediaType(mediaType));
    }

    [Theory]
    [InlineData("   Solid State:            Yes\n", DiskType.Ssd)]
    [InlineData("   Solid State:            No\n", DiskType.Hdd)]
    [InlineData("nessun campo qui", DiskType.Unknown)]
    public void ParseDiskutilSolidState_ReturnsExpectedType(string output, DiskType expected)
    {
        Assert.Equal(expected, DiskTypeService.ParseDiskutilSolidState(output));
    }

    [Fact]
    public async Task GetDiskTypeAsync_NullOrWhitespacePath_ReturnsUnknownWithoutThrowing()
    {
        Assert.Equal(DiskType.Unknown, await DiskTypeService.GetDiskTypeAsync(null, CancellationToken.None));
        Assert.Equal(DiskType.Unknown, await DiskTypeService.GetDiskTypeAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task GetDiskTypeAsync_ValidLocalPath_DoesNotThrow()
    {
        var result = await DiskTypeService.GetDiskTypeAsync(Path.GetTempPath(), CancellationToken.None);
        Assert.True(Enum.IsDefined(typeof(DiskType), result));
    }

    [Fact]
    public void TryGetFreshCached_FreshEntry_ReturnsTypeWithoutEvicting()
    {
        string key = "fresh-" + Guid.NewGuid();
        DiskTypeService.SeedCacheForTest(key, DiskType.Ssd, DateTime.UtcNow);

        bool hit = DiskTypeService.TryGetFreshCached(key, out DiskType type);

        Assert.True(hit);
        Assert.Equal(DiskType.Ssd, type);
        Assert.True(DiskTypeService.CacheContainsKeyForTest(key));
    }

    [Fact]
    public void TryGetFreshCached_ExpiredEntry_EvictsFromCacheAndReturnsFalse()
    {
        string key = "expired-" + Guid.NewGuid();
        // Oltre il TTL (5 minuti): entry già scaduta al momento del lookup.
        DiskTypeService.SeedCacheForTest(key, DiskType.Hdd, DateTime.UtcNow - TimeSpan.FromMinutes(10));

        bool hit = DiskTypeService.TryGetFreshCached(key, out _);

        Assert.False(hit);
        Assert.False(DiskTypeService.CacheContainsKeyForTest(key)); // rimossa dal dizionario, non solo ignorata
    }

    [Fact]
    public void TryGetFreshCached_MissingEntry_ReturnsFalse()
    {
        string key = "missing-" + Guid.NewGuid();

        bool hit = DiskTypeService.TryGetFreshCached(key, out _);

        Assert.False(hit);
    }
}
