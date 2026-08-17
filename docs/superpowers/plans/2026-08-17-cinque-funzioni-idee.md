# Cinque funzioni IDEE (1, 2, 3, 13, 15) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementare i punti 1 (verifica post-copia per cartelle), 3 (copia multi-destinazione), 2 (coda di copia persistente con ripresa), 13 (ricerca duplicati) e 15 (treemap occupazione disco) di `IDEE.md`.

**Architecture:** Si estende il pattern esistente: servizi statici in `Services/`, ViewModel ReactiveUI in `ViewModels/`, viste axaml in `Views/` che creano il proprio ViewModel nel costruttore, persistenza JSON atomica in AppData (pattern `AppSettingsStore`). Le fasi sono ordinate per dipendenza: la verifica cartelle (Fase 1) e la copia multi-destinazione (Fase 2) toccano il motore di copia; la coda persistente (Fase 3) registra anche le destinazioni extra, quindi viene dopo; duplicati (Fase 4) e treemap (Fase 5) sono tab nuove e indipendenti.

**Tech Stack:** .NET 8, Avalonia 11, ReactiveUI, xunit, Projektanker.Icons.Avalonia (FontAwesome), System.Text.Json.

**Spec:** `IDEE.md` (punti 1, 2, 3, 13, 15).

## Global Constraints

- .NET 8, `dotnet build FileExplorer.sln`, test con `dotnet test`.
- Layering: Views → ViewModels → Services → Models. Nessun DI container. Servizi statici.
- Mai colori hardcoded nelle viste: sempre `{DynamicResource Brush.*}` da `Styles/Palette.axaml` (ThemeDictionaries Light+Dark). Icone via `i:Icon` / `i:Attached.Icon` con `fa-*`.
- Stringhe UI e commenti al codice in italiano (convenzione del codebase).
- Mai commit su `main`: ogni fase lavora su un branch dedicato e termina con una PR. Niente co-author Claude nei commit.
- Test: xunit, classi `sealed` + `IDisposable`, directory temporanee `Path.Combine(Path.GetTempPath(), "fe-<nome>-" + Guid.NewGuid().ToString("N"))`, stato statico (`AppSettingsStore.Current`, `CurrentPath`) salvato nel costruttore e ripristinato in `Dispose()`.
- Il file `.editorconfig` definisce lo stile; `dotnet format whitespace` gira in automatico via hook sui file modificati.
- Ogni task dichiara il modello per il subagente esecutore (`haiku` = meccanico, `sonnet` = standard, `opus` = logica complessa); il dispatcher lo passa al tool Agent.
- Al termine di ogni task: spuntare i checkbox del task in questo file.

**Branch per fase:**
- Fase 1: `feature/dir-copy-verify` (Task 1–2)
- Fase 2: `feature/multi-destination-copy` (Task 3–6)
- Fase 3: `feature/persistent-copy-journal` (Task 7–9)
- Fase 4: `feature/duplicate-finder` (Task 10–13)
- Fase 5: `feature/disk-usage-treemap` (Task 14–18)

Ogni fase parte da `main` aggiornato (`git checkout main && git pull && git checkout -b <branch>`) e termina con `gh pr create`. Le fasi 2 e 3 dipendono dalla fase precedente già mergiata (o si branchano dalla precedente se non ancora mergiata).

---

## Fase 1 — Verifica checksum post-copia per cartelle (IDEE punto 1)

Stato attuale: la copia di file singoli verifica già il checksum (`CopyPairsViewModel.CopySingleFileAsync`); la copia di cartelle lo salta esplicitamente (`CopyPairsViewModel.cs:103`). Questa fase aggiunge la verifica parallela dell'intero albero copiato.

### Task 1: DirectoryVerificationService

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/DirectoryVerificationService.cs`
- Test: `FileExplorer.Tests/DirectoryVerificationServiceTests.cs`

**Interfaces:**
- Consumes: `ChecksumService.ComputeSha256Async(string path, CancellationToken ct)` (esistente).
- Produces: `DirectoryVerificationService.VerifyDirectoryAsync(string sourceRoot, string destinationRoot, int maxDegreeOfParallelism, Action<VerifyProgress>? onProgress, CancellationToken ct)` → `Task<DirectoryVerifyResult>`; `readonly record struct VerifyProgress(int VerifiedFiles, int TotalFiles)`; `sealed record DirectoryVerifyResult(int TotalFiles, IReadOnlyList<string> MismatchedFiles, IReadOnlyList<string> MissingFiles)` con proprietà `bool IsSuccess`. I path in `MismatchedFiles`/`MissingFiles` sono relativi a `sourceRoot`.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/DirectoryVerificationServiceTests.cs`:

```csharp
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DirectoryVerificationServiceTests : IDisposable
{
    private readonly string _root;

    public DirectoryVerificationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(string Source, string Destination)> CreateCopiedTreeAsync()
    {
        string source = Path.Combine(_root, "src");
        string destination = Path.Combine(_root, "dst");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "contenuto a");
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "b.txt"), "contenuto b");
        await FileCopyService.CopyDirectoryAsync(source, destination, 2, null, CancellationToken.None);
        return (source, destination);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_IdenticalTrees_ReportsSuccess()
    {
        var (source, destination) = await CreateCopiedTreeAsync();

        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 2, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalFiles);
        Assert.Empty(result.MismatchedFiles);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_CorruptedDestinationFile_ReportsMismatchRelativePath()
    {
        var (source, destination) = await CreateCopiedTreeAsync();
        await File.WriteAllTextAsync(Path.Combine(destination, "sub", "b.txt"), "CORROTTO!!");

        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 2, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { Path.Combine("sub", "b.txt") }, result.MismatchedFiles);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_MissingDestinationFile_ReportsMissingAndProgress()
    {
        var (source, destination) = await CreateCopiedTreeAsync();
        File.Delete(Path.Combine(destination, "a.txt"));

        var progressEvents = new List<VerifyProgress>();
        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 1, progressEvents.Add, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { "a.txt" }, result.MissingFiles);
        Assert.Equal(2, progressEvents.Count);
        Assert.Equal(new VerifyProgress(2, 2), progressEvents[^1]);
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~DirectoryVerificationServiceTests"`
Expected: errore di compilazione — `DirectoryVerificationService` non esiste.

- [ ] **Step 3: Implementare il servizio**

Creare `FileExplorer/Services/DirectoryVerificationService.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Avanzamento della verifica checksum di una cartella.</summary>
public readonly record struct VerifyProgress(int VerifiedFiles, int TotalFiles);

/// <summary>
/// Esito della verifica: elenchi (in path relativi alla sorgente) dei file
/// con checksum diverso e dei file assenti in destinazione.
/// </summary>
public sealed record DirectoryVerifyResult(
    int TotalFiles,
    IReadOnlyList<string> MismatchedFiles,
    IReadOnlyList<string> MissingFiles)
{
    public bool IsSuccess => MismatchedFiles.Count == 0 && MissingFiles.Count == 0;
}

/// <summary>
/// Verifica post-copia di un albero di cartelle: confronta il checksum SHA-256
/// di ogni file sorgente con l'omologo in destinazione, più file in parallelo.
/// </summary>
public static class DirectoryVerificationService
{
    public static async Task<DirectoryVerifyResult> VerifyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<VerifyProgress>? onProgress,
        CancellationToken ct)
    {
        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();

        var mismatched = new ConcurrentBag<string>();
        var missing = new ConcurrentBag<string>();
        int verified = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationFile = Path.Combine(destinationRoot, relative);

                if (!File.Exists(destinationFile))
                {
                    missing.Add(relative);
                }
                else
                {
                    string sourceHash = await ChecksumService.ComputeSha256Async(sourceFile, ct);
                    string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
                    if (!string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
                        mismatched.Add(relative);
                }
            }
            finally
            {
                semaphore.Release();
                onProgress?.Invoke(new VerifyProgress(Interlocked.Increment(ref verified), files.Count));
            }
        });

        await Task.WhenAll(tasks);

        return new DirectoryVerifyResult(
            files.Count,
            mismatched.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            missing.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~DirectoryVerificationServiceTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/DirectoryVerificationService.cs FileExplorer.Tests/DirectoryVerificationServiceTests.cs
git commit -m "feat(verify): aggiungi DirectoryVerificationService per verifica post-copia cartelle"
```

### Task 2: Integrazione verifica cartelle in CopyPairsViewModel

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs:171-208` (metodo `CopyDirectoryAsync`) e il commento a `:103`
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes: `DirectoryVerificationService.VerifyDirectoryAsync(...)` (Task 1); `AppSettingsStore.Current.VerifyChecksumAfterCopy`; `FolderFilePairViewModel.IsVerified` (bool?), `Status`, `StateKind`, `Progress` (esistenti).
- Produces: dopo copia cartella con verifica attiva, `pair.IsVerified` valorizzato; `Status` = `"Completato e verificato (N file)"` (successo) o `"Verifica fallita: X file diversi, Y mancanti"` (`StateKind = Warning`).

- [ ] **Step 1: Scrivere il test che fallisce**

Aggiungere in fondo a `FileExplorer.Tests/CopyPairsViewModelTests.cs` (dentro la classe):

```csharp
    [Fact]
    public async Task StartCopy_Directory_ChecksumEnabled_VerifiesTreeAndMarksSuccess()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceDir = Path.Combine(_root, "vsrc");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "b.txt"), "bbb");
        string destinationDir = Path.Combine(_root, "vdst");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal("Completato e verificato (2 file)", pair.Status);
    }

    [Fact]
    public async Task StartCopy_Directory_ChecksumDisabled_SkipsVerification()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceDir = Path.Combine(_root, "vsrc2");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string destinationDir = Path.Combine(_root, "vdst2");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Null(pair.IsVerified);
        Assert.Equal("Completato", pair.Status);
    }
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~CopyPairsViewModelTests"`
Expected: i due test nuovi FALLISCONO (status `"Completato"` invece di `"Completato e verificato (2 file)"`, `IsVerified` null); gli altri passano.

- [ ] **Step 3: Modificare CopyDirectoryAsync**

In `FileExplorer/ViewModels/CopyPairsViewModel.cs`:

1. A riga 103, sostituire il commento `// La copia di cartelle non prevede la verifica checksum.` con `// La copia di cartelle verifica il checksum dell'intero albero (se abilitato).`
2. Sostituire il blocco finale del metodo `CopyDirectoryAsync` (righe 198-207, l'`if (!ct.IsCancellationRequested && knownFileCount != 0) { ... } else if ...`) con:

```csharp
        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        pair.Status = "Verifica checksum…";
        var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
            pair.SourcePath!,
            pair.DestinationPath!,
            parallelism,
            progress => pair.Status = $"Verifica checksum… ({progress.VerifiedFiles}/{progress.TotalFiles})",
            ct);

        pair.Progress = 1;
        pair.IsVerified = verifyResult.IsSuccess;

        if (verifyResult.IsSuccess)
        {
            pair.Status = $"Completato e verificato ({verifyResult.TotalFiles} file)";
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {verifyResult.MismatchedFiles.Count} file diversi, {verifyResult.MissingFiles.Count} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
```

Nota: la variabile `parallelism` è già in scope (riga 177).

- [ ] **Step 4: Eseguire tutti i test**

Run: `dotnet test`
Expected: tutti PASS (inclusi i test preesistenti `StartCopy_Directory_CopiesAllFilesWithResolvedParallelism` ecc.; se un test preesistente asserisce `Status == "Completato"` per una copia di cartella con default `VerifyChecksumAfterCopy = true`, aggiornarlo a `"Completato e verificato (N file)"` con N corretto).

- [ ] **Step 5: Build e commit**

Run: `dotnet build FileExplorer.sln` → 0 errori.

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer.Tests/CopyPairsViewModelTests.cs
git commit -m "feat(verify): verifica checksum post-copia anche per le cartelle"
```

- [ ] **Step 6: PR di fase**

```bash
git push -u origin feature/dir-copy-verify
gh pr create --title "feat: verifica checksum post-copia per cartelle (IDEE #1)" --body "$(cat <<'EOF'
## Summary
- nuovo DirectoryVerificationService: verifica SHA-256 parallela dell'albero copiato
- CopyPairsViewModel: verifica automatica dopo copia cartella se VerifyChecksumAfterCopy attivo
- esito in Status/StateKind/IsVerified (Warning con conteggio file diversi/mancanti)

## Test plan
- [ ] dotnet test (DirectoryVerificationServiceTests, CopyPairsViewModelTests)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Fase 2 — Copia multi-destinazione (IDEE punto 3)

Una lettura della sorgente, N scritture simultanee. Il pair guadagna destinazioni extra; il motore di copia guadagna le varianti `…ToManyAsync`.

### Task 3: FileCopyService.CopyFileToManyAsync

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs`
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Produces: `FileCopyService.CopyFileToManyAsync(string sourcePath, IReadOnlyList<string> destinationPaths, Action<long>? onBytesCopied, CancellationToken ct, int bufferSize = DefaultBufferSize)` → `Task`. `onBytesCopied` riceve i byte letti dalla sorgente (contati una sola volta, non per destinazione).

- [ ] **Step 1: Scrivere i test che falliscono**

Aggiungere in `FileExplorer.Tests/FileCopyServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: errore di compilazione — `CopyFileToManyAsync` non esiste.

- [ ] **Step 3: Implementare CopyFileToManyAsync**

Aggiungere in `FileExplorer/Services/FileCopyService.cs`, dopo `CopyFileAsync`:

```csharp
    /// <summary>
    /// Copia un file verso più destinazioni con una sola lettura della sorgente:
    /// ogni blocco letto viene scritto in parallelo su tutte le destinazioni.
    /// <paramref name="onBytesCopied"/> conta i byte letti (una volta sola, non per destinazione).
    /// </summary>
    public static async Task CopyFileToManyAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var outputs = new List<FileStream>(destinationPaths.Count);
        try
        {
            foreach (var destination in destinationPaths)
                outputs.Add(new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None));

            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                await Task.WhenAll(outputs.Select(o => o.WriteAsync(buffer.AsMemory(0, read), ct).AsTask()));
                onBytesCopied?.Invoke(read);
            }

            foreach (var output in outputs)
                await output.FlushAsync(ct);
        }
        finally
        {
            foreach (var output in outputs)
                await output.DisposeAsync();
        }
    }
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: tutti PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "feat(copy): copia file multi-destinazione con lettura singola della sorgente"
```

### Task 4: FileCopyService.CopyDirectoryToManyAsync

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs`
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Consumes: `CopyFileToManyAsync` (Task 3), `CopyProgress` (esistente).
- Produces: `FileCopyService.CopyDirectoryToManyAsync(string sourceRoot, IReadOnlyList<string> destinationRoots, int maxDegreeOfParallelism, Action<CopyProgress>? onProgress, CancellationToken ct, int bufferSize = DefaultBufferSize)` → `Task`. `CopyProgress.TotalBytes` = byte della sorgente (una volta sola).

- [ ] **Step 1: Scrivere il test che fallisce**

Aggiungere in `FileExplorer.Tests/FileCopyServiceTests.cs`:

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
```

- [ ] **Step 2: Eseguire il test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: errore di compilazione — `CopyDirectoryToManyAsync` non esiste.

- [ ] **Step 3: Implementare CopyDirectoryToManyAsync**

Aggiungere in `FileExplorer/Services/FileCopyService.cs`, dopo `CopyDirectoryAsync`:

```csharp
    /// <summary>
    /// Copia ricorsivamente una cartella verso più destinazioni (più file in parallelo),
    /// leggendo ogni file sorgente una sola volta. L'avanzamento conta i byte della sorgente.
    /// </summary>
    public static async Task CopyDirectoryToManyAsync(
        string sourceRoot,
        IReadOnlyList<string> destinationRoots,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        long totalBytes = files.Sum(file => new FileInfo(file).Length);

        onProgress?.Invoke(new CopyProgress(0, totalBytes, files.Count));
        if (files.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytes = 0;

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                var destinationFiles = destinationRoots
                    .Select(root => Path.Combine(root, relative))
                    .ToList();

                foreach (var destinationFile in destinationFiles)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                await CopyFileToManyAsync(sourceFile, destinationFiles, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: tutti PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "feat(copy): copia ricorsiva di cartelle verso più destinazioni"
```

### Task 5: Destinazioni extra nel ViewModel

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs`
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes: `FileCopyService.CopyFileToManyAsync` / `CopyDirectoryToManyAsync` (Task 3–4), `DirectoryVerificationService.VerifyDirectoryAsync` (Task 1), `DiskTypeService.GetDiskTypeAsync`, `CopyParallelismResolver.Resolve` (esistenti).
- Produces:
  - `ExtraDestinationViewModel` (nuova classe in `FolderFilePairViewModel.cs`): ctor `(FolderFilePairViewModel owner, string path)`, proprietà `FolderFilePairViewModel Owner { get; }`, `string Path { get; }`.
  - `FolderFilePairViewModel.ExtraDestinations` → `ObservableCollection<ExtraDestinationViewModel>`.
  - `FolderFilePairViewModel.AllDestinations` → `IReadOnlyList<string>` (destinazione primaria + extra).
  - `CopyPairsViewModel.AddExtraDestinationCommand` / `RemoveExtraDestinationCommand` (`ReactiveCommand<FolderFilePairViewModel, Unit>` / `ReactiveCommand<ExtraDestinationViewModel, Unit>`).

- [ ] **Step 1: Scrivere i test che falliscono**

Aggiungere in `FileExplorer.Tests/CopyPairsViewModelTests.cs`:

```csharp
    [Fact]
    public async Task StartCopy_SingleFile_WithExtraDestination_CopiesToBoth()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "multi-src.txt");
        await File.WriteAllTextAsync(sourceFile, "multi");
        string primary = Path.Combine(_root, "multi-d1.txt");
        string extra = Path.Combine(_root, "multi-d2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = primary };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Equal("multi", await File.ReadAllTextAsync(primary));
        Assert.Equal("multi", await File.ReadAllTextAsync(extra));
    }

    [Fact]
    public async Task StartCopy_Directory_WithExtraDestination_VerifiesEveryDestination()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceDir = Path.Combine(_root, "multi-vsrc");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");
        string primary = Path.Combine(_root, "multi-vd1");
        string extra = Path.Combine(_root, "multi-vd2");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = primary };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal("aaa", await File.ReadAllTextAsync(Path.Combine(extra, "a.txt")));
    }
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~CopyPairsViewModelTests"`
Expected: errore di compilazione — `ExtraDestinationViewModel` non esiste.

- [ ] **Step 3: Estendere FolderFilePairViewModel**

In `FileExplorer/ViewModels/FolderFilePairViewModel.cs` aggiungere in cima al file (dopo gli using, prima della classe esistente):

```csharp
/// <summary>Destinazione aggiuntiva di una coppia di copia (copia multi-destinazione).</summary>
public class ExtraDestinationViewModel
{
    public ExtraDestinationViewModel(FolderFilePairViewModel owner, string path)
    {
        Owner = owner;
        Path = path;
    }

    public FolderFilePairViewModel Owner { get; }
    public string Path { get; }
}
```

e dentro `FolderFilePairViewModel` (ad es. dopo la proprietà `DestinationPath`):

```csharp
    /// <summary>Destinazioni aggiuntive oltre a <see cref="DestinationPath"/>.</summary>
    public ObservableCollection<ExtraDestinationViewModel> ExtraDestinations { get; } = new();

    /// <summary>Tutte le destinazioni (primaria + extra). Valido solo quando CanStart è true.</summary>
    public IReadOnlyList<string> AllDestinations =>
        new[] { DestinationPath! }.Concat(ExtraDestinations.Select(e => e.Path)).ToList();
```

Aggiungere `using System.Linq;` agli using del file.

- [ ] **Step 4: Estendere CopyPairsViewModel**

In `FileExplorer/ViewModels/CopyPairsViewModel.cs`:

1. Nuovi comandi (dichiarazione accanto agli altri, riga 29-33):

```csharp
    public ReactiveCommand<FolderFilePairViewModel, Unit> AddExtraDestinationCommand { get; }
    public ReactiveCommand<ExtraDestinationViewModel, Unit> RemoveExtraDestinationCommand { get; }
```

2. Nel costruttore:

```csharp
        AddExtraDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(AddExtraDestinationAsync);
        RemoveExtraDestinationCommand = ReactiveCommand.Create<ExtraDestinationViewModel>(
            extra => extra.Owner.ExtraDestinations.Remove(extra));
```

3. Nuovo metodo:

```csharp
    private async Task AddExtraDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await ShowSelectPathDialogAsync(directoriesOnly: true, pair.DestinationPath);
        if (!string.IsNullOrEmpty(selected))
            pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, selected));
    }
```

4. In `CopySingleFileAsync`, sostituire la chiamata a `FileCopyService.CopyFileAsync` (righe 146-150) e la verifica finale (righe 160-168) per gestire N destinazioni:

```csharp
        var destinationFiles = new List<string>();
        foreach (var destination in pair.AllDestinations)
        {
            bool intoFolder = await FileSystemService.GetPathTypeAsync(destination) == PathType.Directory;
            destinationFiles.Add(intoFolder
                ? Path.Combine(destination, Path.GetFileName(pair.SourcePath!))
                : destination);
        }

        long totalBytes = new FileInfo(pair.SourcePath!).Length;
        long copiedBytes = 0;

        await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, deltaBytes =>
        {
            copiedBytes += deltaBytes;
            pair.Progress = totalBytes > 0 ? (double)copiedBytes / totalBytes : 1;
        }, ct, AppSettingsStore.Current.BufferSizeBytes);

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        // Verifica checksum di tutte le destinazioni.
        pair.Status = "Verifica checksum…";
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);

        bool allMatch = true;
        foreach (var destinationFile in destinationFiles)
        {
            string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
            pair.DestinationChecksum = destinationHash;
            allMatch &= string.Equals(pair.SourceChecksum, destinationHash, StringComparison.OrdinalIgnoreCase);
        }

        pair.IsVerified = allMatch;
        pair.Progress = 1;
        pair.Status = allMatch ? "Completato" : "Completato (checksum non corrisponde)";
        pair.StateKind = allMatch ? CopyStateKind.Success : CopyStateKind.Warning;
```

Nota: la variante a destinazione singola resta un caso particolare (lista con un solo elemento) — `CopyFileToManyAsync` la gestisce identicamente; i test esistenti sui file singoli devono continuare a passare invariati.

5. In `CopyDirectoryAsync`, sostituire il calcolo del parallelismo e la chiamata di copia (righe 175-196) con:

```csharp
        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in pair.AllDestinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            pair.AllDestinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: progress =>
            {
                if (knownFileCount != progress.TotalFiles)
                {
                    knownFileCount = progress.TotalFiles;
                    pair.Status = progress.TotalFiles == 0
                        ? "Nessun file da copiare"
                        : $"Copia cartella… ({progress.TotalFiles} file)";
                }

                pair.Progress = progress.Fraction;
            },
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes);
```

6. Sempre in `CopyDirectoryAsync`, nel blocco di verifica introdotto dal Task 2, sostituire la singola chiamata a `VerifyDirectoryAsync` con un ciclo su tutte le destinazioni:

```csharp
        pair.Status = "Verifica checksum…";
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in pair.AllDestinations)
        {
            var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
                pair.SourcePath!,
                destination,
                parallelism,
                progress => pair.Status = $"Verifica checksum… ({progress.VerifiedFiles}/{progress.TotalFiles})",
                ct);

            totalVerified = verifyResult.TotalFiles;
            mismatchedTotal += verifyResult.MismatchedFiles.Count;
            missingTotal += verifyResult.MissingFiles.Count;
        }

        pair.Progress = 1;
        pair.IsVerified = mismatchedTotal == 0 && missingTotal == 0;

        if (pair.IsVerified == true)
        {
            pair.Status = $"Completato e verificato ({totalVerified} file)";
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {mismatchedTotal} file diversi, {missingTotal} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
```

7. In `StartCopyAsync`, la creazione preventiva della cartella destinazione (riga 94) va estesa a tutte le destinazioni:

```csharp
            await Task.Run(() =>
            {
                foreach (var destination in pair.AllDestinations)
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            });
```

- [ ] **Step 5: Eseguire tutti i test**

Run: `dotnet test`
Expected: tutti PASS (nuovi e preesistenti).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer.Tests/CopyPairsViewModelTests.cs
git commit -m "feat(copy): destinazioni extra per coppia con copia e verifica multi-destinazione"
```

### Task 6: UI destinazioni extra in CopyPairsView

**Modello:** haiku

**Files:**
- Modify: `FileExplorer/Views/CopyPairsView.axaml` (card della coppia, dopo il Grid "Destinazione", righe 66-76)

**Interfaces:**
- Consumes: `ExtraDestinations`, `AddExtraDestinationCommand`, `RemoveExtraDestinationCommand` (Task 5).

- [ ] **Step 1: Aggiungere la UI**

In `FileExplorer/Views/CopyPairsView.axaml`, subito dopo il `Grid` commentato `<!-- Destinazione -->` (dopo la riga 76), inserire:

```xml
                  <!-- Destinazioni extra (copia multi-destinazione) -->
                  <ItemsControl ItemsSource="{Binding ExtraDestinations}">
                    <ItemsControl.ItemTemplate>
                      <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,4,0,0">
                          <i:Icon Grid.Column="0" Value="fa-solid fa-folder-plus" Width="26"
                                  Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
                          <TextBox Grid.Column="1" Text="{Binding Path}" IsReadOnly="True" Margin="8,0" />
                          <Button Grid.Column="2" Classes="iconbtn"
                                  i:Attached.Icon="fa-solid fa-xmark"
                                  Command="{Binding DataContext.RemoveExtraDestinationCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                  CommandParameter="{Binding}" />
                        </Grid>
                      </DataTemplate>
                    </ItemsControl.ItemTemplate>
                  </ItemsControl>

                  <Button Classes="secondary" HorizontalAlignment="Left"
                          Command="{Binding DataContext.AddExtraDestinationCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                          CommandParameter="{Binding}"
                          IsEnabled="{Binding !IsCopying}">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                      <i:Icon Value="fa-solid fa-folder-plus" />
                      <TextBlock Text="Aggiungi destinazione" />
                    </StackPanel>
                  </Button>
```

- [ ] **Step 2: Build e verifica manuale**

Run: `dotnet build FileExplorer.sln` → 0 errori.
Run (facoltativo, macchina con display): `dotnet run --project FileExplorer.Desktop` → nella card: bottone "Aggiungi destinazione" apre il dialog, la riga extra appare con la X per rimuoverla.

- [ ] **Step 3: Commit e PR di fase**

```bash
git add FileExplorer/Views/CopyPairsView.axaml
git commit -m "feat(copy): UI per destinazioni extra nella scheda Copia"
git push -u origin feature/multi-destination-copy
gh pr create --title "feat: copia multi-destinazione (IDEE #3)" --body "$(cat <<'EOF'
## Summary
- FileCopyService: CopyFileToManyAsync / CopyDirectoryToManyAsync (1 lettura → N scritture)
- FolderFilePairViewModel: ExtraDestinations + AllDestinations
- CopyPairsViewModel: comandi aggiungi/rimuovi destinazione, copia e verifica su tutte le destinazioni
- CopyPairsView: righe destinazioni extra nella card

## Test plan
- [ ] dotnet test (FileCopyServiceTests, CopyPairsViewModelTests)
- [ ] verifica manuale UI

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Fase 3 — Coda di copia persistente con ripresa (IDEE punto 2)

Un journal JSON in AppData registra le copie in corso; le voci rimaste dopo un crash vengono riproposte all'avvio come coppie "interrotte", riprendibili saltando i file già copiati (stessa dimensione + stessa data di modifica — per questo la copia deve preservare il timestamp sorgente).

**Decisione di scope:** niente ripresa a offset dentro il singolo file (menzionata in IDEE come possibile raffinamento): un file parziale ha dimensione diversa dalla sorgente e viene semplicemente ricopiato da zero. Semplice, corretto, e per file di dimensioni normali la differenza è trascurabile.

### Task 7: Preservazione mtime + skipUnchanged nel motore di copia

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs`
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Produces:
  - `CopyFileAsync` e `CopyFileToManyAsync` impostano `File.SetLastWriteTimeUtc(destinazione, mtime sorgente)` a copia conclusa.
  - `CopyDirectoryAsync(..., int bufferSize = DefaultBufferSize, bool skipUnchanged = false)` e `CopyDirectoryToManyAsync(..., int bufferSize = DefaultBufferSize, bool skipUnchanged = false)`: con `skipUnchanged=true` un file è saltato se la destinazione esiste con stessa dimensione e `LastWriteTimeUtc` entro 2 secondi; i byte saltati contano comunque nell'avanzamento (per `…ToManyAsync` il salto avviene solo se TUTTE le destinazioni corrispondono).

- [ ] **Step 1: Scrivere i test che falliscono**

Aggiungere in `FileExplorer.Tests/FileCopyServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: il test mtime FALLISCE (mtime = ora della copia); il test skip FALLISCE per parametro `skipUnchanged` inesistente (errore di compilazione).

- [ ] **Step 3: Implementare**

In `FileExplorer/Services/FileCopyService.cs`:

1. In `CopyFileAsync`, racchiudere gli stream in un blocco esplicito e impostare il timestamp dopo la chiusura:

```csharp
        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                onBytesCopied?.Invoke(read);
            }

            await output.FlushAsync(ct);
        }

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato.
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
```

2. In `CopyFileToManyAsync`, dopo il blocco `finally` (a stream chiusi), aggiungere:

```csharp
        DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        foreach (var destination in destinationPaths)
            File.SetLastWriteTimeUtc(destination, sourceTime);
```

(spostare la chiusura del metodo di conseguenza: il `finally` con i `DisposeAsync` resta, il timestamp si imposta dopo il `try/finally`, solo se non ci sono state eccezioni — quindi mettere le due righe come ultime istruzioni del `try` NON va bene perché gli stream sono ancora aperti; la soluzione è: dopo il `try/finally`, le due righe qui sopra come ultime istruzioni del metodo. In caso di eccezione la copia fallisce prima e le righe non vengono eseguite.)

3. Aggiungere il parametro `bool skipUnchanged = false` in coda alla firma di `CopyDirectoryAsync` e `CopyDirectoryToManyAsync`. In `CopyDirectoryAsync`, dentro la lambda per file, subito dopo il calcolo di `destinationFile`:

```csharp
                if (skipUnchanged && IsUnchanged(sourceFile, destinationFile))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }
```

In `CopyDirectoryToManyAsync`, dopo il calcolo di `destinationFiles`:

```csharp
                if (skipUnchanged && destinationFiles.All(destination => IsUnchanged(sourceFile, destination)))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }
```

4. Helper privato in fondo alla classe:

```csharp
    /// <summary>
    /// True se la destinazione esiste con la stessa dimensione della sorgente e
    /// LastWriteTimeUtc entro 2 secondi (tolleranza per filesystem a granularità grossa).
    /// </summary>
    private static bool IsUnchanged(string sourceFile, string destinationFile)
    {
        var destinationInfo = new FileInfo(destinationFile);
        if (!destinationInfo.Exists)
            return false;

        var sourceInfo = new FileInfo(sourceFile);
        return destinationInfo.Length == sourceInfo.Length
               && Math.Abs((destinationInfo.LastWriteTimeUtc - sourceInfo.LastWriteTimeUtc).TotalSeconds) < 2;
    }
```

- [ ] **Step 4: Eseguire tutti i test**

Run: `dotnet test`
Expected: tutti PASS (i test esistenti non asseriscono i timestamp, restano verdi).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "feat(copy): preserva mtime e aggiungi skipUnchanged per la ripresa delle copie"
```

### Task 8: CopyJobRecord + CopyJournalStore

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Models/CopyJobRecord.cs`
- Create: `FileExplorer/Services/CopyJournalStore.cs`
- Test: `FileExplorer.Tests/CopyJournalStoreTests.cs`

**Interfaces:**
- Produces:
  - `CopyJobRecord`: `Guid Id` (default `Guid.NewGuid()`), `string SourcePath`, `string DestinationPath`, `List<string> ExtraDestinations`, `DateTime StartedUtc`.
  - `CopyJournalStore` (statico, pattern `AppSettingsStore`): `string DefaultPath`, `string CurrentPath { get; set; }`, `Task<List<CopyJobRecord>> LoadAsync()`, `Task AddAsync(CopyJobRecord record)`, `Task RemoveAsync(Guid id)`, `Task ClearAsync()`. Scrittura atomica (tmp + move), accessi serializzati da `SemaphoreSlim`, file corrotto/assente → lista vuota.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/CopyJournalStoreTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CopyJournalStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalCurrentPath;

    public CopyJournalStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrentPath = CopyJournalStore.CurrentPath;
        CopyJournalStore.CurrentPath = Path.Combine(_root, "sub", "copy-journal.json");
    }

    public void Dispose()
    {
        CopyJournalStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await CopyJournalStore.LoadAsync());
    }

    [Fact]
    public async Task AddAsync_ThenLoad_RoundTripsRecord()
    {
        var record = new CopyJobRecord
        {
            SourcePath = "/tmp/src",
            DestinationPath = "/tmp/dst",
            ExtraDestinations = { "/tmp/dst2" },
            StartedUtc = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc)
        };

        await CopyJournalStore.AddAsync(record);
        var loaded = await CopyJournalStore.LoadAsync();

        var single = Assert.Single(loaded);
        Assert.Equal(record.Id, single.Id);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal(new[] { "/tmp/dst2" }, single.ExtraDestinations);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyMatchingRecord()
    {
        var first = new CopyJobRecord { SourcePath = "/a", DestinationPath = "/b" };
        var second = new CopyJobRecord { SourcePath = "/c", DestinationPath = "/d" };
        await CopyJournalStore.AddAsync(first);
        await CopyJournalStore.AddAsync(second);

        await CopyJournalStore.RemoveAsync(first.Id);

        var loaded = await CopyJournalStore.LoadAsync();
        Assert.Equal(second.Id, Assert.Single(loaded).Id);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CopyJournalStore.CurrentPath)!);
        await File.WriteAllTextAsync(CopyJournalStore.CurrentPath, "{ json rotto");

        Assert.Empty(await CopyJournalStore.LoadAsync());
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~CopyJournalStoreTests"`
Expected: errore di compilazione — tipi inesistenti.

- [ ] **Step 3: Implementare modello e store**

Creare `FileExplorer/Models/CopyJobRecord.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>
/// Voce del journal delle copie: una copia avviata e non ancora conclusa.
/// Le voci rimaste su disco all'avvio indicano copie interrotte (crash/chiusura).
/// </summary>
public class CopyJobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public List<string> ExtraDestinations { get; set; } = new();
    public DateTime StartedUtc { get; set; }
}
```

Creare `FileExplorer/Services/CopyJournalStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Journal persistente delle copie in corso (JSON in AppData, pattern
/// <see cref="AppSettingsStore"/>): scrittura atomica e accessi serializzati.
/// </summary>
public static class CopyJournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>Percorso predefinito del file journal.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "copy-journal.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>Carica il journal; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<CopyJobRecord>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<CopyJobRecord>();

            await using var stream = File.OpenRead(CurrentPath);
            return await JsonSerializer.DeserializeAsync<List<CopyJobRecord>>(stream, Options).ConfigureAwait(false)
                   ?? new List<CopyJobRecord>();
        }
        catch (Exception)
        {
            return new List<CopyJobRecord>();
        }
    }

    /// <summary>Aggiunge una voce e salva.</summary>
    public static async Task AddAsync(CopyJobRecord record)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<CopyJobRecord> jobs = await LoadAsync().ConfigureAwait(false);
            jobs.Add(record);
            await SaveAsync(jobs).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Rimuove la voce con l'id indicato (no-op se assente) e salva.</summary>
    public static async Task RemoveAsync(Guid id)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<CopyJobRecord> jobs = await LoadAsync().ConfigureAwait(false);
            jobs.RemoveAll(job => job.Id == id);
            await SaveAsync(jobs).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Svuota il journal.</summary>
    public static async Task ClearAsync()
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(new List<CopyJobRecord>()).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task SaveAsync(List<CopyJobRecord> jobs)
    {
        string? directory = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = CurrentPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, jobs, Options).ConfigureAwait(false);
        }

        File.Move(tempPath, CurrentPath, overwrite: true);
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~CopyJournalStoreTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Models/CopyJobRecord.cs FileExplorer/Services/CopyJournalStore.cs FileExplorer.Tests/CopyJournalStoreTests.cs
git commit -m "feat(journal): journal persistente delle copie in corso"
```

### Task 9: Integrazione journal e ripresa in CopyPairsViewModel

**Modello:** opus

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs` (proprietà `SkipUnchanged`)
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs` (costruttore/Dispose + test nuovi)

**Interfaces:**
- Consumes: `CopyJournalStore` (Task 8), `skipUnchanged` (Task 7), `ExtraDestinationViewModel`/`AllDestinations` (Task 5).
- Produces:
  - `FolderFilePairViewModel.SkipUnchanged` (`bool`, default false): passato a `CopyDirectoryToManyAsync(..., skipUnchanged: pair.SkipUnchanged)`.
  - `CopyPairsViewModel.JournalRestore` (`Task`, pubblico): task del ripristino avviato dal costruttore; i test lo attendono.
  - All'avvio: voci residue nel journal → coppie in `PathPairs` con `Status = "Interrotto — premere Avvia per riprendere"`, `StateKind = Warning`, `SkipUnchanged = true`; journal svuotato dopo il ripristino.
  - Durante `StartCopyAsync`: voce aggiunta prima della copia, rimossa nel `finally` (successo, errore o annullamento — solo il crash la lascia su disco).

- [ ] **Step 1: Aggiornare il setup dei test esistenti**

In `FileExplorer.Tests/CopyPairsViewModelTests.cs` il costruttore deve reindirizzare il journal (altrimenti i test leggono/scrivono l'AppData reale). Sostituire costruttore e `Dispose` (righe 12-24) con:

```csharp
    private readonly string _originalJournalPath;

    public CopyPairsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copypairs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings();
        _originalJournalPath = CopyJournalStore.CurrentPath;
        CopyJournalStore.CurrentPath = Path.Combine(_root, "copy-journal.json");
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        CopyJournalStore.CurrentPath = _originalJournalPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
```

(la dichiarazione `private readonly string _originalJournalPath;` va accanto agli altri campi).

- [ ] **Step 2: Scrivere i test che falliscono**

Aggiungere in `FileExplorer.Tests/CopyPairsViewModelTests.cs`:

```csharp
    [Fact]
    public async Task Constructor_JournalWithLeftoverRecord_RestoresInterruptedPair()
    {
        string sourceDir = Path.Combine(_root, "jrn-src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "aaa");

        await CopyJournalStore.AddAsync(new CopyJobRecord
        {
            SourcePath = sourceDir,
            DestinationPath = Path.Combine(_root, "jrn-dst"),
            ExtraDestinations = { Path.Combine(_root, "jrn-dst2") }
        });

        var vm = new CopyPairsViewModel();
        await vm.JournalRestore;

        var pair = Assert.Single(vm.PathPairs);
        Assert.Equal(sourceDir, pair.SourcePath);
        Assert.Equal(CopyStateKind.Warning, pair.StateKind);
        Assert.Equal("Interrotto — premere Avvia per riprendere", pair.Status);
        Assert.True(pair.SkipUnchanged);
        Assert.Single(pair.ExtraDestinations);
        Assert.Empty(await CopyJournalStore.LoadAsync()); // journal svuotato dopo il ripristino
    }

    [Fact]
    public async Task StartCopy_SuccessfulCopy_LeavesJournalEmpty()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "jrn-file.txt");
        await File.WriteAllTextAsync(sourceFile, "contenuto");
        var pair = new FolderFilePairViewModel
        {
            SourcePath = sourceFile,
            DestinationPath = Path.Combine(_root, "jrn-file-dst.txt")
        };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.JournalRestore;
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Empty(await CopyJournalStore.LoadAsync());
    }
```

- [ ] **Step 3: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~CopyPairsViewModelTests"`
Expected: errore di compilazione — `JournalRestore` e `SkipUnchanged` non esistono.

- [ ] **Step 4: Implementare**

1. In `FileExplorer/ViewModels/FolderFilePairViewModel.cs`, dentro la classe:

```csharp
    /// <summary>
    /// True per le coppie ripristinate dal journal: la copia di cartelle salta
    /// i file già identici in destinazione (stessa dimensione e mtime).
    /// </summary>
    public bool SkipUnchanged { get; set; }
```

2. In `FileExplorer/ViewModels/CopyPairsViewModel.cs`:

Nel costruttore, come ultima istruzione:

```csharp
        JournalRestore = RestoreInterruptedJobsAsync();
```

Nuova proprietà e metodo:

```csharp
    /// <summary>
    /// Task del ripristino delle copie interrotte, avviato dal costruttore.
    /// I test lo attendono; la UI non ne ha bisogno.
    /// </summary>
    public Task JournalRestore { get; }

    /// <summary>
    /// Ripropone come coppie "interrotte" le voci rimaste nel journal
    /// (copie in corso al momento di un crash/chiusura), poi svuota il journal.
    /// </summary>
    private async Task RestoreInterruptedJobsAsync()
    {
        List<CopyJobRecord> jobs = await CopyJournalStore.LoadAsync();
        if (jobs.Count == 0)
            return;

        await CopyJournalStore.ClearAsync();

        foreach (var job in jobs)
        {
            var pair = new FolderFilePairViewModel
            {
                SourcePath = job.SourcePath,
                DestinationPath = job.DestinationPath,
                SkipUnchanged = true,
                Status = "Interrotto — premere Avvia per riprendere",
                StateKind = CopyStateKind.Warning
            };

            foreach (var extra in job.ExtraDestinations)
                pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));

            PathPairs.Add(pair);
        }
    }
```

In `StartCopyAsync`, subito dopo il check `CanStart` e prima della creazione del `CancellationTokenSource`:

```csharp
        var journalRecord = new CopyJobRecord
        {
            SourcePath = pair.SourcePath!,
            DestinationPath = pair.DestinationPath!,
            ExtraDestinations = pair.ExtraDestinations.Select(e => e.Path).ToList(),
            StartedUtc = DateTime.UtcNow
        };
        await CopyJournalStore.AddAsync(journalRecord);
```

e nel blocco `finally` esistente, come prima istruzione:

```csharp
            await CopyJournalStore.RemoveAsync(journalRecord.Id);
```

(aggiungere `using System.Linq;` se assente).

3. In `CopyDirectoryAsync`, passare il flag alla copia: nella chiamata a `FileCopyService.CopyDirectoryToManyAsync` aggiungere l'argomento finale `skipUnchanged: pair.SkipUnchanged`.

- [ ] **Step 5: Eseguire tutti i test**

Run: `dotnet test`
Expected: tutti PASS.

- [ ] **Step 6: Build, commit e PR di fase**

Run: `dotnet build FileExplorer.sln` → 0 errori.

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer.Tests/CopyPairsViewModelTests.cs
git commit -m "feat(journal): ripresa delle copie interrotte all'avvio via journal persistente"
git push -u origin feature/persistent-copy-journal
gh pr create --title "feat: coda di copia persistente con ripresa (IDEE #2)" --body "$(cat <<'EOF'
## Summary
- FileCopyService: preserva mtime sorgente e skipUnchanged (dimensione+mtime) per riprendere senza ricopiare
- CopyJournalStore: journal JSON atomico delle copie in corso (AppData)
- CopyPairsViewModel: registra la copia nel journal, la rimuove a fine corsa; all'avvio ripropone le copie interrotte come coppie riprendibili

## Test plan
- [ ] dotnet test (FileCopyServiceTests, CopyJournalStoreTests, CopyPairsViewModelTests)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Fase 4 — Ricerca duplicati (IDEE punto 13)

Nuova tab "Duplicati": scansione a cascata (dimensione → hash parziale 64 KB → hash completo), gruppi ordinati per spazio recuperabile, eliminazione per file o "tieni solo il primo".

**Decisione di scope:** l'azione "sostituisci con hardlink" citata in IDEE è rimandata (YAGNI: richiede gestione per-filesystem e non serve al flusso base); le azioni disponibili sono eliminazione singola e per gruppo.

### Task 10: Hash parziale in ChecksumService + SizeFormatter

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/ChecksumService.cs`
- Create: `FileExplorer/Services/SizeFormatter.cs`
- Test: `FileExplorer.Tests/ChecksumServiceTests.cs` (nuovo file)

**Interfaces:**
- Produces:
  - `ChecksumService.ComputeSha256Async(string path, long maxBytes, CancellationToken ct = default)` → `Task<string>`: SHA-256 dei primi `maxBytes` byte (dell'intero file se più corto). Con `maxBytes >= lunghezza file` il risultato coincide con l'overload esistente.
  - `SizeFormatter.Format(long bytes)` → `string` (`"512 B"`, `"1.5 KB"`, `"2.34 MB"`, `"1.02 GB"`; separatore decimale invariante di formattazione `.` non richiesto — usare la cultura corrente come il resto dell'app).

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/ChecksumServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~ChecksumServiceTests"`
Expected: errore di compilazione — overload e `SizeFormatter` inesistenti.

- [ ] **Step 3: Implementare**

Aggiungere in `FileExplorer/Services/ChecksumService.cs`:

```csharp
    /// <summary>
    /// Calcola il checksum SHA-256 dei primi <paramref name="maxBytes"/> byte del file
    /// (dell'intero file se più corto). Usato come pre-filtro veloce nella ricerca duplicati.
    /// </summary>
    public static async Task<string> ComputeSha256Async(string path, long maxBytes, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long remaining = maxBytes;
        int read;
        while (remaining > 0
               && (read = await stream.ReadAsync(
                   buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct)) > 0)
        {
            sha256.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }
```

Creare `FileExplorer/Services/SizeFormatter.cs`:

```csharp
namespace FileExplorer.Services;

/// <summary>Formattazione di dimensioni in byte per la UI.</summary>
public static class SizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B"
    };
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~ChecksumServiceTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/ChecksumService.cs FileExplorer/Services/SizeFormatter.cs FileExplorer.Tests/ChecksumServiceTests.cs
git commit -m "feat(dedup): hash SHA-256 parziale e formattazione dimensioni"
```

### Task 11: DuplicateFinderService

**Modello:** opus

**Files:**
- Create: `FileExplorer/Services/DuplicateFinderService.cs`
- Test: `FileExplorer.Tests/DuplicateFinderServiceTests.cs`

**Interfaces:**
- Consumes: `ChecksumService.ComputeSha256Async(path, maxBytes, ct)` (Task 10).
- Produces:
  - `sealed record DuplicateGroup(long FileSize, string Sha256, IReadOnlyList<string> FilePaths)` — `FilePaths` ordinati, gruppi ordinati per spazio sprecato (`FileSize * (count-1)`) decrescente.
  - `readonly record struct DuplicateScanProgress(string Stage, int Processed, int Total)`.
  - `DuplicateFinderService.FindDuplicatesAsync(string rootPath, int maxDegreeOfParallelism, Action<DuplicateScanProgress>? onProgress, CancellationToken ct)` → `Task<IReadOnlyList<DuplicateGroup>>`. File a dimensione 0 e file illeggibili sono ignorati.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/DuplicateFinderServiceTests.cs`:

```csharp
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DuplicateFinderServiceTests : IDisposable
{
    private readonly string _root;

    public DuplicateFinderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task WriteAsync(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public async Task FindDuplicatesAsync_IdenticalFiles_GroupedTogether()
    {
        await WriteAsync("uno.txt", "stesso contenuto");
        await WriteAsync("sub/due.txt", "stesso contenuto");
        await WriteAsync("tre.txt", "contenuto differente!");

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.Contains(Path.Combine(_root, "uno.txt"), group.FilePaths);
        Assert.Contains(Path.Combine(_root, "sub", "due.txt"), group.FilePaths);
    }

    [Fact]
    public async Task FindDuplicatesAsync_SameSizeDifferentContent_NotGrouped()
    {
        await WriteAsync("a.txt", "AAAA");
        await WriteAsync("b.txt", "BBBB"); // stessa lunghezza, contenuto diverso

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_LargeFilesSamePrefixDifferentTail_ResolvedByFullHash()
    {
        // Prefisso identico oltre i 64 KB del pre-filtro, coda diversa:
        // il solo hash parziale li raggrupperebbe, l'hash completo li separa.
        string prefix = new string('x', 70 * 1024);
        await WriteAsync("big1.bin", prefix + "FINE-1");
        await WriteAsync("big2.bin", prefix + "FINE-2");

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsOrderedByWastedSpace()
    {
        await WriteAsync("small1.txt", "ab");
        await WriteAsync("small2.txt", "ab");
        await WriteAsync("large1.txt", new string('z', 5000));
        await WriteAsync("large2.txt", new string('z', 5000));

        var groups = await DuplicateFinderService.FindDuplicatesAsync(_root, 2, null, CancellationToken.None);

        Assert.Equal(2, groups.Count);
        Assert.Equal(5000, groups[0].FileSize); // il gruppo con più spreco viene prima
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~DuplicateFinderServiceTests"`
Expected: errore di compilazione — `DuplicateFinderService` non esiste.

- [ ] **Step 3: Implementare il servizio**

Creare `FileExplorer/Services/DuplicateFinderService.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Gruppo di file identici (stessa dimensione e stesso SHA-256).</summary>
public sealed record DuplicateGroup(long FileSize, string Sha256, IReadOnlyList<string> FilePaths);

/// <summary>Avanzamento della scansione duplicati, per fase.</summary>
public readonly record struct DuplicateScanProgress(string Stage, int Processed, int Total);

/// <summary>
/// Ricerca duplicati a cascata: raggruppamento per dimensione, poi hash parziale
/// (primi 64 KB) dei soli candidati, poi hash completo. Ogni fase scarta i gruppi
/// rimasti con un solo file, così l'hash completo tocca il minimo indispensabile.
/// </summary>
public static class DuplicateFinderService
{
    private const long PartialHashBytes = 64 * 1024;

    public static async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        string rootPath,
        int maxDegreeOfParallelism,
        Action<DuplicateScanProgress>? onProgress,
        CancellationToken ct)
    {
        // Fase 1: enumerazione e raggruppamento per dimensione (file vuoti e illeggibili esclusi).
        List<(string Path, long Length)> files = await Task.Run(() =>
            Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    try { return (Path: path, Length: new FileInfo(path).Length); }
                    catch (IOException) { return (Path: path, Length: -1L); }
                    catch (UnauthorizedAccessException) { return (Path: path, Length: -1L); }
                })
                .Where(file => file.Length > 0)
                .ToList(), ct);

        var partialCandidates = files
            .GroupBy(file => file.Length)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        // Fase 2: hash parziale dei candidati.
        var partialHashes = await HashAllAsync(
            partialCandidates, PartialHashBytes, maxDegreeOfParallelism,
            processed => onProgress?.Invoke(new DuplicateScanProgress("Hash parziale", processed, partialCandidates.Count)),
            ct);

        var partialGroups = partialHashes
            .GroupBy(file => (file.Length, file.Hash))
            .Where(group => group.Count() > 1)
            .ToList();

        // Fase 3: hash completo; per i file entro i 64 KB l'hash parziale è già completo.
        var results = new List<DuplicateGroup>();
        var fullCandidates = new List<(string Path, long Length)>();

        foreach (var group in partialGroups)
        {
            if (group.Key.Length <= PartialHashBytes)
                results.Add(new DuplicateGroup(
                    group.Key.Length,
                    group.Key.Hash,
                    group.Select(file => file.Path).OrderBy(p => p, StringComparer.Ordinal).ToList()));
            else
                fullCandidates.AddRange(group.Select(file => (file.Path, file.Length)));
        }

        var fullHashes = await HashAllAsync(
            fullCandidates, long.MaxValue, maxDegreeOfParallelism,
            processed => onProgress?.Invoke(new DuplicateScanProgress("Hash completo", processed, fullCandidates.Count)),
            ct);

        results.AddRange(fullHashes
            .GroupBy(file => (file.Length, file.Hash))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.Key.Length,
                group.Key.Hash,
                group.Select(file => file.Path).OrderBy(p => p, StringComparer.Ordinal).ToList())));

        return results
            .OrderByDescending(group => group.FileSize * (group.FilePaths.Count - 1))
            .ToList();
    }

    private static async Task<List<(string Path, long Length, string Hash)>> HashAllAsync(
        List<(string Path, long Length)> files,
        long maxBytes,
        int maxDegreeOfParallelism,
        Action<int>? onProcessed,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<(string Path, long Length, string Hash)>();
        int processed = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                string hash = await ChecksumService.ComputeSha256Async(file.Path, maxBytes, ct);
                results.Add((file.Path, file.Length, hash));
            }
            catch (IOException) { /* file sparito o bloccato: escluso dai risultati */ }
            catch (UnauthorizedAccessException) { /* idem */ }
            finally
            {
                semaphore.Release();
                onProcessed?.Invoke(Interlocked.Increment(ref processed));
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~DuplicateFinderServiceTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/DuplicateFinderService.cs FileExplorer.Tests/DuplicateFinderServiceTests.cs
git commit -m "feat(dedup): ricerca duplicati a cascata dimensione/hash parziale/hash completo"
```

### Task 12: DuplicatesViewModel (+ helper dialog condiviso)

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/SelectPathDialogHelper.cs`
- Create: `FileExplorer/ViewModels/DuplicatesViewModel.cs`
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs` (usa l'helper, elimina il metodo privato duplicato)
- Test: `FileExplorer.Tests/DuplicatesViewModelTests.cs`

**Interfaces:**
- Consumes: `DuplicateFinderService.FindDuplicatesAsync` (Task 11), `SizeFormatter.Format` (Task 10).
- Produces:
  - `SelectPathDialogHelper.ShowAsync(bool directoriesOnly, string? currentPath)` → `Task<string?>` (statico, internal): il corpo è l'attuale `CopyPairsViewModel.ShowSelectPathDialogAsync`.
  - `DuplicateFileViewModel`: ctor `(DuplicateGroupViewModel group, string filePath)`; proprietà `Group`, `FilePath`, `Name`, `Directory`.
  - `DuplicateGroupViewModel`: ctor `(DuplicateGroup group)`; proprietà `long FileSize`, `ObservableCollection<DuplicateFileViewModel> Files`, `string Header` (aggiornata al variare di `Files`).
  - `DuplicatesViewModel`: proprietà `RootPath` (string?), `IsScanning` (bool), `StatusText` (string), `HasGroups` (bool), `Groups` (`ObservableCollection<DuplicateGroupViewModel>`); comandi `BrowseRootCommand`, `ScanCommand`, `CancelScanCommand`, `DeleteFileCommand` (`ReactiveCommand<DuplicateFileViewModel, Unit>`), `KeepFirstCommand` (`ReactiveCommand<DuplicateGroupViewModel, Unit>`); metodi pubblici awaitabili nei test: `Task ScanAsync()`, `Task DeleteFileAsync(DuplicateFileViewModel file)`, `Task KeepFirstAsync(DuplicateGroupViewModel group)`.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/DuplicatesViewModelTests.cs`:

```csharp
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class DuplicatesViewModelTests : IDisposable
{
    private readonly string _root;

    public DuplicatesViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-dupvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ScanAsync_FindsGroups_AndUpdatesStatus()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "x1.txt"), "doppio");
        await File.WriteAllTextAsync(Path.Combine(_root, "x2.txt"), "doppio");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();

        var group = Assert.Single(vm.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.False(vm.IsScanning);
        Assert.Equal("1 gruppi di duplicati", vm.StatusText);
    }

    [Fact]
    public async Task DeleteFileAsync_RemovesFileFromDiskAndDissolvesPair()
    {
        string keep = Path.Combine(_root, "k.txt");
        string remove = Path.Combine(_root, "r.txt");
        await File.WriteAllTextAsync(keep, "doppio");
        await File.WriteAllTextAsync(remove, "doppio");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();
        var target = Assert.Single(vm.Groups).Files.First(f => f.FilePath == remove);

        await vm.DeleteFileAsync(target);

        Assert.False(File.Exists(remove));
        Assert.True(File.Exists(keep));
        Assert.Empty(vm.Groups); // rimasto un solo file: gruppo dissolto
    }

    [Fact]
    public async Task KeepFirstAsync_DeletesAllButFirst()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "triplo");
        await File.WriteAllTextAsync(Path.Combine(_root, "b.txt"), "triplo");
        await File.WriteAllTextAsync(Path.Combine(_root, "c.txt"), "triplo");

        var vm = new DuplicatesViewModel { RootPath = _root };
        await vm.ScanAsync();
        var group = Assert.Single(vm.Groups);
        string first = group.Files[0].FilePath;

        await vm.KeepFirstAsync(group);

        Assert.True(File.Exists(first));
        Assert.Single(Directory.GetFiles(_root)); // sopravvive solo il primo
        Assert.Empty(vm.Groups);
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~DuplicatesViewModelTests"`
Expected: errore di compilazione — `DuplicatesViewModel` non esiste.

- [ ] **Step 3: Estrarre l'helper dialog**

Creare `FileExplorer/ViewModels/SelectPathDialogHelper.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using FileExplorer.Views;

namespace FileExplorer.ViewModels;

/// <summary>Apertura del dialog di selezione percorso, condivisa tra le schede.</summary>
internal static class SelectPathDialogHelper
{
    public static async Task<string?> ShowAsync(bool directoriesOnly, string? currentPath)
    {
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly,
                currentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        };

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null;

        return await dialog.ShowDialog<string?>(owner);
    }
}
```

In `FileExplorer/ViewModels/CopyPairsViewModel.cs`: eliminare il metodo privato `ShowSelectPathDialogAsync` (righe 63-76) e sostituire le sue chiamate con `SelectPathDialogHelper.ShowAsync(...)` (in `BrowseSourceAsync`, `BrowseDestinationAsync`, `AddExtraDestinationAsync`). Rimuovere gli using rimasti orfani (`Avalonia.Controls.ApplicationLifetimes`, `FileExplorer.Views`) se il compilatore li segnala inutilizzati.

- [ ] **Step 4: Implementare i ViewModel**

Creare `FileExplorer/ViewModels/DuplicatesViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Riga file dentro un gruppo di duplicati.</summary>
public class DuplicateFileViewModel
{
    public DuplicateFileViewModel(DuplicateGroupViewModel group, string filePath)
    {
        Group = group;
        FilePath = filePath;
    }

    public DuplicateGroupViewModel Group { get; }
    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public string Directory => Path.GetDirectoryName(FilePath) ?? "";
}

/// <summary>Gruppo di file identici, con intestazione riepilogativa.</summary>
public class DuplicateGroupViewModel : ReactiveObject
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        FileSize = group.FileSize;
        foreach (var path in group.FilePaths)
            Files.Add(new DuplicateFileViewModel(this, path));

        Files.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Header));
    }

    public long FileSize { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; } = new();

    public string Header =>
        $"{Files.Count} copie · {SizeFormatter.Format(FileSize)} l'una · spreco {SizeFormatter.Format(FileSize * Math.Max(0, Files.Count - 1))}";
}

/// <summary>
/// Scheda "Duplicati": scansione di una cartella alla ricerca di file identici,
/// con eliminazione per singolo file o per gruppo ("tieni solo il primo").
/// </summary>
public class DuplicatesViewModel : ViewModelBase
{
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    public bool HasGroups => Groups.Count > 0;

    private string? _rootPath;
    public string? RootPath
    {
        get => _rootPath;
        set => this.RaiseAndSetIfChanged(ref _rootPath, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    private string _statusText = "Pronto";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public ReactiveCommand<Unit, Unit> BrowseRootCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelScanCommand { get; }
    public ReactiveCommand<DuplicateFileViewModel, Unit> DeleteFileCommand { get; }
    public ReactiveCommand<DuplicateGroupViewModel, Unit> KeepFirstCommand { get; }

    public DuplicatesViewModel()
    {
        Groups.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasGroups));

        BrowseRootCommand = ReactiveCommand.CreateFromTask(BrowseRootAsync);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        CancelScanCommand = ReactiveCommand.Create(() => { _scanCts?.Cancel(); });
        DeleteFileCommand = ReactiveCommand.CreateFromTask<DuplicateFileViewModel>(DeleteFileAsync);
        KeepFirstCommand = ReactiveCommand.CreateFromTask<DuplicateGroupViewModel>(KeepFirstAsync);
    }

    private async Task BrowseRootAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, RootPath);
        if (!string.IsNullOrEmpty(selected))
            RootPath = selected;
    }

    public async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            StatusText = "Selezionare una cartella valida";
            return;
        }

        _scanCts = new CancellationTokenSource();
        Groups.Clear();
        IsScanning = true;
        StatusText = "Analisi…";

        try
        {
            var found = await DuplicateFinderService.FindDuplicatesAsync(
                RootPath,
                Math.Max(2, Environment.ProcessorCount - 1),
                progress => StatusText = $"{progress.Stage}: {progress.Processed}/{progress.Total}",
                _scanCts.Token);

            foreach (var group in found)
                Groups.Add(new DuplicateGroupViewModel(group));

            StatusText = found.Count == 0 ? "Nessun duplicato trovato" : $"{found.Count} gruppi di duplicati";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Annullato";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    public async Task DeleteFileAsync(DuplicateFileViewModel file)
    {
        try
        {
            await Task.Run(() => File.Delete(file.FilePath));
        }
        catch (Exception ex)
        {
            StatusText = $"Errore eliminazione: {ex.Message}";
            return;
        }

        file.Group.Files.Remove(file);
        if (file.Group.Files.Count < 2)
            Groups.Remove(file.Group);
    }

    public async Task KeepFirstAsync(DuplicateGroupViewModel group)
    {
        foreach (var file in group.Files.Skip(1).ToList())
            await DeleteFileAsync(file);
    }
}
```

- [ ] **Step 5: Eseguire tutti i test**

Run: `dotnet test`
Expected: tutti PASS (inclusi i CopyPairsViewModelTests dopo il refactoring dell'helper).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/ViewModels/SelectPathDialogHelper.cs FileExplorer/ViewModels/DuplicatesViewModel.cs FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer.Tests/DuplicatesViewModelTests.cs
git commit -m "feat(dedup): DuplicatesViewModel con scansione, eliminazione e helper dialog condiviso"
```

### Task 13: DuplicatesView + tab in MainWindow

**Modello:** haiku

**Files:**
- Create: `FileExplorer/Views/DuplicatesView.axaml`
- Create: `FileExplorer/Views/DuplicatesView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml` (nuova TabItem prima di "Impostazioni", riga 30)

**Interfaces:**
- Consumes: `DuplicatesViewModel` (Task 12) e tutte le sue proprietà/comandi.

- [ ] **Step 1: Creare la vista**

`FileExplorer/Views/DuplicatesView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             x:Class="FileExplorer.Views.DuplicatesView">

  <DockPanel>

    <!-- Header con gradiente -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <i:Icon Value="fa-solid fa-clone" FontSize="20" Foreground="{DynamicResource Brush.OnAccent}" />
        <TextBlock Text="Ricerca duplicati" FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
      </StackPanel>
    </Border>

    <!-- Barra comandi -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Surface}" Padding="20,12">
      <Grid ColumnDefinitions="*,Auto,Auto,Auto">
        <TextBox Grid.Column="0" Text="{Binding RootPath}" IsReadOnly="True"
                 Watermark="Cartella da analizzare…" />
        <Button Grid.Column="1" Classes="iconbtn" Margin="8,0,0,0"
                i:Attached.Icon="fa-solid fa-magnifying-glass"
                Command="{Binding BrowseRootCommand}" />
        <Button Grid.Column="2" Classes="primary" Content="Analizza" Margin="8,0,0,0"
                Command="{Binding ScanCommand}" IsEnabled="{Binding !IsScanning}" />
        <Button Grid.Column="3" Classes="secondary" Content="Annulla" Margin="8,0,0,0"
                Command="{Binding CancelScanCommand}" IsEnabled="{Binding IsScanning}" />
      </Grid>
    </Border>

    <!-- Stato -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Surface}" Padding="20,0,20,8">
      <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextMuted}" />
    </Border>

    <!-- Gruppi -->
    <Panel Background="{DynamicResource Brush.Surface}">
      <StackPanel IsVisible="{Binding !HasGroups}" VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="12">
        <i:Icon Value="fa-regular fa-clone" FontSize="52" Foreground="{DynamicResource Brush.TextMuted}" HorizontalAlignment="Center" />
        <TextBlock Text="Nessun gruppo di duplicati"
                   FontSize="16" Foreground="{DynamicResource Brush.TextMuted}" HorizontalAlignment="Center" />
      </StackPanel>

      <ScrollViewer IsVisible="{Binding HasGroups}">
        <ItemsControl ItemsSource="{Binding Groups}" Margin="20,0,20,12">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Border Classes="card">
                <StackPanel Spacing="8">
                  <Grid ColumnDefinitions="*,Auto">
                    <TextBlock Grid.Column="0" Text="{Binding Header}" FontWeight="Bold"
                               Foreground="{DynamicResource Brush.TextPrimary}" VerticalAlignment="Center" />
                    <Button Grid.Column="1" Classes="secondary" Content="Tieni solo il primo"
                            Command="{Binding DataContext.KeepFirstCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                            CommandParameter="{Binding}" />
                  </Grid>
                  <ItemsControl ItemsSource="{Binding Files}">
                    <ItemsControl.ItemTemplate>
                      <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,2">
                          <i:Icon Grid.Column="0" Value="fa-regular fa-file" Width="26"
                                  Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
                          <StackPanel Grid.Column="1" Margin="8,0">
                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Brush.TextPrimary}" />
                            <TextBlock Text="{Binding Directory}" FontSize="11"
                                       Foreground="{DynamicResource Brush.TextMuted}" />
                          </StackPanel>
                          <Button Grid.Column="2" Classes="iconbtn"
                                  i:Attached.Icon="fa-solid fa-trash"
                                  Command="{Binding DataContext.DeleteFileCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                  CommandParameter="{Binding}" />
                        </Grid>
                      </DataTemplate>
                    </ItemsControl.ItemTemplate>
                  </ItemsControl>
                </StackPanel>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Panel>
  </DockPanel>

</UserControl>
```

`FileExplorer/Views/DuplicatesView.axaml.cs` (stesso pattern di `CopyPairsView`):

```csharp
using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView()
    {
        InitializeComponent();
        DataContext = new DuplicatesViewModel();
    }
}
```

- [ ] **Step 2: Aggiungere la tab**

In `FileExplorer/Views/MainWindow.axaml`, prima della TabItem "Impostazioni" (riga 30), inserire:

```xml
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-clone" />
          <TextBlock Text="Duplicati" />
        </StackPanel>
      </TabItem.Header>
      <views:DuplicatesView />
    </TabItem>
```

- [ ] **Step 3: Build, test e verifica manuale**

Run: `dotnet build FileExplorer.sln` → 0 errori.
Run: `dotnet test` → tutti PASS.
Run (facoltativo): `dotnet run --project FileExplorer.Desktop` → tab "Duplicati": selezione cartella, Analizza popola i gruppi, cestino elimina.

- [ ] **Step 4: Commit e PR di fase**

```bash
git add FileExplorer/Views/DuplicatesView.axaml FileExplorer/Views/DuplicatesView.axaml.cs FileExplorer/Views/MainWindow.axaml
git commit -m "feat(dedup): tab Duplicati con gruppi ed eliminazione"
git push -u origin feature/duplicate-finder
gh pr create --title "feat: ricerca duplicati (IDEE #13)" --body "$(cat <<'EOF'
## Summary
- DuplicateFinderService: cascata dimensione → hash parziale 64KB → hash completo
- ChecksumService: overload con maxBytes; SizeFormatter condiviso
- DuplicatesViewModel + tab Duplicati: scansione, gruppi ordinati per spreco, elimina singolo / tieni solo il primo
- SelectPathDialogHelper estratto e riusato da CopyPairsViewModel

## Test plan
- [ ] dotnet test (ChecksumServiceTests, DuplicateFinderServiceTests, DuplicatesViewModelTests)
- [ ] verifica manuale UI

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Fase 5 — Treemap occupazione disco (IDEE punto 15)

Nuova tab "Spazio disco": scansione ricorsiva delle dimensioni, layout "squarified treemap" (algoritmo puro e testabile) e controllo custom con drill-down.

### Task 14: DiskUsageService + DiskUsageNode

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Models/DiskUsageNode.cs`
- Create: `FileExplorer/Services/DiskUsageService.cs`
- Test: `FileExplorer.Tests/DiskUsageServiceTests.cs`

**Interfaces:**
- Produces:
  - `DiskUsageNode`: `string Name`, `string FullPath`, `long SizeBytes`, `bool IsDirectory`, `List<DiskUsageNode> Children`.
  - `DiskUsageService.BuildTreeAsync(string rootPath, Action<int>? onFilesScanned, CancellationToken ct)` → `Task<DiskUsageNode>`: dimensioni delle cartelle = somma ricorsiva; cartelle inaccessibili ignorate; il callback riceve il conteggio file ogni 256 file.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/DiskUsageServiceTests.cs`:

```csharp
using FileExplorer.Services;

namespace FileExplorer.Tests;

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
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~DiskUsageServiceTests"`
Expected: errore di compilazione — tipi inesistenti.

- [ ] **Step 3: Implementare**

Creare `FileExplorer/Models/DiskUsageNode.cs`:

```csharp
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Nodo dell'albero di occupazione disco (file o cartella con somma ricorsiva).</summary>
public class DiskUsageNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsDirectory { get; set; }
    public List<DiskUsageNode> Children { get; } = new();
}
```

Creare `FileExplorer/Services/DiskUsageService.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Costruzione dell'albero di occupazione disco: scansione ricorsiva con somma
/// delle dimensioni; le cartelle inaccessibili vengono ignorate.
/// </summary>
public static class DiskUsageService
{
    /// <summary>Contatore mutabile condiviso dalla ricorsione (evita ref nei metodi async).</summary>
    private sealed class ScanCounter
    {
        public int Files;
    }

    public static Task<DiskUsageNode> BuildTreeAsync(
        string rootPath,
        Action<int>? onFilesScanned,
        CancellationToken ct) =>
        Task.Run(() => BuildNode(new DirectoryInfo(rootPath), new ScanCounter(), onFilesScanned, ct), ct);

    private static DiskUsageNode BuildNode(
        DirectoryInfo directory,
        ScanCounter counter,
        Action<int>? onFilesScanned,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new DiskUsageNode
        {
            Name = directory.Name,
            FullPath = directory.FullName,
            IsDirectory = true
        };

        try
        {
            foreach (var subDirectory in directory.EnumerateDirectories())
            {
                var child = BuildNode(subDirectory, counter, onFilesScanned, ct);
                node.Children.Add(child);
                node.SizeBytes += child.SizeBytes;
            }

            foreach (var file in directory.EnumerateFiles())
            {
                node.Children.Add(new DiskUsageNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    SizeBytes = file.Length,
                    IsDirectory = false
                });
                node.SizeBytes += file.Length;

                counter.Files++;
                if (counter.Files % 256 == 0)
                    onFilesScanned?.Invoke(counter.Files);
            }
        }
        catch (UnauthorizedAccessException) { /* cartella non leggibile: esclusa */ }
        catch (IOException) { /* percorso irraggiungibile: escluso */ }

        return node;
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~DiskUsageServiceTests"`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Models/DiskUsageNode.cs FileExplorer/Services/DiskUsageService.cs FileExplorer.Tests/DiskUsageServiceTests.cs
git commit -m "feat(treemap): albero di occupazione disco con scansione ricorsiva"
```

### Task 15: TreemapLayout (squarified)

**Modello:** opus

**Files:**
- Create: `FileExplorer/Services/TreemapLayout.cs`
- Test: `FileExplorer.Tests/TreemapLayoutTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct TreemapRect(double X, double Y, double Width, double Height)` con proprietà `double Area => Width * Height`.
  - `TreemapLayout.Compute(IReadOnlyList<long> values, double x, double y, double width, double height)` → `IReadOnlyList<TreemapRect>`: il rettangolo i-esimo corrisponde al valore i-esimo; area proporzionale al valore; i valori vanno passati in ordine decrescente per un layout ottimale (non è un requisito di correttezza); valori ≤ 0 producono rettangoli vuoti (default `TreemapRect`).

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/TreemapLayoutTests.cs`:

```csharp
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class TreemapLayoutTests
{
    [Fact]
    public void Compute_AreasProportionalToValues()
    {
        var rects = TreemapLayout.Compute(new long[] { 3, 1 }, 0, 0, 100, 100);

        Assert.Equal(7500, rects[0].Area, precision: 6);
        Assert.Equal(2500, rects[1].Area, precision: 6);
    }

    [Fact]
    public void Compute_AllRectsInsideBounds_AndTotalAreaMatches()
    {
        var values = new long[] { 500, 300, 200, 100, 50, 25 };
        var rects = TreemapLayout.Compute(values, 10, 20, 400, 300);

        double totalArea = 0;
        foreach (var rect in rects)
        {
            Assert.True(rect.X >= 10 - 1e-6 && rect.Y >= 20 - 1e-6);
            Assert.True(rect.X + rect.Width <= 410 + 1e-6);
            Assert.True(rect.Y + rect.Height <= 320 + 1e-6);
            totalArea += rect.Area;
        }

        Assert.Equal(400 * 300, totalArea, precision: 4);
    }

    [Fact]
    public void Compute_RectsDoNotOverlap()
    {
        var rects = TreemapLayout.Compute(new long[] { 40, 30, 20, 10 }, 0, 0, 200, 100);

        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
        {
            double overlapWidth = Math.Min(rects[i].X + rects[i].Width, rects[j].X + rects[j].Width)
                                  - Math.Max(rects[i].X, rects[j].X);
            double overlapHeight = Math.Min(rects[i].Y + rects[i].Height, rects[j].Y + rects[j].Height)
                                   - Math.Max(rects[i].Y, rects[j].Y);
            double overlapArea = Math.Max(0, overlapWidth) * Math.Max(0, overlapHeight);
            Assert.Equal(0, overlapArea, precision: 4);
        }
    }

    [Fact]
    public void Compute_EmptyAndZeroValues_HandledGracefully()
    {
        Assert.Empty(TreemapLayout.Compute(Array.Empty<long>(), 0, 0, 100, 100));

        var rects = TreemapLayout.Compute(new long[] { 0, 10, 0 }, 0, 0, 100, 100);
        Assert.Equal(default, rects[0]);
        Assert.Equal(default, rects[2]);
        Assert.Equal(10000, rects[1].Area, precision: 4);
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~TreemapLayoutTests"`
Expected: errore di compilazione — `TreemapLayout` non esiste.

- [ ] **Step 3: Implementare l'algoritmo**

Creare `FileExplorer/Services/TreemapLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorer.Services;

/// <summary>Rettangolo di layout della treemap, in coordinate assolute.</summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
}

/// <summary>
/// Layout "squarified treemap" (Bruls, Huizing, van Wijk 2000): dispone aree
/// proporzionali ai valori dentro un rettangolo, tenendo i tasselli il più
/// possibile vicini al quadrato. Puro e senza dipendenze UI: testabile in isolamento.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapRect> Compute(
        IReadOnlyList<long> values, double x, double y, double width, double height)
    {
        var result = new TreemapRect[values.Count];
        double total = values.Where(v => v > 0).Sum(v => (double)v);
        if (values.Count == 0 || total <= 0 || width <= 0 || height <= 0)
            return result;

        // Fattore che trasforma un valore nella sua area in pixel quadri.
        double scale = width * height / total;

        // Rettangolo libero residuo.
        double freeX = x, freeY = y, freeWidth = width, freeHeight = height;

        // Riga corrente: indici dei valori e statistiche delle loro aree.
        var row = new List<int>();
        var rowAreas = new List<double>();
        double rowArea = 0, rowMin = double.MaxValue, rowMax = 0;

        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0)
                continue;

            double itemArea = values[i] * scale;
            double side = Math.Min(freeWidth, freeHeight);

            bool startNewRow = row.Count > 0
                && WorstRatio(side, rowArea + itemArea, Math.Min(rowMin, itemArea), Math.Max(rowMax, itemArea))
                   > WorstRatio(side, rowArea, rowMin, rowMax);

            if (startNewRow)
            {
                LayoutRow(result, row, rowAreas, rowArea, ref freeX, ref freeY, ref freeWidth, ref freeHeight);
                row.Clear();
                rowAreas.Clear();
                rowArea = 0;
                rowMin = double.MaxValue;
                rowMax = 0;
            }

            row.Add(i);
            rowAreas.Add(itemArea);
            rowArea += itemArea;
            rowMin = Math.Min(rowMin, itemArea);
            rowMax = Math.Max(rowMax, itemArea);
        }

        if (row.Count > 0)
            LayoutRow(result, row, rowAreas, rowArea, ref freeX, ref freeY, ref freeWidth, ref freeHeight);

        return result;
    }

    /// <summary>
    /// Aspect ratio peggiore tra i tasselli di una riga di area <paramref name="rowArea"/>
    /// disposta lungo un lato di lunghezza <paramref name="side"/>.
    /// </summary>
    private static double WorstRatio(double side, double rowArea, double minArea, double maxArea)
    {
        double side2 = side * side;
        double area2 = rowArea * rowArea;
        return Math.Max(side2 * maxArea / area2, area2 / (side2 * minArea));
    }

    /// <summary>
    /// Dispone la riga corrente come striscia lungo il lato corto del rettangolo
    /// libero e riduce il rettangolo libero di conseguenza.
    /// </summary>
    private static void LayoutRow(
        TreemapRect[] result,
        List<int> row,
        List<double> rowAreas,
        double rowArea,
        ref double freeX,
        ref double freeY,
        ref double freeWidth,
        ref double freeHeight)
    {
        if (freeWidth >= freeHeight)
        {
            // Striscia verticale sul bordo sinistro.
            double stripWidth = rowArea / freeHeight;
            double currentY = freeY;
            for (int k = 0; k < row.Count; k++)
            {
                double itemHeight = rowAreas[k] / stripWidth;
                result[row[k]] = new TreemapRect(freeX, currentY, stripWidth, itemHeight);
                currentY += itemHeight;
            }

            freeX += stripWidth;
            freeWidth -= stripWidth;
        }
        else
        {
            // Striscia orizzontale sul bordo superiore.
            double stripHeight = rowArea / freeWidth;
            double currentX = freeX;
            for (int k = 0; k < row.Count; k++)
            {
                double itemWidth = rowAreas[k] / stripHeight;
                result[row[k]] = new TreemapRect(currentX, freeY, itemWidth, stripHeight);
                currentX += itemWidth;
            }

            freeY += stripHeight;
            freeHeight -= stripHeight;
        }
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~TreemapLayoutTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/TreemapLayout.cs FileExplorer.Tests/TreemapLayoutTests.cs
git commit -m "feat(treemap): layout squarified puro e testato"
```

### Task 16: DiskUsageViewModel

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/DiskUsageViewModel.cs`
- Test: `FileExplorer.Tests/DiskUsageViewModelTests.cs`

**Interfaces:**
- Consumes: `DiskUsageService.BuildTreeAsync` (Task 14), `DiskUsageNode` (Task 14), `SizeFormatter.Format` (Task 10), `SelectPathDialogHelper.ShowAsync` (Task 12).
- Produces: `DiskUsageViewModel` con proprietà `RootPath` (string?), `IsScanning` (bool), `StatusText` (string), `CurrentNode` (`DiskUsageNode?`), `CurrentPathText` (string), `CanNavigateUp` (bool); comandi `BrowseRootCommand`, `ScanCommand`, `CancelScanCommand`, `NavigateUpCommand`; metodi pubblici `Task ScanAsync()`, `void DrillDown(DiskUsageNode node)`, `void NavigateUp()`.

- [ ] **Step 1: Scrivere i test che falliscono**

Creare `FileExplorer.Tests/DiskUsageViewModelTests.cs`:

```csharp
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class DiskUsageViewModelTests : IDisposable
{
    private readonly string _root;

    public DiskUsageViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-usagevm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ScanAsync_PopulatesCurrentNode()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "f.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();

        Assert.NotNull(vm.CurrentNode);
        Assert.Equal(_root, vm.CurrentNode!.FullPath);
        Assert.False(vm.IsScanning);
        Assert.False(vm.CanNavigateUp);
    }

    [Fact]
    public async Task DrillDownAndNavigateUp_MoveThroughTheTree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "sub", "inner.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();
        var sub = vm.CurrentNode!.Children.Single(c => c.IsDirectory);

        vm.DrillDown(sub);
        Assert.Equal(sub, vm.CurrentNode);
        Assert.True(vm.CanNavigateUp);

        vm.NavigateUp();
        Assert.Equal(_root, vm.CurrentNode!.FullPath);
        Assert.False(vm.CanNavigateUp);
    }

    [Fact]
    public async Task DrillDown_OnFileNode_DoesNothing()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "f.bin"), new byte[10]);

        var vm = new DiskUsageViewModel { RootPath = _root };
        await vm.ScanAsync();
        var fileNode = vm.CurrentNode!.Children.Single();

        vm.DrillDown(fileNode);

        Assert.Equal(_root, vm.CurrentNode!.FullPath);
    }
}
```

- [ ] **Step 2: Eseguire i test e verificarne il fallimento**

Run: `dotnet test --filter "FullyQualifiedName~DiskUsageViewModelTests"`
Expected: errore di compilazione — `DiskUsageViewModel` non esiste.

- [ ] **Step 3: Implementare il ViewModel**

Creare `FileExplorer/ViewModels/DiskUsageViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Spazio disco": scansione di una cartella e navigazione della treemap
/// (drill-down nei nodi cartella, risalita lungo la catena visitata).
/// </summary>
public class DiskUsageViewModel : ViewModelBase
{
    private readonly List<DiskUsageNode> _breadcrumb = new();
    private CancellationTokenSource? _scanCts;

    private string? _rootPath;
    public string? RootPath
    {
        get => _rootPath;
        set => this.RaiseAndSetIfChanged(ref _rootPath, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    private string _statusText = "Pronto";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private DiskUsageNode? _currentNode;
    public DiskUsageNode? CurrentNode
    {
        get => _currentNode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentNode, value);
            this.RaisePropertyChanged(nameof(CurrentPathText));
            this.RaisePropertyChanged(nameof(CanNavigateUp));
        }
    }

    public string CurrentPathText => _currentNode is null
        ? ""
        : $"{_currentNode.FullPath} — {SizeFormatter.Format(_currentNode.SizeBytes)}";

    public bool CanNavigateUp => _breadcrumb.Count > 0;

    public ReactiveCommand<Unit, Unit> BrowseRootCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelScanCommand { get; }
    public ReactiveCommand<Unit, Unit> NavigateUpCommand { get; }

    public DiskUsageViewModel()
    {
        BrowseRootCommand = ReactiveCommand.CreateFromTask(BrowseRootAsync);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        CancelScanCommand = ReactiveCommand.Create(() => { _scanCts?.Cancel(); });
        NavigateUpCommand = ReactiveCommand.Create(NavigateUp);
    }

    private async Task BrowseRootAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, RootPath);
        if (!string.IsNullOrEmpty(selected))
            RootPath = selected;
    }

    public async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            StatusText = "Selezionare una cartella valida";
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "Analisi…";
        _breadcrumb.Clear();
        CurrentNode = null;

        try
        {
            var root = await DiskUsageService.BuildTreeAsync(
                RootPath,
                scanned => StatusText = $"Analisi… {scanned} file",
                _scanCts.Token);

            CurrentNode = root;
            StatusText = $"Totale: {SizeFormatter.Format(root.SizeBytes)}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Annullato";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>Entra in un nodo cartella (no-op su file, cartelle vuote o senza scansione).</summary>
    public void DrillDown(DiskUsageNode node)
    {
        if (CurrentNode is null || !node.IsDirectory || node.Children.Count == 0)
            return;

        _breadcrumb.Add(CurrentNode);
        CurrentNode = node;
    }

    /// <summary>Risale al nodo precedente della catena visitata.</summary>
    public void NavigateUp()
    {
        if (_breadcrumb.Count == 0)
            return;

        CurrentNode = _breadcrumb[^1];
        _breadcrumb.RemoveAt(_breadcrumb.Count - 1);
    }
}
```

- [ ] **Step 4: Eseguire i test e verificarne il successo**

Run: `dotnet test --filter "FullyQualifiedName~DiskUsageViewModelTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/DiskUsageViewModel.cs FileExplorer.Tests/DiskUsageViewModelTests.cs
git commit -m "feat(treemap): DiskUsageViewModel con scansione e navigazione drill-down"
```

### Task 17: TreemapControl + brush di palette

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Views/TreemapControl.cs`
- Modify: `FileExplorer/Styles/Palette.axaml` (6 brush per variante)

**Interfaces:**
- Consumes: `TreemapLayout.Compute` (Task 15), `DiskUsageNode` (Task 14), `SizeFormatter.Format` (Task 10), brush `Brush.Treemap.1`…`Brush.Treemap.6` (questo task).
- Produces: `TreemapControl : Canvas` con `StyledProperty<DiskUsageNode?> NodeProperty` (proprietà CLR `Node`) ed evento `event Action<DiskUsageNode>? NodeActivated` (click su un tassello). Ricostruisce i tasselli al cambio di `Node` o delle dimensioni.

- [ ] **Step 1: Aggiungere i brush alla palette**

In `FileExplorer/Styles/Palette.axaml`, nel dizionario `Light` (dopo `Brush.NeutralFg`, riga 29):

```xml
      <SolidColorBrush x:Key="Brush.Treemap.1" Color="#F2C4B3" />
      <SolidColorBrush x:Key="Brush.Treemap.2" Color="#F5D8A7" />
      <SolidColorBrush x:Key="Brush.Treemap.3" Color="#C9DEC4" />
      <SolidColorBrush x:Key="Brush.Treemap.4" Color="#BCD5E3" />
      <SolidColorBrush x:Key="Brush.Treemap.5" Color="#D9C6E0" />
      <SolidColorBrush x:Key="Brush.Treemap.6" Color="#E3CFC0" />
```

nel dizionario `Dark` (dopo `Brush.NeutralFg`, riga 47):

```xml
      <SolidColorBrush x:Key="Brush.Treemap.1" Color="#7A4A3C" />
      <SolidColorBrush x:Key="Brush.Treemap.2" Color="#7A6236" />
      <SolidColorBrush x:Key="Brush.Treemap.3" Color="#46603F" />
      <SolidColorBrush x:Key="Brush.Treemap.4" Color="#3B586B" />
      <SolidColorBrush x:Key="Brush.Treemap.5" Color="#5C4668" />
      <SolidColorBrush x:Key="Brush.Treemap.6" Color="#6B5546" />
```

- [ ] **Step 2: Implementare il controllo**

Creare `FileExplorer/Views/TreemapControl.cs`:

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Views;

/// <summary>
/// Treemap dei figli di un <see cref="DiskUsageNode"/>: un Border per tassello
/// (tooltip nativo, click per drill-down), layout squarified ricalcolato al
/// cambio di nodo o di dimensioni.
/// </summary>
public class TreemapControl : Canvas
{
    public static readonly StyledProperty<DiskUsageNode?> NodeProperty =
        AvaloniaProperty.Register<TreemapControl, DiskUsageNode?>(nameof(Node));

    public DiskUsageNode? Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    /// <summary>Scatta al click su un tassello (la vista lo inoltra al ViewModel).</summary>
    public event Action<DiskUsageNode>? NodeActivated;

    public TreemapControl()
    {
        SizeChanged += (_, _) => Rebuild();
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodeProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();

        if (Node is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var nodes = Node.Children
            .Where(child => child.SizeBytes > 0)
            .OrderByDescending(child => child.SizeBytes)
            .ToList();
        if (nodes.Count == 0)
            return;

        var rects = TreemapLayout.Compute(
            nodes.Select(child => child.SizeBytes).ToList(),
            0, 0, Bounds.Width, Bounds.Height);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var rect = rects[i];
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            var border = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                Background = FindTreemapBrush(i),
                BorderBrush = this.FindResource(ActualThemeVariant, "Brush.CardBorder") as IBrush,
                BorderThickness = new Thickness(1)
            };

            if (rect.Width >= 60 && rect.Height >= 24)
            {
                border.Child = new TextBlock
                {
                    Text = node.Name,
                    FontSize = 11,
                    Foreground = this.FindResource(ActualThemeVariant, "Brush.TextPrimary") as IBrush,
                    Margin = new Thickness(4, 2),
                    VerticalAlignment = VerticalAlignment.Top,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
            }

            ToolTip.SetTip(border, $"{node.Name} — {SizeFormatter.Format(node.SizeBytes)}");
            border.PointerPressed += (_, _) => NodeActivated?.Invoke(node);

            SetLeft(border, rect.X);
            SetTop(border, rect.Y);
            Children.Add(border);
        }
    }

    private IBrush? FindTreemapBrush(int index) =>
        this.FindResource(ActualThemeVariant, $"Brush.Treemap.{index % 6 + 1}") as IBrush;
}
```

Nota per l'esecutore: `FindResource(ThemeVariant, object key)` è l'extension in `Avalonia.Controls.ResourceNodeExtensions`; se la firma con `ThemeVariant` non fosse disponibile nella versione di Avalonia in uso, ripiegare su `this.TryFindResource(key, ActualThemeVariant, out var value)` e castare `value`.

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln` → 0 errori.

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Views/TreemapControl.cs FileExplorer/Styles/Palette.axaml
git commit -m "feat(treemap): controllo treemap con tasselli cliccabili e palette dedicata"
```

### Task 18: DiskUsageView + tab in MainWindow

**Modello:** haiku

**Files:**
- Create: `FileExplorer/Views/DiskUsageView.axaml`
- Create: `FileExplorer/Views/DiskUsageView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml` (nuova TabItem prima di "Impostazioni")

**Interfaces:**
- Consumes: `DiskUsageViewModel` (Task 16), `TreemapControl` (Task 17).

- [ ] **Step 1: Creare la vista**

`FileExplorer/Views/DiskUsageView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:views="clr-namespace:FileExplorer.Views"
             x:Class="FileExplorer.Views.DiskUsageView">

  <DockPanel>

    <!-- Header con gradiente -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <i:Icon Value="fa-solid fa-chart-pie" FontSize="20" Foreground="{DynamicResource Brush.OnAccent}" />
        <TextBlock Text="Spazio disco" FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
      </StackPanel>
    </Border>

    <!-- Barra comandi -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Surface}" Padding="20,12">
      <Grid ColumnDefinitions="Auto,*,Auto,Auto,Auto">
        <Button Grid.Column="0" Classes="iconbtn" Margin="0,0,8,0"
                i:Attached.Icon="fa-solid fa-arrow-up"
                Command="{Binding NavigateUpCommand}"
                IsEnabled="{Binding CanNavigateUp}" />
        <TextBox Grid.Column="1" Text="{Binding RootPath}" IsReadOnly="True"
                 Watermark="Cartella da analizzare…" />
        <Button Grid.Column="2" Classes="iconbtn" Margin="8,0,0,0"
                i:Attached.Icon="fa-solid fa-magnifying-glass"
                Command="{Binding BrowseRootCommand}" />
        <Button Grid.Column="3" Classes="primary" Content="Analizza" Margin="8,0,0,0"
                Command="{Binding ScanCommand}" IsEnabled="{Binding !IsScanning}" />
        <Button Grid.Column="4" Classes="secondary" Content="Annulla" Margin="8,0,0,0"
                Command="{Binding CancelScanCommand}" IsEnabled="{Binding IsScanning}" />
      </Grid>
    </Border>

    <!-- Stato e percorso corrente -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Surface}" Padding="20,0,20,8">
      <StackPanel Spacing="2">
        <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextMuted}" />
        <TextBlock Text="{Binding CurrentPathText}" FontSize="11"
                   Foreground="{DynamicResource Brush.TextMuted}" />
      </StackPanel>
    </Border>

    <!-- Treemap -->
    <Border Background="{DynamicResource Brush.Surface}" Padding="20,0,20,20">
      <views:TreemapControl x:Name="Treemap" Node="{Binding CurrentNode}" />
    </Border>
  </DockPanel>

</UserControl>
```

`FileExplorer/Views/DiskUsageView.axaml.cs`:

```csharp
using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class DiskUsageView : UserControl
{
    public DiskUsageView()
    {
        InitializeComponent();
        var viewModel = new DiskUsageViewModel();
        DataContext = viewModel;

        var treemap = this.FindControl<TreemapControl>("Treemap")!;
        treemap.NodeActivated += viewModel.DrillDown;
    }
}
```

- [ ] **Step 2: Aggiungere la tab**

In `FileExplorer/Views/MainWindow.axaml`, prima della TabItem "Impostazioni" (dopo "Duplicati", se la Fase 4 è già mergiata), inserire:

```xml
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-chart-pie" />
          <TextBlock Text="Spazio disco" />
        </StackPanel>
      </TabItem.Header>
      <views:DiskUsageView />
    </TabItem>
```

- [ ] **Step 3: Build, test e verifica manuale**

Run: `dotnet build FileExplorer.sln` → 0 errori.
Run: `dotnet test` → tutti PASS.
Run (facoltativo): `dotnet run --project FileExplorer.Desktop` → tab "Spazio disco": Analizza mostra la treemap, click su una cartella entra, freccia su risale, tooltip con nome e dimensione; verificare anche in tema Dark.

- [ ] **Step 4: Commit e PR di fase**

```bash
git add FileExplorer/Views/DiskUsageView.axaml FileExplorer/Views/DiskUsageView.axaml.cs FileExplorer/Views/MainWindow.axaml
git commit -m "feat(treemap): tab Spazio disco con treemap navigabile"
git push -u origin feature/disk-usage-treemap
gh pr create --title "feat: treemap occupazione disco (IDEE #15)" --body "$(cat <<'EOF'
## Summary
- DiskUsageService: albero dimensioni con scansione ricorsiva tollerante agli accessi negati
- TreemapLayout: algoritmo squarified puro con test di proporzionalità/contenimento/non-sovrapposizione
- TreemapControl: tasselli cliccabili theme-aware (palette Brush.Treemap.*)
- Tab "Spazio disco" con drill-down e risalita

## Test plan
- [ ] dotnet test (DiskUsageServiceTests, TreemapLayoutTests, DiskUsageViewModelTests)
- [ ] verifica manuale UI (tema chiaro e scuro)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Aggiornamento IDEE.md

A fine lavori (o a ogni PR mergiata), marcare in `IDEE.md` i punti completati: `[ ]` → `[x]` sulle voci 1, 2, 3, 13, 15.

