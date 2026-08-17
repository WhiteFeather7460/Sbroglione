using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class ChecksumServiceTests : IDisposable
{
    private readonly string _root;

    public ChecksumServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-checksum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ComputeSha256Async_MaxBytes_HashesOnlyPrefix()
    {
        string samePrefixA = Path.Combine(_root, "a.bin");
        string samePrefixB = Path.Combine(_root, "b.bin");
        byte[] prefix = Enumerable.Repeat((byte)7, 100).ToArray();
        await File.WriteAllBytesAsync(samePrefixA, prefix.Concat(new byte[] { 1 }).ToArray());
        await File.WriteAllBytesAsync(samePrefixB, prefix.Concat(new byte[] { 2 }).ToArray());

        string hashA = await ChecksumService.ComputeSha256Async(samePrefixA, maxBytes: 100);
        string hashB = await ChecksumService.ComputeSha256Async(samePrefixB, maxBytes: 100);
        string fullA = await ChecksumService.ComputeSha256Async(samePrefixA);
        string fullB = await ChecksumService.ComputeSha256Async(samePrefixB);

        Assert.Equal(hashA, hashB);      // prefissi identici
        Assert.NotEqual(fullA, fullB);   // file interi diversi
    }

    [Fact]
    public async Task ComputeSha256Async_MaxBytesLargerThanFile_MatchesFullHash()
    {
        string path = Path.Combine(_root, "small.bin");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        Assert.Equal(
            await ChecksumService.ComputeSha256Async(path),
            await ChecksumService.ComputeSha256Async(path, maxBytes: 1024));
    }

    [Fact]
    public void SizeFormatter_FormatsAllMagnitudes()
    {
        Assert.Equal("512 B", SizeFormatter.Format(512));
        Assert.Equal("1 KB", SizeFormatter.Format(1024));
        Assert.EndsWith(" MB", SizeFormatter.Format(5 * 1024 * 1024));
        Assert.EndsWith(" GB", SizeFormatter.Format(3L * 1024 * 1024 * 1024));
    }
}
