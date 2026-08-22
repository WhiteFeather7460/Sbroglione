# Copia multi-destinazione con avanzamento indipendente per destinazione — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Disaccoppiare la copia verso più destinazioni: una destinazione lenta o fallita non deve più bloccare/abortire le altre. Ogni destinazione avanza al proprio ritmo, con progresso/velocità/stato/errore propri, visibili nel widget "in copia adesso".

**Architecture:** `CopyFileToManyAsync` passa da scrittura lockstep (`Task.WhenAll` per chunk) a un reader che legge la sorgente una sola volta e distribuisce ogni chunk su `Channel<byte[]>` bounded, uno per destinazione; un task writer per destinazione consuma il proprio canale al proprio ritmo. Il fallimento di una destinazione non propaga alle altre (a meno che falliscano tutte). Lo stesso schema si propaga a `CopyDirectoryToManyAsync` (per-file → per-destinazione) e al livello ViewModel (`DestinationProgressViewModel` per destinazione, aggregato sul pair).

**Tech Stack:** .NET 8, `System.Threading.Channels`, ReactiveUI, Avalonia, xunit.

**Spec:** `docs/superpowers/specs/2026-08-22-multi-dest-independent-progress-design.md`

## Global Constraints

- Capacità del channel per destinazione: costante fissa `8`, non esposta in UI/impostazioni.
- Fallimento di una destinazione: le altre proseguono; se **tutte** falliscono, l'eccezione si ripropaga.
- Parallelismo multi-file (`maxDegreeOfParallelism`/semaphore) resta invariato.
- Nessun colore hardcoded in AXAML: riusare `{DynamicResource Brush.*}` e le classi `Border.badge.*` esistenti.
- Nessuna retry automatica, nessuna configurabilità della capacità del buffer, nessun log/export a fine batch oltre allo stato visibile (fuori scope).

---

### Task 1: `FileCopyService.CopyFileToManyAsync` — writer disaccoppiati via channel

**Files:**
- Modify: `Sbroglione/Services/FileCopyService.cs:76-126` (metodo `CopyFileToManyAsync`)
- Test: `Sbroglione.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Produces: `Task<CopyToManyResult> CopyFileToManyAsync(string sourcePath, IReadOnlyList<string> destinationPaths, Action<string, long>? onBytesCopied, CancellationToken ct, int bufferSize = DefaultBufferSize)` e `readonly record struct CopyToManyResult(IReadOnlyList<string> SucceededDestinations, IReadOnlyDictionary<string, Exception> FailedDestinations)`. `onBytesCopied` ora riceve `(destinationPath, deltaBytes)` invece di solo `deltaBytes`.

- [ ] **Step 1: Aggiorna i test esistenti alla nuova firma**

I due test che usano `CopyFileToManyAsync` in `Sbroglione.Tests/FileCopyServiceTests.cs` (righe 90-122) vanno adattati: `onBytesCopied` ora prende `(string, long)`.

```csharp
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
```

- [ ] **Step 2: Nuovo test — una destinazione fallisce, le altre completano**

```csharp
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
```

- [ ] **Step 3: Esegui i test — devono fallire (firma/comportamento non ancora implementati)**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests.CopyFileToManyAsync"`
Expected: FAIL a compilazione (firma diversa) o su `result`/eccezione mancante.

- [ ] **Step 4: Implementa `CopyToManyResult` e il nuovo `CopyFileToManyAsync`**

Aggiungi `using System.Collections.Concurrent;` e `using System.Threading.Channels;` in cima a `Sbroglione/Services/FileCopyService.cs`. Sostituisci il metodo (righe 76-126):

```csharp
private const int DestinationChannelCapacity = 8;

/// <summary>
/// Risultato di una copia verso più destinazioni: quali sono riuscite e, per quelle
/// fallite, l'eccezione che le ha fatte fallire.
/// </summary>
public readonly record struct CopyToManyResult(
    IReadOnlyList<string> SucceededDestinations,
    IReadOnlyDictionary<string, Exception> FailedDestinations);

/// <summary>
/// Copia un file verso più destinazioni con una sola lettura della sorgente: un task
/// legge la sorgente e distribuisce ogni blocco su un <see cref="Channel{T}"/> bounded
/// per destinazione; un task scrittore per destinazione consuma il proprio canale al
/// proprio ritmo, così una destinazione lenta non blocca le altre (solo, tramite il
/// backpressure del canale, rallenta la lettura una volta piena la coda di quella
/// destinazione). Se una destinazione fallisce, le altre proseguono; se falliscono
/// tutte, la prima eccezione viene rilanciata.
/// <paramref name="onBytesCopied"/> riceve (percorso destinazione, byte scritti) per
/// ogni blocco effettivamente scritto su quella destinazione.
/// </summary>
public static async Task<CopyToManyResult> CopyFileToManyAsync(
    string sourcePath,
    IReadOnlyList<string> destinationPaths,
    Action<string, long>? onBytesCopied,
    CancellationToken ct,
    int bufferSize = DefaultBufferSize)
{
    if (bufferSize <= 0)
        bufferSize = DefaultBufferSize;

    if (destinationPaths.Count == 0)
        return new CopyToManyResult(Array.Empty<string>(), new Dictionary<string, Exception>());

    var channels = destinationPaths.ToDictionary(
        d => d,
        _ => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(DestinationChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true
        }));
    var failed = new ConcurrentDictionary<string, Exception>();

    var writerTasks = destinationPaths.Select(destination => Task.Run(async () =>
    {
        ChannelReader<byte[]> reader = channels[destination].Reader;
        try
        {
            var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (output.ConfigureAwait(false))
            {
                await foreach (byte[] chunk in reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await output.WriteAsync(chunk, ct).ConfigureAwait(false);
                    onBytesCopied?.Invoke(destination, chunk.Length);
                }

                await output.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed[destination] = ex;
            // Smaltisce il resto del canale: il reader potrebbe essere bloccato in
            // WriteAsync per backpressure e deve poter continuare con le altre destinazioni.
            try
            {
                await foreach (var _ in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) { }
            }
            catch { /* canale già completato o cancellato: nulla da smaltire */ }
        }
    })).ToList();

    var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    await using (input.ConfigureAwait(false))
    {
        var buffer = new byte[bufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
        {
            await IoThrottleService.WaitAsync(read, ct).ConfigureAwait(false);

            var chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);

            foreach (var destination in destinationPaths)
            {
                if (failed.ContainsKey(destination))
                    continue;
                try
                {
                    await channels[destination].Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    // Il writer di questa destinazione ha già fallito e chiuso il canale.
                }
            }
        }
    }

    foreach (var channel in channels.Values)
        channel.Writer.TryComplete();

    await Task.WhenAll(writerTasks).ConfigureAwait(false);

    var succeeded = destinationPaths.Where(d => !failed.ContainsKey(d)).ToList();
    if (succeeded.Count == 0)
        throw failed.Values.First();

    // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato
    // solo sulle destinazioni riuscite.
    DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
    foreach (var destination in succeeded)
        File.SetLastWriteTimeUtc(destination, sourceTime);

    return new CopyToManyResult(succeeded, failed);
}
```

- [ ] **Step 5: Esegui i test — devono passare**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests.CopyFileToManyAsync"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Sbroglione/Services/FileCopyService.cs Sbroglione.Tests/FileCopyServiceTests.cs
git commit -m "feat: CopyFileToManyAsync usa writer per-destinazione disaccoppiati via channel"
```

---

### Task 2: `FileCopyService.CopyDirectoryToManyAsync` — progresso/skip/errore per destinazione

**Files:**
- Modify: `Sbroglione/Services/FileCopyService.cs:203-278` (metodo `CopyDirectoryToManyAsync`)
- Test: `Sbroglione.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Consumes: `CopyFileToManyAsync` e `CopyToManyResult` da Task 1.
- Produces: `Task<CopyDirectoryToManyResult> CopyDirectoryToManyAsync(string sourceRoot, IReadOnlyList<string> destinationRoots, int maxDegreeOfParallelism, Action<string, CopyProgress>? onProgress, CancellationToken ct, int bufferSize = DefaultBufferSize, bool skipUnchanged = false, Action<string, string>? onFileStarted = null, Action<string, string>? onFileCompleted = null, Action<string, string, Exception>? onFileFailed = null)` e `readonly record struct CopyDirectoryToManyResult(IReadOnlyDictionary<string, bool> DestinationSucceeded)`. Tutti i callback ora hanno `destinationRoot` come primo parametro.

- [ ] **Step 1: Aggiorna il test esistente alla nuova firma `onProgress`**

`CopyDirectoryToManyAsync_ReplicatesTreeInEveryDestination` (righe 124-152 di `FileCopyServiceTests.cs`) usa `onProgress: Action<CopyProgress>`; diventa `Action<string, CopyProgress>`:

```csharp
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
```

- [ ] **Step 2: Nuovo test — skip-unchanged indipendente per destinazione**

```csharp
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
```

- [ ] **Step 3: Nuovo test — una destinazione fallisce durante la copia di una cartella**

```csharp
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
```

- [ ] **Step 4: Esegui i test — devono fallire**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests.CopyDirectoryToManyAsync"`
Expected: FAIL a compilazione.

- [ ] **Step 5: Implementa `CopyDirectoryToManyResult` e il nuovo `CopyDirectoryToManyAsync`**

Aggiungi `using System.Collections.Concurrent;` se non già presente (Task 1 lo aggiunge già). Sostituisci il metodo (righe 203-278):

```csharp
/// <summary>Esito di una copia cartella verso più destinazioni: quali sono riuscite (nessun file fallito).</summary>
public readonly record struct CopyDirectoryToManyResult(IReadOnlyDictionary<string, bool> DestinationSucceeded);

private sealed class ByteCounter
{
    public long Value;
}

/// <summary>
/// Copia ricorsivamente una cartella verso più destinazioni (più file in parallelo),
/// leggendo ogni file sorgente una sola volta per file. Ogni destinazione avanza in modo
/// indipendente: lo skip-unchanged è valutato per singola coppia sorgente/destinazione, e
/// il fallimento di una destinazione (per un singolo file) non ferma le altre.
/// </summary>
public static async Task<CopyDirectoryToManyResult> CopyDirectoryToManyAsync(
    string sourceRoot,
    IReadOnlyList<string> destinationRoots,
    int maxDegreeOfParallelism,
    Action<string, CopyProgress>? onProgress,
    CancellationToken ct,
    int bufferSize = DefaultBufferSize,
    bool skipUnchanged = false,
    Action<string, string>? onFileStarted = null,
    Action<string, string>? onFileCompleted = null,
    Action<string, string, Exception>? onFileFailed = null)
{
    if (bufferSize <= 0)
        bufferSize = DefaultBufferSize;

    (List<string> files, long totalBytes) = await Task.Run(() =>
    {
        var list = new List<string>();
        long total = 0;
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            list.Add(file);
            total += new FileInfo(file).Length;
        }
        return (list, total);
    }, ct).ConfigureAwait(false);

    var counters = destinationRoots.ToDictionary(d => d, _ => new ByteCounter());
    var destinationFailed = new ConcurrentDictionary<string, bool>(destinationRoots.ToDictionary(d => d, _ => false));

    foreach (var destination in destinationRoots)
        onProgress?.Invoke(destination, new CopyProgress(0, totalBytes, files.Count));

    if (files.Count == 0)
        return new CopyDirectoryToManyResult(destinationRoots.ToDictionary(d => d, d => true));

    using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

    var tasks = files.Select(async sourceFile =>
    {
        ct.ThrowIfCancellationRequested();
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFileByRoot = destinationRoots.ToDictionary(root => root, root => Path.Combine(root, relative));

            var toCopy = new List<string>();
            var toSkip = new List<string>();
            foreach (var root in destinationRoots)
            {
                if (skipUnchanged && IsUnchanged(sourceFile, destinationFileByRoot[root]))
                    toSkip.Add(root);
                else
                    toCopy.Add(root);
            }

            long sourceLength = new FileInfo(sourceFile).Length;
            foreach (var root in toSkip)
            {
                long newTotal = Interlocked.Add(ref counters[root].Value, sourceLength);
                onProgress?.Invoke(root, new CopyProgress(newTotal, totalBytes, files.Count));
                onFileCompleted?.Invoke(root, sourceFile);
            }

            if (toCopy.Count == 0)
                return;

            foreach (var root in toCopy)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFileByRoot[root])!);
                onFileStarted?.Invoke(root, sourceFile);
            }

            var copyDestinationFiles = toCopy.Select(root => destinationFileByRoot[root]).ToList();
            var rootByDestinationFile = toCopy.ToDictionary(root => destinationFileByRoot[root], root => root);

            CopyToManyResult copyResult;
            try
            {
                copyResult = await CopyFileToManyAsync(sourceFile, copyDestinationFiles, (destinationFile, deltaBytes) =>
                {
                    string root = rootByDestinationFile[destinationFile];
                    long newTotal = Interlocked.Add(ref counters[root].Value, deltaBytes);
                    onProgress?.Invoke(root, new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tutte le destinazioni di questo file hanno fallito.
                foreach (var root in toCopy)
                {
                    destinationFailed[root] = true;
                    onFileFailed?.Invoke(root, sourceFile, ex);
                }
                return;
            }

            foreach (var destinationFile in copyResult.SucceededDestinations)
                onFileCompleted?.Invoke(rootByDestinationFile[destinationFile], sourceFile);

            foreach (var (destinationFile, error) in copyResult.FailedDestinations)
            {
                string root = rootByDestinationFile[destinationFile];
                destinationFailed[root] = true;
                onFileFailed?.Invoke(root, sourceFile, error);
            }
        }
        finally
        {
            semaphore.Release();
        }
    });

    await Task.WhenAll(tasks).ConfigureAwait(false);

    return new CopyDirectoryToManyResult(
        destinationRoots.ToDictionary(d => d, d => !destinationFailed[d]));
}
```

- [ ] **Step 6: Esegui i test — devono passare**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: PASS (tutta la classe, inclusi i test di Task 1)

- [ ] **Step 7: Commit**

```bash
git add Sbroglione/Services/FileCopyService.cs Sbroglione.Tests/FileCopyServiceTests.cs
git commit -m "feat: CopyDirectoryToManyAsync valuta skip/errore/progresso per destinazione"
```

---

### Task 3: `DestinationProgressViewModel` e `FolderFilePairViewModel.DestinationsProgress`

**Files:**
- Modify: `Sbroglione/ViewModels/FolderFilePairViewModel.cs`
- Test: `Sbroglione.Tests/FolderFilePairViewModelTests.cs` (crea se non esiste — verifica prima con `find Sbroglione.Tests -iname "FolderFilePairViewModelTests.cs"`; se assente aggiungi i nuovi `[Fact]` nel file di test più vicino all'esistente organizzazione, es. nuovo file dedicato)

**Interfaces:**
- Produces: `DestinationProgressViewModel(string path)` con proprietà `Path` (string, get-only), `Progress` (double), `Status` (string?), `SpeedText` (string?), `CurrentBytesPerSecond` (double), `StateKind` (CopyStateKind, default `Copying`), `ErrorMessage` (string?), `CopyingFiles` (ObservableCollection<FileSystemItem>). `FolderFilePairViewModel.DestinationsProgress` : `ObservableCollection<DestinationProgressViewModel>`. Rimuove `FolderFilePairViewModel.CopyingFiles`.

- [ ] **Step 1: Scrivi il test (nuovo file `Sbroglione.Tests/FolderFilePairViewModelTests.cs`)**

```csharp
// Sbroglione.Tests/FolderFilePairViewModelTests.cs
using Sbroglione.Models;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class FolderFilePairViewModelTests
{
    [Fact]
    public void DestinationsProgress_StartsEmpty_AndAcceptsAddedEntries()
    {
        var pair = new FolderFilePairViewModel();
        Assert.Empty(pair.DestinationsProgress);

        var destination = new DestinationProgressViewModel(@"C:\dest");
        pair.DestinationsProgress.Add(destination);

        Assert.Equal(@"C:\dest", destination.Path);
        Assert.Equal(CopyStateKind.Copying, destination.StateKind);
        Assert.Equal(0, destination.Progress);
        Assert.Empty(destination.CopyingFiles);
        Assert.Single(pair.DestinationsProgress);
    }

    [Fact]
    public void DestinationProgressViewModel_PropertySetters_RaisePropertyChanged()
    {
        var destination = new DestinationProgressViewModel("/dest");
        var raised = new List<string>();
        destination.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        destination.Progress = 0.5;
        destination.Status = "Copia in corso";
        destination.SpeedText = "10 MB/s";
        destination.StateKind = CopyStateKind.Error;
        destination.ErrorMessage = "disco pieno";

        Assert.Contains(nameof(DestinationProgressViewModel.Progress), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.Status), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.SpeedText), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.StateKind), raised);
        Assert.Contains(nameof(DestinationProgressViewModel.ErrorMessage), raised);
    }
}
```

- [ ] **Step 2: Esegui i test — devono fallire (i tipi non esistono ancora)**

Run: `dotnet test --filter "FullyQualifiedName~FolderFilePairViewModelTests"`
Expected: FAIL a compilazione.

- [ ] **Step 3: Aggiungi `DestinationProgressViewModel` e sostituisci `CopyingFiles`**

In `Sbroglione/ViewModels/FolderFilePairViewModel.cs`, dopo la classe `ExtraDestinationViewModel` (dopo la riga 23), aggiungi:

```csharp
/// <summary>
/// Avanzamento, velocità e stato di una singola destinazione durante una copia
/// multi-destinazione: ogni destinazione procede al proprio ritmo e può fallire
/// indipendentemente dalle altre.
/// </summary>
public sealed class DestinationProgressViewModel : ReactiveObject
{
    public DestinationProgressViewModel(string path) => Path = path;

    public string Path { get; }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string? _status;
    public string? Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private string? _speedText;
    public string? SpeedText
    {
        get => _speedText;
        set => this.RaiseAndSetIfChanged(ref _speedText, value);
    }

    /// <summary>Velocità istantanea in byte/s: non a binding diretto, usata per aggregare la velocità totale del pair.</summary>
    public double CurrentBytesPerSecond { get; set; }

    private CopyStateKind _stateKind = CopyStateKind.Copying;
    public CopyStateKind StateKind
    {
        get => _stateKind;
        set => this.RaiseAndSetIfChanged(ref _stateKind, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>File attualmente in copia verso questa destinazione (sottoinsieme di FilesToProcess).</summary>
    public ObservableCollection<FileSystemItem> CopyingFiles { get; } = new();
}
```

Sostituisci la proprietà `CopyingFiles` di `FolderFilePairViewModel` (riga 210-211):

```csharp
    /// <summary>Avanzamento per destinazione durante una copia, per il widget "in corso".</summary>
    public ObservableCollection<DestinationProgressViewModel> DestinationsProgress { get; } = new();
```

- [ ] **Step 4: Esegui i test — devono passare**

Run: `dotnet test --filter "FullyQualifiedName~FolderFilePairViewModelTests"`
Expected: PASS

Nota: a questo punto `CopyPairsViewModel.cs` e `CopyPairsView.axaml` non compilano più (riferimenti a `pair.CopyingFiles`) — verranno sistemati nei Task 4-6. `dotnet build` sull'intera solution fallisce fino ad allora; è atteso.

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/ViewModels/FolderFilePairViewModel.cs Sbroglione.Tests/FolderFilePairViewModelTests.cs
git commit -m "feat: DestinationProgressViewModel per stato per-destinazione, sostituisce CopyingFiles condivisa"
```

---

### Task 4: `CopyPairsViewModel.CopySingleFileAsync` — progresso/velocità/errore per destinazione

**Files:**
- Modify: `Sbroglione/ViewModels/CopyPairsViewModel.cs:341-444` (`StartCopyAsync`) e `:539-615` (`CopySingleFileAsync`)
- Test: `Sbroglione.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes: `FileCopyService.CopyFileToManyAsync` (Task 1), `DestinationProgressViewModel`/`DestinationsProgress` (Task 3).
- Produces: `private static void PublishDestinationSpeed(FolderFilePairViewModel pair, DestinationProgressViewModel target, SpeedSnapshot snapshot)`, `private static void RecomputePairAggregate(FolderFilePairViewModel pair)`, `private static CopyStateKind AggregatePairState(FolderFilePairViewModel pair)` — usati anche da Task 5.

- [ ] **Step 1: Aggiorna `StartCopyAsync` per popolare `DestinationsProgress`**

In `Sbroglione/ViewModels/CopyPairsViewModel.cs`, sostituisci (riga 405-407):

```csharp
            foreach (var item in pair.FilesToProcess)
                item.Status = FileCopyStatus.Pending;
            pair.CopyingFiles.Clear();
```

con:

```csharp
            foreach (var item in pair.FilesToProcess)
                item.Status = FileCopyStatus.Pending;
            pair.DestinationsProgress.Clear();
            foreach (var destination in destinations)
                pair.DestinationsProgress.Add(new DestinationProgressViewModel(destination));
```

- [ ] **Step 2: Scrivi i test per il comportamento nuovo (fallimento parziale + aggregazione)**

Aggiungi in `Sbroglione.Tests/CopyPairsViewModelTests.cs`:

```csharp
[Fact]
public async Task StartCopy_SingleFile_WithExtraDestination_PopulatesDestinationsProgress()
{
    string sourceFile = Path.Combine(_root, "dp-source.txt");
    await File.WriteAllTextAsync(sourceFile, "dati");
    string destination1 = Path.Combine(_root, "dp-dest1.txt");
    string destination2 = Path.Combine(_root, "dp-dest2.txt");

    var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destination1 };
    pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, destination2));
    await pair.SourceStateRefresh;

    var vm = new CopyPairsViewModel();
    await vm.StartCopyAsync(pair);

    Assert.Equal(2, pair.DestinationsProgress.Count);
    Assert.All(pair.DestinationsProgress, d => Assert.Equal(CopyStateKind.Success, d.StateKind));
    Assert.All(pair.DestinationsProgress, d => Assert.Equal(1, d.Progress, 3));
    Assert.Equal(CopyStateKind.Success, pair.StateKind);
}

[Fact]
public async Task StartCopy_SingleFile_OneDestinationUnwritable_MarksThatDestinationErrorAndPairError()
{
    AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

    string sourceFile = Path.Combine(_root, "dp-fail-source.txt");
    await File.WriteAllTextAsync(sourceFile, "dati");
    string goodDestination = Path.Combine(_root, "dp-fail-good.txt");
    // Destinazione dentro una cartella inesistente: FileCopyService la marca fallita.
    string badDestination = Path.Combine(_root, "dp-fail-missing-dir", "bad.txt");

    var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = goodDestination };
    pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, badDestination));
    await pair.SourceStateRefresh;

    var vm = new CopyPairsViewModel();
    await vm.StartCopyAsync(pair);

    var goodEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == goodDestination);
    var badEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == badDestination);
    Assert.Equal(CopyStateKind.Success, goodEntry.StateKind);
    Assert.Equal(CopyStateKind.Error, badEntry.StateKind);
    Assert.NotNull(badEntry.ErrorMessage);
    Assert.Equal(CopyStateKind.Error, pair.StateKind);
    Assert.Equal("dati", await File.ReadAllTextAsync(goodDestination));
}
```

- [ ] **Step 3: Esegui i test — devono fallire**

Run: `dotnet test --filter "FullyQualifiedName~StartCopy_SingleFile_WithExtraDestination_PopulatesDestinationsProgress|StartCopy_SingleFile_OneDestinationUnwritable"`
Expected: FAIL (compilazione: `CopyingFiles` non esiste più, `CopyFileToManyAsync` ha firma diversa; oppure a runtime finché `CopySingleFileAsync` non è riscritto).

- [ ] **Step 4: Riscrivi `CopySingleFileAsync` e aggiungi gli helper di aggregazione**

Sostituisci `CopySingleFileAsync` (righe 539-615) con:

```csharp
    private static async Task CopySingleFileAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        // Se la sorgente è un file e una destinazione è una cartella, il file viene copiato dentro la cartella.
        var destinationFiles = new List<string>();
        foreach (var destination in destinations)
        {
            bool intoFolder = await FileSystemService.GetPathTypeAsync(destination) == PathType.Directory;
            destinationFiles.Add(intoFolder
                ? Path.Combine(destination, Path.GetFileName(pair.SourcePath!))
                : destination);
        }

        long totalBytes = new FileInfo(pair.SourcePath!).Length;

        // DestinationsProgress è stata popolata da StartCopyAsync nello stesso ordine di
        // `destinations`: gli indici corrispondono a `destinationFiles`.
        var vmByResolvedPath = new Dictionary<string, DestinationProgressViewModel>();
        var trackers = new Dictionary<string, (SpeedTracker Tracker, MonotonicProgressGate TrackerGate, MonotonicProgressGate UiGate, UiProgressThrottle UiThrottle, StrongCopiedBytes CopiedBytes)>();
        for (int i = 0; i < destinations.Count; i++)
        {
            var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            tracker.Start(totalBytes);
            vmByResolvedPath[destinationFiles[i]] = pair.DestinationsProgress[i];
            trackers[destinationFiles[i]] = (tracker, new MonotonicProgressGate(), new MonotonicProgressGate(), new UiProgressThrottle(), new StrongCopiedBytes());
        }

        var copyResult = await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, (destinationFile, deltaBytes) =>
        {
            var (tracker, trackerGate, uiGate, uiThrottle, copiedBytes) = trackers[destinationFile];
            long total = Interlocked.Add(ref copiedBytes.Value, deltaBytes);
            if (!trackerGate.TryAdvance(total))
                return;

            tracker.Report(total);
            bool haveSnapshot = tracker.TryTakeSnapshot(out SpeedSnapshot snapshot);
            if (!haveSnapshot && !uiThrottle.ShouldPublish())
                return;

            double fraction = totalBytes > 0 ? (double)total / totalBytes : 1;
            var target = vmByResolvedPath[destinationFile];
            UiDispatch.Post(() =>
            {
                if (uiGate.TryAdvance(fraction))
                    target.Progress = fraction;
                if (haveSnapshot)
                    PublishDestinationSpeed(pair, target, snapshot);
                RecomputePairAggregate(pair);
            });
        }, ct, AppSettingsStore.Current.BufferSizeBytes);

        foreach (var destinationFile in copyResult.SucceededDestinations)
        {
            var target = vmByResolvedPath[destinationFile];
            var tracker = trackers[destinationFile].Tracker;
            target.SpeedText = string.Format(
                LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"),
                FormatSpeed(tracker.AverageBytesPerSecond),
                FormatSpeed(tracker.PeakBytesPerSecond));
            target.StateKind = CopyStateKind.Success;
        }

        foreach (var (destinationFile, error) in copyResult.FailedDestinations)
        {
            var target = vmByResolvedPath[destinationFile];
            target.StateKind = CopyStateKind.Error;
            target.ErrorMessage = error.Message;
            target.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.DestinationErrorFormat"), error.Message);
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.StateKind = AggregatePairState(pair);
            pair.Status = pair.StateKind == CopyStateKind.Error
                ? string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"), copyResult.SucceededDestinations.Count, destinations.Count)
                : LocalizationService.Tr("Str.CopyPairs.Completed");
            return;
        }

        // Verifica checksum solo sulle destinazioni riuscite.
        pair.Status = LocalizationService.Tr("Str.CopyPairs.VerifyingChecksum");
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);

        bool allMatch = true;
        foreach (var destinationFile in copyResult.SucceededDestinations)
        {
            string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
            pair.DestinationChecksum = destinationHash;
            bool matches = string.Equals(pair.SourceChecksum, destinationHash, StringComparison.OrdinalIgnoreCase);
            allMatch &= matches;
            if (!matches)
                vmByResolvedPath[destinationFile].StateKind = CopyStateKind.Warning;
        }

        pair.IsVerified = allMatch && copyResult.FailedDestinations.Count == 0;
        pair.Progress = 1;
        pair.StateKind = AggregatePairState(pair);
        pair.Status = pair.StateKind switch
        {
            CopyStateKind.Error => string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"), copyResult.SucceededDestinations.Count, destinations.Count),
            CopyStateKind.Warning => LocalizationService.Tr("Str.CopyPairs.CompletedChecksumMismatch"),
            _ => LocalizationService.Tr("Str.CopyPairs.Completed")
        };
    }

    /// <summary>Contenitore per un contatore byte mutabile per destinazione, target di <see cref="Interlocked.Add(ref long, long)"/>.</summary>
    private sealed class StrongCopiedBytes
    {
        public long Value;
    }

    private static void PublishDestinationSpeed(FolderFilePairViewModel pair, DestinationProgressViewModel target, SpeedSnapshot snapshot)
    {
        target.SpeedText = string.Format(
            LocalizationService.Tr("Str.CopyPairs.SpeedSummaryFormat"),
            FormatSpeed(snapshot.CurrentBytesPerSecond),
            FormatSpeed(snapshot.AverageBytesPerSecond),
            FormatSpeed(snapshot.PeakBytesPerSecond),
            FormatEta(snapshot.EtaSeconds));
        target.CurrentBytesPerSecond = snapshot.CurrentBytesPerSecond;
    }

    /// <summary>Ricalcola gli aggregati del pair dalle sue destinazioni: la più lenta pilota il progresso, la somma la velocità mostrata.</summary>
    private static void RecomputePairAggregate(FolderFilePairViewModel pair)
    {
        if (pair.DestinationsProgress.Count == 0)
            return;

        pair.Progress = pair.DestinationsProgress.Min(d => d.Progress);
        double totalSpeed = pair.DestinationsProgress.Sum(d => d.CurrentBytesPerSecond);
        pair.SpeedText = totalSpeed > 0
            ? string.Format(LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"), FormatSpeed(totalSpeed), FormatSpeed(totalSpeed))
            : pair.SpeedText;
    }

    /// <summary>Priorità Error > Warning > Success sulle destinazioni del pair.</summary>
    private static CopyStateKind AggregatePairState(FolderFilePairViewModel pair)
    {
        if (pair.DestinationsProgress.Any(d => d.StateKind == CopyStateKind.Error))
            return CopyStateKind.Error;
        if (pair.DestinationsProgress.Any(d => d.StateKind == CopyStateKind.Warning))
            return CopyStateKind.Warning;
        return CopyStateKind.Success;
    }
```

Nota: `RecomputePairAggregate` usa `Str.CopyPairs.SpeedAveragePeakFormat` con lo stesso valore due volte (non abbiamo un "media"/"picco" aggregato distinto, solo il throughput istantaneo totale) — accettabile come approssimazione: il dettaglio media/picco resta nel widget per-destinazione via `PublishDestinationSpeed`.

- [ ] **Step 5: Esegui i test — devono passare**

Run: `dotnet test --filter "FullyQualifiedName~CopyPairsViewModelTests"`
Expected: Compilazione ancora rotta per `CopyDirectoryAsync`/`DirectoryCopyProgressPublisher` (Task 5) e per l'AXAML (Task 6) — se il progetto Desktop/AXAML non fa parte della build di `Sbroglione.Tests`, la sola `Sbroglione.Tests` può già passare per i test single-file; altrimenti annota il fallimento atteso e prosegui a Task 5 prima di verificare l'intera suite.

- [ ] **Step 6: Aggiungi la chiave di localizzazione mancante**

In `Sbroglione/Services/Localization/StringsIt.cs`, vicino a `Str.CopyPairs.VerifyFailedFormat` (riga 145):

```csharp
        ["Str.CopyPairs.DestinationErrorFormat"] = "Errore: {0}",
        ["Str.CopyPairs.CompletedWithErrorsFormat"] = "Completato con errori ({0}/{1} destinazioni)",
```

In `Sbroglione/Services/Localization/StringsEn.cs`, vicino alla riga 141:

```csharp
        ["Str.CopyPairs.DestinationErrorFormat"] = "Error: {0}",
        ["Str.CopyPairs.CompletedWithErrorsFormat"] = "Completed with errors ({0}/{1} destinations)",
```

- [ ] **Step 7: Commit**

```bash
git add Sbroglione/ViewModels/CopyPairsViewModel.cs Sbroglione/Services/Localization/StringsIt.cs Sbroglione/Services/Localization/StringsEn.cs Sbroglione.Tests/CopyPairsViewModelTests.cs
git commit -m "feat: CopySingleFileAsync traccia progresso/velocita/errore per destinazione"
```

---

### Task 5: `CopyPairsViewModel.CopyDirectoryAsync` + `DirectoryCopyProgressPublisher` per destinazione

**Files:**
- Modify: `Sbroglione/ViewModels/CopyPairsViewModel.cs:617-816` (`CopyDirectoryAsync` e `DirectoryCopyProgressPublisher`)
- Test: `Sbroglione.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes: `FileCopyService.CopyDirectoryToManyAsync` (Task 2), `PublishDestinationSpeed`/`RecomputePairAggregate`/`AggregatePairState` (Task 4).
- Produces: `DirectoryCopyProgressPublisher(FolderFilePairViewModel pair, DestinationProgressViewModel target, SpeedTracker tracker, UiProgressThrottle? uiThrottle = null)` — la firma pubblica del costruttore cambia (prima: `pair, tracker, uiThrottle`); `KnownFileCount` e `Report(CopyProgress)` restano.

- [ ] **Step 1: Aggiorna i due test unitari esistenti sul publisher**

`Sbroglione.Tests/CopyPairsViewModelTests.cs` righe 48-93: il publisher ora scrive su un `DestinationProgressViewModel`, non più direttamente su `pair`.

```csharp
[Fact]
public void DirectoryProgress_StaleCumulativeReport_DoesNotRegressProgress()
{
    var pair = new FolderFilePairViewModel();
    var destination = new DestinationProgressViewModel("/dest");
    var tracker = new SpeedTracker(() => 0);      // orologio fermo: fuori scopo qui
    var publisher = new CopyPairsViewModel.DirectoryCopyProgressPublisher(
        pair, destination, tracker, new UiProgressThrottle(TimeSpan.Zero));   // niente throttle nei test

    publisher.Report(new CopyProgress(CopiedBytes: 6, TotalBytes: 10, TotalFiles: 3));
    Assert.Equal(string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), 3), destination.Status);
    Assert.Equal(0.6, destination.Progress, 3);

    publisher.Report(new CopyProgress(CopiedBytes: 5, TotalBytes: 10, TotalFiles: 3));
    Assert.Equal(0.6, destination.Progress, 3);          // cumulativo stantio: ignorato

    publisher.Report(new CopyProgress(CopiedBytes: 9, TotalBytes: 10, TotalFiles: 3));
    Assert.Equal(0.9, destination.Progress, 3);
    Assert.Equal(3, publisher.KnownFileCount);
}

[Fact]
public void DirectoryProgress_UiPostsRunOutOfOrder_KeepHighestProgress()
{
    var posted = new List<Action>();
    UiDispatch.Override = posted.Add;             // marshaling differito, non inline

    var pair = new FolderFilePairViewModel();
    var destination = new DestinationProgressViewModel("/dest");
    var tracker = new SpeedTracker(() => 0);
    var publisher = new CopyPairsViewModel.DirectoryCopyProgressPublisher(
        pair, destination, tracker, new UiProgressThrottle(TimeSpan.Zero));

    publisher.Report(new CopyProgress(CopiedBytes: 4, TotalBytes: 10, TotalFiles: 2));
    publisher.Report(new CopyProgress(CopiedBytes: 8, TotalBytes: 10, TotalFiles: 2));
    Assert.Equal(0, destination.Progress);               // nulla applicato finché i Post non girano

    posted.Reverse();                             // il dispatcher li esegue al contrario
    foreach (Action action in posted)
        action();

    Assert.Equal(0.8, destination.Progress, 3);          // mai regredito a 0.4
    Assert.Equal(string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), 2), destination.Status);
}
```

- [ ] **Step 2: Nuovo test — copia cartella con una destinazione che fallisce su un file**

```csharp
[Fact]
public async Task StartCopy_Directory_OneDestinationFails_OtherSucceedsAndPairIsError()
{
    AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

    string sourceDir = Path.Combine(_root, "dir-fail-src");
    Directory.CreateDirectory(sourceDir);
    await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");

    string goodDestination = Path.Combine(_root, "dir-fail-good");
    string badParent = Path.Combine(_root, "dir-fail-bad-parent");
    await File.WriteAllTextAsync(badParent, "non è una cartella");
    string badDestination = Path.Combine(badParent, "sub");

    var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = goodDestination };
    pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, badDestination));
    await pair.SourceStateRefresh;

    var vm = new CopyPairsViewModel();
    await vm.StartCopyAsync(pair);

    Assert.True(File.Exists(Path.Combine(goodDestination, "a.txt")));
    var goodEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == goodDestination);
    var badEntry = Assert.Single(pair.DestinationsProgress, d => d.Path == badDestination);
    Assert.Equal(CopyStateKind.Success, goodEntry.StateKind);
    Assert.Equal(CopyStateKind.Error, badEntry.StateKind);
    Assert.Equal(CopyStateKind.Error, pair.StateKind);
}
```

- [ ] **Step 3: Esegui i test — devono fallire (costruttore/firma non ancora aggiornati)**

Run: `dotnet test --filter "FullyQualifiedName~DirectoryProgress_|StartCopy_Directory_OneDestinationFails"`
Expected: FAIL a compilazione.

- [ ] **Step 4: Riscrivi `CopyDirectoryAsync` e `DirectoryCopyProgressPublisher`**

Sostituisci l'intero blocco `CopyDirectoryAsync` + `DirectoryCopyProgressPublisher` (righe 617-816) con:

```csharp
    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        var vmByRoot = new Dictionary<string, DestinationProgressViewModel>();
        var trackerByRoot = new Dictionary<string, SpeedTracker>();
        var publisherByRoot = new Dictionary<string, DirectoryCopyProgressPublisher>();
        for (int i = 0; i < destinations.Count; i++)
        {
            var vm = pair.DestinationsProgress[i];
            vmByRoot[destinations[i]] = vm;
            var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            trackerByRoot[destinations[i]] = tracker;
            publisherByRoot[destinations[i]] = new DirectoryCopyProgressPublisher(pair, vm, tracker);
        }

        // Lookup per aggiornare lo stato per-file nella lista "File da elaborare": vuoto
        // (no-op) se l'Expander non è mai stato aperto, FilesToProcess non è ancora caricata.
        var filesByPath = pair.FilesToProcess.ToDictionary(f => f.FullPath, f => f);
        string primaryDestination = destinations[0];

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in destinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        var result = await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: (destination, progress) => publisherByRoot[destination].Report(progress),
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged,
            onFileStarted: (destination, sourceFile) =>
            {
                UiDispatch.Post(() =>
                {
                    var vm = vmByRoot[destination];
                    // filesByPath è vuoto se l'Expander "File da elaborare" non è mai stato
                    // aperto: il widget "In copia adesso" deve funzionare comunque, quindi
                    // qui costruiamo un item al volo invece di dipendere da quel listing.
                    var item = filesByPath.TryGetValue(sourceFile, out var existing)
                        ? existing
                        : new FileSystemItem { Name = Path.GetFileName(sourceFile), FullPath = sourceFile };
                    if (destination == primaryDestination)
                        item.Status = FileCopyStatus.Copying;
                    vm.CopyingFiles.Add(item);
                });
            },
            onFileCompleted: (destination, sourceFile) =>
            {
                UiDispatch.Post(() =>
                {
                    var vm = vmByRoot[destination];
                    var toRemove = vm.CopyingFiles.FirstOrDefault(f => f.FullPath == sourceFile);
                    if (toRemove is not null)
                        vm.CopyingFiles.Remove(toRemove);
                    if (destination == primaryDestination && filesByPath.TryGetValue(sourceFile, out var item))
                        item.Status = FileCopyStatus.Done;
                });
            },
            onFileFailed: (destination, sourceFile, error) =>
            {
                UiDispatch.Post(() =>
                {
                    var vm = vmByRoot[destination];
                    vm.StateKind = CopyStateKind.Error;
                    vm.ErrorMessage = error.Message;
                    vm.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.DestinationErrorFormat"), error.Message);
                });
            });

        int knownFileCount = publisherByRoot.Values.Max(p => p.KnownFileCount);

        foreach (var destination in destinations)
        {
            var vm = vmByRoot[destination];
            var tracker = trackerByRoot[destination];
            if (publisherByRoot[destination].KnownFileCount > 0)
                vm.SpeedText = string.Format(
                    LocalizationService.Tr("Str.CopyPairs.SpeedAveragePeakFormat"),
                    FormatSpeed(tracker.AverageBytesPerSecond),
                    FormatSpeed(tracker.PeakBytesPerSecond));
            if (vm.StateKind != CopyStateKind.Error)
                vm.StateKind = CopyStateKind.Success;
        }

        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.StateKind = AggregatePairState(pair);
            pair.Status = pair.StateKind == CopyStateKind.Error
                ? string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"),
                    result.DestinationSucceeded.Values.Count(succeeded => succeeded), result.DestinationSucceeded.Count)
                : LocalizationService.Tr("Str.CopyPairs.Completed");
            return;
        }

        pair.Status = LocalizationService.Tr("Str.CopyPairs.VerifyingChecksum");
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in destinations)
        {
            if (!result.DestinationSucceeded[destination])
                continue; // destinazione fallita durante la copia: niente verifica, resta Error

            var vm = vmByRoot[destination];
            var verifyThrottle = new UiProgressThrottle();
            var verifyGate = new MonotonicProgressGate();

            var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
                pair.SourcePath!,
                destination,
                parallelism,
                progress =>
                {
                    if (!verifyThrottle.ShouldPublish())
                        return;

                    int verified = progress.VerifiedFiles;
                    int total = progress.TotalFiles;
                    UiDispatch.Post(() =>
                    {
                        if (verifyGate.TryAdvance(verified))
                            vm.Status = string.Format(LocalizationService.Tr("Str.CopyPairs.VerifyingChecksumProgressFormat"), verified, total);
                    });
                },
                ct);

            totalVerified = verifyResult.TotalFiles;
            mismatchedTotal += verifyResult.MismatchedFiles.Count;
            missingTotal += verifyResult.MissingFiles.Count;

            bool destinationVerified = verifyResult.MismatchedFiles.Count == 0 && verifyResult.MissingFiles.Count == 0;
            vm.StateKind = destinationVerified ? CopyStateKind.Success : CopyStateKind.Warning;
            vm.Status = destinationVerified
                ? string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedVerifiedFormat"), verifyResult.TotalFiles)
                : string.Format(LocalizationService.Tr("Str.CopyPairs.VerifyFailedFormat"), verifyResult.MismatchedFiles.Count, verifyResult.MissingFiles.Count);
        }

        pair.Progress = 1;
        pair.IsVerified = mismatchedTotal == 0 && missingTotal == 0 && result.DestinationSucceeded.Values.All(succeeded => succeeded);
        pair.StateKind = AggregatePairState(pair);
        pair.Status = pair.StateKind switch
        {
            CopyStateKind.Error => string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedWithErrorsFormat"),
                result.DestinationSucceeded.Values.Count(succeeded => succeeded), result.DestinationSucceeded.Count),
            CopyStateKind.Warning => string.Format(LocalizationService.Tr("Str.CopyPairs.VerifyFailedFormat"), mismatchedTotal, missingTotal),
            _ => string.Format(LocalizationService.Tr("Str.CopyPairs.CompletedVerifiedFormat"), totalVerified)
        };
    }

    /// <summary>
    /// Contabilità e pubblicazione del progresso di una destinazione durante una copia
    /// cartella: clamp monotono, tracker di velocità, throttle e marshaling sul thread UI
    /// in un punto solo. Classe (e non lambda) per avere un seam testabile: i callback del
    /// servizio arrivano da threadpool e in parallelo, quindi i cumulativi possono
    /// presentarsi fuori ordine (prima 6, poi 5) — condizione impossibile da provocare in
    /// modo deterministico passando da una copia reale.
    /// Un'istanza per (pair, destinazione).
    /// </summary>
    internal sealed class DirectoryCopyProgressPublisher
    {
        private readonly FolderFilePairViewModel _pair;
        private readonly DestinationProgressViewModel _target;
        private readonly SpeedTracker _tracker;
        private readonly UiProgressThrottle _uiThrottle;
        private readonly MonotonicProgressGate _trackerGate = new();
        private readonly MonotonicProgressGate _uiGate = new();
        private int _knownFileCount = -1;

        /// <param name="uiThrottle">
        /// Solo per i test: un throttle senza attesa fa passare ogni report, così le
        /// asserzioni riguardano il clamp e non la finestra temporale del throttle.
        /// </param>
        public DirectoryCopyProgressPublisher(
            FolderFilePairViewModel pair,
            DestinationProgressViewModel target,
            SpeedTracker tracker,
            UiProgressThrottle? uiThrottle = null)
        {
            _pair = pair;
            _target = target;
            _tracker = tracker;
            _uiThrottle = uiThrottle ?? new UiProgressThrottle();
        }

        /// <summary>Numero di file annunciato dal primo report; -1 se non è ancora arrivato.</summary>
        public int KnownFileCount => Volatile.Read(ref _knownFileCount);

        public void Report(CopyProgress progress)
        {
            // I callback arrivano da threadpool e in parallelo: il first-report deve
            // vincere una sola volta (altrimenti tracker.Start girerebbe più volte).
            bool firstReport = Interlocked.CompareExchange(ref _knownFileCount, progress.TotalFiles, -1) == -1;
            if (firstReport)
                _tracker.Start(progress.TotalBytes);

            // Cumulativi fuori ordine (Interlocked.Add e Invoke non sono atomici tra
            // loro nel servizio): scartati, così il tracker non torna indietro.
            bool advanced = _trackerGate.TryAdvance(progress.CopiedBytes);
            if (advanced)
                _tracker.Report(progress.CopiedBytes);

            SpeedSnapshot snapshot = default;
            bool haveSnapshot = advanced && _tracker.TryTakeSnapshot(out snapshot);
            if (!firstReport && !haveSnapshot && (!advanced || !_uiThrottle.ShouldPublish()))
                return;

            double fraction = progress.Fraction;
            int totalFiles = progress.TotalFiles;
            FolderFilePairViewModel pair = _pair;
            DestinationProgressViewModel target = _target;
            MonotonicProgressGate uiGate = _uiGate;
            UiDispatch.Post(() =>
            {
                if (firstReport)
                    target.Status = totalFiles == 0
                        ? LocalizationService.Tr("Str.CopyPairs.NoFilesToCopy")
                        : string.Format(LocalizationService.Tr("Str.CopyPairs.CopyingFolderFormat"), totalFiles);
                // Secondo clamp lato UI: anche due Post partiti in ordine possono essere
                // eseguiti fuori ordine dal dispatcher.
                if (advanced && uiGate.TryAdvance(fraction))
                    target.Progress = fraction;
                if (haveSnapshot)
                    PublishDestinationSpeed(pair, target, snapshot);
                RecomputePairAggregate(pair);
            });
        }
    }
}
```

- [ ] **Step 5: Esegui l'intera suite — deve passare**

Run: `dotnet test`
Expected: PASS (tutti i progetti, `Sbroglione.Tests` incluso)

- [ ] **Step 6: Commit**

```bash
git add Sbroglione/ViewModels/CopyPairsViewModel.cs Sbroglione.Tests/CopyPairsViewModelTests.cs
git commit -m "feat: CopyDirectoryAsync e DirectoryCopyProgressPublisher tracciano stato per destinazione"
```

---

### Task 6: Widget "in copia adesso" — barra/velocità/errore per destinazione

**Files:**
- Modify: `Sbroglione/Views/CopyPairsView.axaml:188-229`

**Interfaces:**
- Consumes: `FolderFilePairViewModel.DestinationsProgress` (Task 3), classi badge esistenti `Border.badge`/`Classes.error` (`Sbroglione/Styles/Controls.axaml`), converter `EnumEquals` già in uso nel file.

- [ ] **Step 1: Verifica manuale pre-modifica (baseline)**

Run: `dotnet build Sbroglione.sln` (deve essere pulita: Task 5 ha già sistemato tutti i riferimenti C#; solo l'AXAML in `CopyPairsView.axaml` referenzia ancora `AllDestinations`/`CopyingFiles` come binding — l'AXAML non causa errori di build C#, ma i binding falliscono silenziosamente a runtime finché non aggiornati in questo task).

- [ ] **Step 2: Sostituisci il blocco del widget**

In `Sbroglione/Views/CopyPairsView.axaml`, sostituisci le righe 188-229 (dal commento `<!-- File in copia adesso, divisi per destinazione -->` alla chiusura del `Border x:Name="CopyingNowWidget"`):

```xml
                  <!-- File in copia adesso, divisi per destinazione -->
                  <Border IsVisible="{Binding IsCopying}"
                          Background="{DynamicResource Brush.NeutralBg}"
                          CornerRadius="8"
                          Padding="10"
                          Margin="0,6,0,0">
                    <StackPanel Spacing="6">
                      <TextBlock Text="{DynamicResource Str.CopyPairs.CopyingNowHeader}"
                                 FontWeight="SemiBold" FontSize="12"
                                 Foreground="{DynamicResource Brush.TextMuted}" />
                      <ItemsControl ItemsSource="{Binding DestinationsProgress}">
                        <ItemsControl.ItemTemplate>
                          <DataTemplate>
                            <StackPanel Spacing="4" Margin="0,4,0,0">
                              <StackPanel Orientation="Horizontal" Spacing="6">
                                <i:Icon Value="fa-solid fa-folder" Width="14"
                                        Foreground="{DynamicResource Brush.Accent}" VerticalAlignment="Center" />
                                <TextBlock Text="{Binding Path}" FontSize="12" FontWeight="SemiBold"
                                           TextWrapping="Wrap"
                                           Foreground="{DynamicResource Brush.TextPrimary}" />
                                <Border Classes="badge error"
                                        IsVisible="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Error}"
                                        ToolTip.Tip="{Binding ErrorMessage}">
                                  <TextBlock Text="{DynamicResource Str.CopyPairs.DestinationErrorBadge}" FontSize="11" />
                                </Border>
                              </StackPanel>
                              <StackPanel Orientation="Horizontal" Spacing="8" Margin="20,0,0,0">
                                <ProgressBar Width="140" Height="6" Minimum="0" Maximum="1"
                                             Value="{Binding Progress}" VerticalAlignment="Center" />
                                <TextBlock Text="{Binding SpeedText}" FontSize="11"
                                           Foreground="{DynamicResource Brush.TextMuted}"
                                           IsVisible="{Binding SpeedText, Converter={x:Static ObjectConverters.IsNotNull}}" />
                              </StackPanel>
                              <ItemsControl ItemsSource="{Binding CopyingFiles}" Margin="20,0,0,0">
                                <ItemsControl.ItemsPanel>
                                  <ItemsPanelTemplate>
                                    <WrapPanel />
                                  </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                  <DataTemplate>
                                    <Border Classes="badge" Margin="0,0,6,4">
                                      <TextBlock Text="{Binding Name}" FontSize="11" />
                                    </Border>
                                  </DataTemplate>
                                </ItemsControl.ItemTemplate>
                              </ItemsControl>
                            </StackPanel>
                          </DataTemplate>
                        </ItemsControl.ItemTemplate>
                      </ItemsControl>
                    </StackPanel>
                  </Border>
```

Nota: `x:Name="CopyingNowWidget"` viene rimosso perché non più necessario — il binding indiretto `#CopyingNowWidget.DataContext.CopyingFiles` serviva solo perché la `CopyingFiles` era condivisa sul pair; ora `CopyingFiles` è una proprietà del `DestinationProgressViewModel` di ogni riga (`DataContext` implicito dell'`ItemTemplate` esterno), quindi il binding diretto `{Binding CopyingFiles}` nell'`ItemsControl` interno basta.

- [ ] **Step 3: Aggiungi la chiave di localizzazione per il badge errore**

In `Sbroglione/Services/Localization/StringsIt.cs`, vicino a `Str.CopyPairs.CopyingNowHeader` (riga 118):

```csharp
        ["Str.CopyPairs.DestinationErrorBadge"] = "Errore",
```

In `Sbroglione/Services/Localization/StringsEn.cs`, vicino alla riga 114:

```csharp
        ["Str.CopyPairs.DestinationErrorBadge"] = "Error",
```

- [ ] **Step 4: Build e avvio manuale per verifica visiva**

Run: `dotnet build Sbroglione.sln`
Expected: build pulita, zero errori.

Run: `dotnet run --project Sbroglione.Desktop` (avvio in background, poi ispezione manuale):
- Configura una coppia con 2+ destinazioni (una valida, una in un percorso non scrivibile, es. dentro un file esistente).
- Avvia la copia di una cartella con più file.
- Verifica: il widget mostra una riga per destinazione, ciascuna con la propria barra di progresso e velocità; la destinazione non scrivibile mostra il badge "Errore" con tooltip; la card di riepilogo passa a stato Error a fine copia.

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/Views/CopyPairsView.axaml Sbroglione/Services/Localization/StringsIt.cs Sbroglione/Services/Localization/StringsEn.cs
git commit -m "feat: widget in copia adesso mostra barra/velocita/errore per destinazione"
```

---

### Task 7: Verifica finale e pulizia IDEE.md

**Files:**
- Modify: `IDEE.md` (marca l'idea 25 come fatta)

**Interfaces:**
- Nessuna nuova interfaccia: task di chiusura.

- [ ] **Step 1: Esegui l'intera suite di test**

Run: `dotnet test`
Expected: PASS, nessuna regressione sui test esistenti (incluse le suite di `FileCopyServiceTests`, `CopyPairsViewModelTests`, `FolderFilePairViewModelTests`).

- [ ] **Step 2: Build completa**

Run: `dotnet build Sbroglione.sln`
Expected: zero errori, zero nuovi warning introdotti (confronta con l'output pre-modifica se necessario).

- [ ] **Step 3: Marca l'idea 25 come completata in `IDEE.md`**

Cambia la riga 67 di `IDEE.md` da `25. \`[ ]\` **Copia multi-destinazione...` a `25. \`[x]\` **Copia multi-destinazione...` (stesso testo, solo checkbox).

- [ ] **Step 4: Commit finale**

```bash
git add IDEE.md
git commit -m "docs: segna idea 25 (copia multi-destinazione indipendente) come completata"
```
