// FileExplorer.Tests/FileCopyServiceTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class FileCopyServiceTests : IDisposable
{
    private static readonly string[] ManyDestinationNames = { "d1.bin", "d2.bin", "d3.bin" };
    private readonly string _root;

    // Il throttle legge AppSettingsStore.Current: salvato/ripristinato per non contaminare gli altri test.
    private readonly AppSettings _originalCurrent;

    public FileCopyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
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

        var destinations = ManyDestinationNames
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
            sourceRoot, destinationRoots, 2,
            progress => { lock (progressEvents) progressEvents.Add(progress); },
            CancellationToken.None);

        foreach (var destinationRoot in destinationRoots)
        {
            Assert.Equal("alfa", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "a.txt")));
            Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "sub", "b.txt")));
        }

        Assert.Equal(2, progressEvents[0].TotalFiles);
        Assert.Equal(8, progressEvents.Max(p => p.CopiedBytes)); // "alfa" + "beta" contati una sola volta
    }

    [Fact]
    public async Task CopyFileAsync_PreservesSourceLastWriteTime()
    {
        string source = Path.Combine(_root, "mtime-src.bin");
        string destination = Path.Combine(_root, "mtime-dst.bin");
        await File.WriteAllBytesAsync(source, new byte[10]);
        var sourceTime = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, sourceTime);

        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None);

        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public async Task CopyFileAsync_WithThrottleEnabled_TakesAtLeastExpectedTime()
    {
        // 2 MB a 1 MB/s: il burst iniziale copre ~1 MB, il resto attende ~1 s.
        string source = Path.Combine(_root, "big.bin");
        string destination = Path.Combine(_root, "big-copy.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);

        AppSettingsStore.Current = new AppSettings { ThrottleEnabled = true, ThrottleMBps = 1 };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None);
        stopwatch.Stop();

        // Nota anti-flakiness: soglia 0.5s su un'attesa teorica di ~1s — verifica che il throttle
        // rallenti la copia, non il tempo esatto.
        Assert.True(stopwatch.Elapsed.TotalSeconds >= 0.5,
            $"Copia troppo veloce con throttle attivo: {stopwatch.Elapsed.TotalSeconds:F2}s");
        Assert.Equal(2 * 1024 * 1024, new FileInfo(destination).Length);
    }

    [Fact]
    public async Task CopyDirectoryAsync_SkipUnchanged_LeavesMatchingDestinationFilesUntouched()
    {
        string sourceRoot = Path.Combine(_root, "skip-src");
        string destinationRoot = Path.Combine(_root, "skip-dst");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "same.txt"), "12345");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "grown.txt"), "abc");

        // Prima copia completa.
        await FileCopyService.CopyDirectoryAsync(sourceRoot, destinationRoot, 1, null, CancellationToken.None);

        // Marcatore in destinazione: stessa lunghezza e stesso mtime → deve sopravvivere al re-run.
        await File.WriteAllTextAsync(Path.Combine(destinationRoot, "same.txt"), "MARKR");
        File.SetLastWriteTimeUtc(
            Path.Combine(destinationRoot, "same.txt"),
            File.GetLastWriteTimeUtc(Path.Combine(sourceRoot, "same.txt")));

        // La sorgente di grown.txt cambia dimensione → deve essere ricopiato.
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "grown.txt"), "abcdef");

        var progressEvents = new List<CopyProgress>();
        await FileCopyService.CopyDirectoryAsync(
            sourceRoot, destinationRoot, 1, progressEvents.Add, CancellationToken.None,
            skipUnchanged: true);

        Assert.Equal("MARKR", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "same.txt")));
        Assert.Equal("abcdef", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "grown.txt")));
        Assert.Equal(progressEvents[^1].TotalBytes, progressEvents[^1].CopiedBytes); // i saltati contano
    }
}
