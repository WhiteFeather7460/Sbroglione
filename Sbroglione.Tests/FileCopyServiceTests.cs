// Sbroglione.Tests/FileCopyServiceTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

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

        var result = await FileCopyService.CopyFileToManyAsync(source, destinations, null, CancellationToken.None);

        foreach (var destination in destinations)
            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.Equal(destinations.Count, result.SucceededDestinations.Count);
        Assert.Empty(result.FailedDestinations);
    }

    [Fact]
    public async Task CopyFileToManyAsync_CountsBytesPerDestination()
    {
        string source = Path.Combine(_root, "many-src2.bin");
        await File.WriteAllBytesAsync(source, new byte[20]);
        var destinations = new List<string>
        {
            Path.Combine(_root, "m1.bin"),
            Path.Combine(_root, "m2.bin")
        };

        var totalByDestination = new Dictionary<string, long>();
        await FileCopyService.CopyFileToManyAsync(
            source, destinations,
            (destination, delta) =>
            {
                lock (totalByDestination)
                    totalByDestination[destination] = totalByDestination.GetValueOrDefault(destination) + delta;
            },
            CancellationToken.None, bufferSize: 8);

        Assert.Equal(20, totalByDestination[destinations[0]]);
        Assert.Equal(20, totalByDestination[destinations[1]]);
    }

    [Fact]
    public async Task CopyFileToManyAsync_OneDestinationFails_OthersStillComplete()
    {
        string source = Path.Combine(_root, "partial-fail-src.bin");
        byte[] content = Enumerable.Range(0, 50).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, content);

        string goodDestination = Path.Combine(_root, "good.bin");
        // Directory inesistente come "destinazione": FileStream fallisce all'apertura → simula
        // un errore di scrittura (disco pieno, permessi) senza dipendere da mock del filesystem.
        string badDestination = Path.Combine(_root, "missing-dir", "bad.bin");

        var result = await FileCopyService.CopyFileToManyAsync(
            source, new[] { goodDestination, badDestination }, null, CancellationToken.None, bufferSize: 8);

        Assert.Equal(content, await File.ReadAllBytesAsync(goodDestination));
        Assert.Contains(goodDestination, result.SucceededDestinations);
        Assert.True(result.FailedDestinations.ContainsKey(badDestination));
    }

    [Fact]
    public async Task CopyFileToManyAsync_AllDestinationsFail_ThrowsFirstException()
    {
        string source = Path.Combine(_root, "all-fail-src.bin");
        await File.WriteAllBytesAsync(source, new byte[10]);

        var destinations = new[]
        {
            Path.Combine(_root, "missing1", "a.bin"),
            Path.Combine(_root, "missing2", "b.bin")
        };

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            FileCopyService.CopyFileToManyAsync(source, destinations, null, CancellationToken.None));
    }

    [Fact]
    public async Task CopyFileToManyAsync_SourceMissing_PropagatesAndDoesNotLeakWriterTasks()
    {
        // La sorgente non esiste: l'apertura del FileStream fallisce prima di leggere qualunque
        // blocco. I writer task sono già partiti e bloccati in ReadAllAsync in attesa di dati o
        // del completamento del canale: senza il fix (channel.Writer.TryComplete() in finally)
        // resterebbero bloccati per sempre con i FileStream di destinazione aperti e locked.
        string source = Path.Combine(_root, "does-not-exist.bin");
        var destinations = new List<string>
        {
            Path.Combine(_root, "leak-d1.bin"),
            Path.Combine(_root, "leak-d2.bin")
        };

        var callTask = FileCopyService.CopyFileToManyAsync(source, destinations, null, CancellationToken.None);
        var completed = await Task.WhenAny(callTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(callTask, completed); // non deve appendersi: i writer task devono uscire in tempo utile

        await Assert.ThrowsAsync<FileNotFoundException>(() => callTask);

        // Se i writer task avessero il proprio FileStream (FileShare.None) ancora aperto,
        // la Delete/apertura esclusiva qui sotto fallirebbe con IOException.
        foreach (var destination in destinations)
        {
            Assert.True(File.Exists(destination));
            File.Delete(destination);
        }
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

        var progressByDestination = new Dictionary<string, List<CopyProgress>>();
        var result = await FileCopyService.CopyDirectoryToManyAsync(
            sourceRoot, destinationRoots, 2,
            (destination, progress) =>
            {
                lock (progressByDestination)
                {
                    if (!progressByDestination.TryGetValue(destination, out var list))
                        progressByDestination[destination] = list = new List<CopyProgress>();
                    list.Add(progress);
                }
            },
            CancellationToken.None);

        foreach (var destinationRoot in destinationRoots)
        {
            Assert.Equal("alfa", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "a.txt")));
            Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(destinationRoot, "sub", "b.txt")));
            Assert.Equal(2, progressByDestination[destinationRoot][0].TotalFiles);
            Assert.Equal(8, progressByDestination[destinationRoot].Max(p => p.CopiedBytes));
            Assert.True(result.DestinationSucceeded[destinationRoot]);
        }
    }

    [Fact]
    public async Task CopyDirectoryToManyAsync_SkipUnchanged_EvaluatedPerDestination()
    {
        string sourceRoot = Path.Combine(_root, "skip-many-src");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "same.txt"), "12345");

        string upToDateDestination = Path.Combine(_root, "skip-many-uptodate");
        Directory.CreateDirectory(upToDateDestination);
        await File.WriteAllTextAsync(Path.Combine(upToDateDestination, "same.txt"), "MARKR");
        File.SetLastWriteTimeUtc(
            Path.Combine(upToDateDestination, "same.txt"),
            File.GetLastWriteTimeUtc(Path.Combine(sourceRoot, "same.txt")));

        string staleDestination = Path.Combine(_root, "skip-many-stale");

        var completedByDestination = new Dictionary<string, List<string>>();
        await FileCopyService.CopyDirectoryToManyAsync(
            sourceRoot, new[] { upToDateDestination, staleDestination }, 1,
            null, CancellationToken.None, skipUnchanged: true,
            onFileCompleted: (destination, file) =>
            {
                lock (completedByDestination)
                {
                    if (!completedByDestination.TryGetValue(destination, out var list))
                        completedByDestination[destination] = list = new List<string>();
                    list.Add(file);
                }
            });

        Assert.Equal("MARKR", await File.ReadAllTextAsync(Path.Combine(upToDateDestination, "same.txt"))); // saltato: intatto
        Assert.Equal("12345", await File.ReadAllTextAsync(Path.Combine(staleDestination, "same.txt")));     // copiato
    }

    [Fact]
    public async Task CopyDirectoryToManyAsync_OneDestinationFails_OthersComplete()
    {
        string sourceRoot = Path.Combine(_root, "dir-fail-src");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.txt"), "aaa");

        string goodDestination = Path.Combine(_root, "dir-fail-good");
        // Un file (non una cartella) nel punto in cui dovrebbe crearsi la destinazione:
        // Directory.CreateDirectory fallisce con IOException, simulando un errore per-destinazione.
        string badDestinationParent = Path.Combine(_root, "dir-fail-bad-parent");
        await File.WriteAllTextAsync(badDestinationParent, "sono un file, non una cartella");
        string badDestination = Path.Combine(badDestinationParent, "sub");

        var failures = new List<(string destination, string file)>();
        var result = await FileCopyService.CopyDirectoryToManyAsync(
            sourceRoot, new[] { goodDestination, badDestination }, 1,
            null, CancellationToken.None,
            onFileFailed: (destination, file, _) =>
            {
                lock (failures) failures.Add((destination, file));
            });

        Assert.Equal("aaa", await File.ReadAllTextAsync(Path.Combine(goodDestination, "a.txt")));
        Assert.True(result.DestinationSucceeded[goodDestination]);
        Assert.False(result.DestinationSucceeded[badDestination]);
        Assert.Single(failures);
        Assert.Equal(badDestination, failures[0].destination);
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

    [Fact]
    public async Task CopyDirectoryAsync_WithSymlinkLoop_DoesNotFollowSymlinks()
    {
        // I symlink non sono affidabili su Windows senza privilegi: test solo Unix.
        if (OperatingSystem.IsWindows())
            return;

        string source = Path.Combine(_root, "loop-src");
        string destination = Path.Combine(_root, "loop-dst");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "reale.bin"), new byte[8]);
        // Symlink che punta alla cartella stessa: senza skip, l'enumerazione ricorsiva esplode.
        Directory.CreateSymbolicLink(Path.Combine(source, "loop"), source);

        await FileCopyService.CopyDirectoryAsync(source, destination, 1, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destination, "reale.bin")));
        Assert.False(Directory.Exists(Path.Combine(destination, "loop")));
    }
}
