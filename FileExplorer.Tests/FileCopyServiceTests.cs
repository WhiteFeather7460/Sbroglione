// FileExplorer.Tests/FileCopyServiceTests.cs
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class FileCopyServiceTests : IDisposable
{
    private readonly string _root;

    public FileCopyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CopyFileAsync_CustomBufferSize_InvokesCallbackPerBlockAndCopiesCorrectly()
    {
        string source = Path.Combine(_root, "source.bin");
        string destination = Path.Combine(_root, "dest.bin");
        byte[] content = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, content);

        var callbackSizes = new List<long>();

        await FileCopyService.CopyFileAsync(
            source,
            destination,
            bytesRead => callbackSizes.Add(bytesRead),
            CancellationToken.None,
            bufferSize: 5);

        Assert.Equal(new long[] { 5, 5, 5, 5 }, callbackSizes);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileAsync_DefaultBufferSize_CopiesContentCorrectly()
    {
        string source = Path.Combine(_root, "source2.bin");
        string destination = Path.Combine(_root, "dest2.bin");
        byte[] content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, content);

        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileAsync_ZeroBufferSize_FallsBackToDefaultAndCopiesCorrectly()
    {
        string source = Path.Combine(_root, "source3.bin");
        string destination = Path.Combine(_root, "dest3.bin");
        byte[] content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, content);

        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None, bufferSize: 0);

        var destinationInfo = new FileInfo(destination);
        Assert.Equal(content.Length, destinationInfo.Length);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileAsync_NegativeBufferSize_FallsBackToDefaultAndCopiesCorrectly()
    {
        string source = Path.Combine(_root, "source4.bin");
        string destination = Path.Combine(_root, "dest4.bin");
        byte[] content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, content);

        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None, bufferSize: -1);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileToManyAsync_ThreeDestinations_AllReceiveIdenticalContent()
    {
        string source = Path.Combine(_root, "many-src.bin");
        byte[] content = Enumerable.Range(0, 300).Select(i => (byte)(i % 256)).ToArray();
        await File.WriteAllBytesAsync(source, content);

        var destinations = new[] { "d1.bin", "d2.bin", "d3.bin" }
            .Select(name => Path.Combine(_root, name)).ToList();

        await FileCopyService.CopyFileToManyAsync(source, destinations, null, CancellationToken.None);

        foreach (var destination in destinations)
            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileToManyAsync_CountsSourceBytesOnce()
    {
        string source = Path.Combine(_root, "many-src2.bin");
        await File.WriteAllBytesAsync(source, new byte[20]);
        var destinations = new List<string>
        {
            Path.Combine(_root, "m1.bin"),
            Path.Combine(_root, "m2.bin")
        };

        long totalReported = 0;
        await FileCopyService.CopyFileToManyAsync(
            source, destinations, delta => totalReported += delta, CancellationToken.None, bufferSize: 8);

        Assert.Equal(20, totalReported);
    }

    [Fact]
    public async Task CopyDirectoryToManyAsync_ReplicatesTreeInEveryDestination()
    {
        string sourceRoot = Path.Combine(_root, "many-dir-src");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.txt"), "alfa");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "sub", "b.txt"), "beta");

        var destinationRoots = new List<string>
        {
            Path.Combine(_root, "many-dir-d1"),
            Path.Combine(_root, "many-dir-d2")
        };

        var progressEvents = new List<CopyProgress>();
        await FileCopyService.CopyDirectoryToManyAsync(
            sourceRoot, destinationRoots, 2, progressEvents.Add, CancellationToken.None);

        foreach (var destinationRoot in destinationRoots)
        {
            Assert.Equal("alfa", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "a.txt")));
            Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "sub", "b.txt")));
        }

        Assert.Equal(2, progressEvents[0].TotalFiles);
        Assert.Equal(8, progressEvents[^1].CopiedBytes); // "alfa" + "beta" contati una sola volta
    }
}
