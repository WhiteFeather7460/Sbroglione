# Delta-copy stile rsync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Copiare solo i blocchi cambiati (rolling checksum, algoritmo rsync) quando il file di destinazione esiste già, invece di riscrivere l'intero file — IDEE.md punto 5, Fase 1 (solo copia locale/locale, FTP/SFTP fuori scope).

**Architecture:** Nuovo namespace `Sbroglione.Services` con 4 classi: `DeltaCopySignatureBuilder` (hash a blocchi della destinazione esistente), `DeltaCopyScanner` (rolling checksum sulla sorgente, produce istruzioni Copy/Literal), `DeltaCopyApplier` (applica le istruzioni scrivendo un file temporaneo poi rename atomico), `DeltaCopyService` (orchestratore con fallback a full-copy). Integrato come opt-in in `FileCopyService` dietro un nuovo parametro `deltaCopyEnabled`.

**Tech Stack:** .NET 8, `System.Security.Cryptography.SHA256`, `System.IO.FileStream`, xunit.

**Spec:** `docs/superpowers/specs/2026-09-09-delta-copy-design.md`

## Global Constraints

- Scope Fase 1: solo copia locale/locale (incl. volumi montati). Nessuna modifica a `IRemoteFileClient`/FTP/SFTP.
- Opt-in per copy pair: nuovo flag `DeltaCopyEnabled`, default `false` — nessun comportamento esistente deve cambiare quando è `false`.
- Block size fisso, configurabile: `AppSettings.DeltaBlockSizeKB`, default `128`.
- Algoritmo rsync a due livelli: weak rolling hash + SHA-256 forte per conferma.
- Verify post-copia (`DirectoryVerificationService`) resta invariato, gira sempre dopo indipendentemente dal delta.
- `CopyJobRecord`/journal: nessuna modifica di schema.
- Multi-destinazione: ogni destinazione fa la propria scansione indipendente della sorgente (nessun fan-out condiviso con delta attivo).
- Namespace: tutte le nuove classi in `Sbroglione.Services` (service statici, stesso stile di `FileCopyService`); modelli in `Sbroglione.Models`.

---

### Task 1: RollingChecksum (weak hash a due livelli)

**Model:** opus (matematica del rolling hash: un errore di segno/offset è subdolo e passa i test superficiali)

**Files:**
- Create: `Sbroglione/Services/RollingChecksum.cs`
- Test: `Sbroglione.Tests/RollingChecksumTests.cs`

**Interfaces:**
- Produces: `Sbroglione.Services.RollingChecksum` — classe mutabile con `void AddByte(byte value)`, `void Roll(byte outgoing, byte incoming)`, `void Reset()`, `uint Value { get; }`.

- [x] **Step 1: Scrivi i test che falliscono**

```csharp
// Sbroglione.Tests/RollingChecksumTests.cs
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class RollingChecksumTests
{
    [Fact]
    public void AddByte_SameBytes_ProducesSameValueAsIndependentComputation()
    {
        var a = new RollingChecksum();
        var b = new RollingChecksum();
        byte[] window = { 1, 2, 3, 4, 5 };

        foreach (var value in window) a.AddByte(value);
        foreach (var value in window) b.AddByte(value);

        Assert.Equal(a.Value, b.Value);
    }

    [Fact]
    public void AddByte_DifferentBytes_ProducesDifferentValue()
    {
        var a = new RollingChecksum();
        var b = new RollingChecksum();

        foreach (byte value in new byte[] { 1, 2, 3 }) a.AddByte(value);
        foreach (byte value in new byte[] { 1, 2, 4 }) b.AddByte(value);

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void Roll_MatchesRecomputationFromScratch()
    {
        // Finestra iniziale [1,2,3,4,5] fatta scorrere di uno -> [2,3,4,5,6].
        var rolling = new RollingChecksum();
        foreach (byte value in new byte[] { 1, 2, 3, 4, 5 }) rolling.AddByte(value);
        rolling.Roll(outgoing: 1, incoming: 6);

        var expected = new RollingChecksum();
        foreach (byte value in new byte[] { 2, 3, 4, 5, 6 }) expected.AddByte(value);

        Assert.Equal(expected.Value, rolling.Value);
    }

    [Fact]
    public void Roll_MultipleSlides_MatchesRecomputationEachStep()
    {
        byte[] data = { 10, 20, 30, 40, 50, 60, 70, 80 };
        const int windowSize = 3;

        var rolling = new RollingChecksum();
        for (int i = 0; i < windowSize; i++) rolling.AddByte(data[i]);

        for (int start = 1; start <= data.Length - windowSize; start++)
        {
            rolling.Roll(outgoing: data[start - 1], incoming: data[start + windowSize - 1]);

            var expected = new RollingChecksum();
            for (int i = start; i < start + windowSize; i++) expected.AddByte(data[i]);

            Assert.Equal(expected.Value, rolling.Value);
        }
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var rolling = new RollingChecksum();
        foreach (byte value in new byte[] { 1, 2, 3 }) rolling.AddByte(value);
        rolling.Reset();

        var empty = new RollingChecksum();
        Assert.Equal(empty.Value, rolling.Value);
    }
}
```

- [x] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test --filter RollingChecksumTests`
Expected: FAIL (compile error, `RollingChecksum` non esiste)

- [x] **Step 3: Implementa**

```csharp
// Sbroglione/Services/RollingChecksum.cs
namespace Sbroglione.Services;

/// <summary>
/// Weak checksum a due livelli per il rolling hash stile rsync: a = somma dei byte,
/// b = somma pesata per posizione (peso 1 al primo byte della finestra, crescente).
/// Non è il rolling checksum "ufficiale" di rsync (pesi in ordine inverso), ma è
/// auto-consistente: AddByte costruisce la finestra iniziale, Roll la fa scorrere
/// di un byte con aggiornamento O(1). Va bene per il nostro scopo (trovare
/// candidati locali, confermati poi da SHA-256): non deve essere bit-compatibile
/// con l'algoritmo originale.
/// </summary>
public sealed class RollingChecksum
{
    private const long Mod = 1 << 16;

    private long _a;
    private long _b;
    private int _length;

    /// <summary>Valore corrente: 16 bit bassi = a, 16 bit alti = b.</summary>
    public uint Value => (uint)((_b << 16) | _a);

    /// <summary>Aggiunge un byte in coda alla finestra (usato per costruirla dall'inizio).</summary>
    public void AddByte(byte value)
    {
        _length++;
        _a = (_a + value) % Mod;
        _b = (_b + (long)_length * value) % Mod;
    }

    /// <summary>
    /// Fa scorrere la finestra di un byte: rimuove <paramref name="outgoing"/> dall'inizio,
    /// aggiunge <paramref name="incoming"/> in coda. La lunghezza della finestra non cambia.
    /// </summary>
    public void Roll(byte outgoing, byte incoming)
    {
        long oldA = _a;
        _a = Mod2(_a - outgoing + incoming);
        _b = Mod2(_b - oldA + (long)_length * incoming);
    }

    public void Reset()
    {
        _a = 0;
        _b = 0;
        _length = 0;
    }

    private static long Mod2(long value)
    {
        long m = value % Mod;
        return m < 0 ? m + Mod : m;
    }
}
```

- [x] **Step 4: Esegui e verifica il successo**

Run: `dotnet test --filter RollingChecksumTests`
Expected: PASS (5/5)

- [x] **Step 5: Commit**

```bash
git add Sbroglione/Services/RollingChecksum.cs Sbroglione.Tests/RollingChecksumTests.cs
git commit -m "feat: add RollingChecksum for delta-copy weak hash"
```

---

### Task 2: Modelli istruzioni + DeltaCopySignatureBuilder

**Model:** sonnet (logica standard di I/O a blocchi + hashing, nessuna decisione algoritmica delicata)

**Files:**
- Create: `Sbroglione/Models/DeltaCopyInstruction.cs`
- Create: `Sbroglione/Services/DeltaCopySignatureBuilder.cs`
- Test: `Sbroglione.Tests/DeltaCopySignatureBuilderTests.cs`

**Interfaces:**
- Consumes: `RollingChecksum` (Task 1).
- Produces:
  - `Sbroglione.Models.DeltaCopyInstruction` (abstract record), `CopyBlockInstruction(long DestOffset, int Length)`, `LiteralInstruction(byte[] Data)`.
  - `Sbroglione.Models.SignatureBlock(long Offset, int Length, byte[] StrongHash)`.
  - `Sbroglione.Services.DeltaSignature` — `IReadOnlyDictionary<uint, List<SignatureBlock>> BlocksByWeak { get; }`.
  - `Sbroglione.Services.DeltaCopySignatureBuilder.BuildAsync(string destPath, int blockSizeBytes, CancellationToken ct) -> Task<DeltaSignature>`.

- [x] **Step 1: Scrivi i modelli**

```csharp
// Sbroglione/Models/DeltaCopyInstruction.cs
namespace Sbroglione.Models;

/// <summary>Istruzione prodotta da <see cref="Services.DeltaCopyScanner"/> per ricostruire il file.</summary>
public abstract record DeltaCopyInstruction;

/// <summary>Copia un blocco di <paramref name="Length"/> byte dal vecchio file di destinazione, a partire da <paramref name="DestOffset"/>.</summary>
public sealed record CopyBlockInstruction(long DestOffset, int Length) : DeltaCopyInstruction;

/// <summary>Scrive byte letti direttamente dalla sorgente (nessun blocco corrispondente trovato in destinazione).</summary>
public sealed record LiteralInstruction(byte[] Data) : DeltaCopyInstruction;

/// <summary>Un blocco della destinazione esistente indicizzato per la ricerca di corrispondenze.</summary>
public sealed record SignatureBlock(long Offset, int Length, byte[] StrongHash);
```

- [x] **Step 2: Scrivi il test che fallisce**

```csharp
// Sbroglione.Tests/DeltaCopySignatureBuilderTests.cs
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopySignatureBuilderTests : IDisposable
{
    private readonly string _root;

    public DeltaCopySignatureBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltasig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public async Task BuildAsync_ExactMultipleOfBlockSize_ProducesOneBlockPerChunk()
    {
        string path = Path.Combine(_root, "dest.bin");
        byte[] content = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(path, blockSizeBytes: 10, CancellationToken.None);

        int totalBlocks = signature.BlocksByWeak.Values.Sum(list => list.Count);
        Assert.Equal(3, totalBlocks);
        Assert.Contains(signature.BlocksByWeak.Values.SelectMany(v => v), b => b.Offset == 0 && b.Length == 10);
        Assert.Contains(signature.BlocksByWeak.Values.SelectMany(v => v), b => b.Offset == 20 && b.Length == 10);
    }

    [Fact]
    public async Task BuildAsync_LastBlockShorter_KeepsActualLength()
    {
        string path = Path.Combine(_root, "dest.bin");
        byte[] content = Enumerable.Range(0, 25).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(path, content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(path, blockSizeBytes: 10, CancellationToken.None);

        var lastBlock = signature.BlocksByWeak.Values.SelectMany(v => v).Single(b => b.Offset == 20);
        Assert.Equal(5, lastBlock.Length);
    }

    [Fact]
    public async Task BuildAsync_SameContentTwice_ProducesSameStrongHash()
    {
        string pathA = Path.Combine(_root, "a.bin");
        string pathB = Path.Combine(_root, "b.bin");
        byte[] content = Enumerable.Repeat((byte)7, 10).ToArray();
        await File.WriteAllBytesAsync(pathA, content);
        await File.WriteAllBytesAsync(pathB, content);

        var sigA = await DeltaCopySignatureBuilder.BuildAsync(pathA, blockSizeBytes: 10, CancellationToken.None);
        var sigB = await DeltaCopySignatureBuilder.BuildAsync(pathB, blockSizeBytes: 10, CancellationToken.None);

        var blockA = sigA.BlocksByWeak.Values.Single().Single();
        var blockB = sigB.BlocksByWeak.Values.Single().Single();
        Assert.Equal(blockA.StrongHash, blockB.StrongHash);
    }
}
```

- [x] **Step 3: Esegui e verifica il fallimento**

Run: `dotnet test --filter DeltaCopySignatureBuilderTests`
Expected: FAIL (compile error, `DeltaCopySignatureBuilder`/`DeltaSignature` non esistono)

- [x] **Step 4: Implementa**

```csharp
// Sbroglione/Services/DeltaCopySignatureBuilder.cs
using System.Security.Cryptography;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Signature della destinazione esistente: hash debole+forte per ciascun blocco fisso.</summary>
public sealed class DeltaSignature
{
    public DeltaSignature(IReadOnlyDictionary<uint, List<SignatureBlock>> blocksByWeak) => BlocksByWeak = blocksByWeak;

    public IReadOnlyDictionary<uint, List<SignatureBlock>> BlocksByWeak { get; }
}

/// <summary>Calcola la signature a blocchi di un file esistente, per il confronto delta-copy.</summary>
public static class DeltaCopySignatureBuilder
{
    public static async Task<DeltaSignature> BuildAsync(string destPath, int blockSizeBytes, CancellationToken ct)
    {
        var blocksByWeak = new Dictionary<uint, List<SignatureBlock>>();

        var stream = new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[blockSizeBytes];
            long offset = 0;
            int read;
            while ((read = await ReadFullAsync(stream, buffer, blockSizeBytes, ct).ConfigureAwait(false)) > 0)
            {
                var rolling = new RollingChecksum();
                for (int i = 0; i < read; i++)
                    rolling.AddByte(buffer[i]);

                byte[] strong = SHA256.HashData(buffer.AsSpan(0, read));

                if (!blocksByWeak.TryGetValue(rolling.Value, out var list))
                    blocksByWeak[rolling.Value] = list = new List<SignatureBlock>();
                list.Add(new SignatureBlock(offset, read, strong));

                offset += read;
            }
        }

        return new DeltaSignature(blocksByWeak);
    }

    internal static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
```

- [x] **Step 5: Esegui e verifica il successo**

Run: `dotnet test --filter DeltaCopySignatureBuilderTests`
Expected: PASS (3/3)

- [x] **Step 6: Commit**

```bash
git add Sbroglione/Models/DeltaCopyInstruction.cs Sbroglione/Services/DeltaCopySignatureBuilder.cs Sbroglione.Tests/DeltaCopySignatureBuilderTests.cs
git commit -m "feat: add delta-copy instruction models and signature builder"
```

---

### Task 3: DeltaCopyScanner (algoritmo rolling)

**Model:** opus (cuore dell'algoritmo rsync: gestione shift/EOF/match ha molti edge case, il più a rischio bug silenzioso di tutto il piano)

**Files:**
- Create: `Sbroglione/Services/DeltaCopyScanner.cs`
- Test: `Sbroglione.Tests/DeltaCopyScannerTests.cs`

**Interfaces:**
- Consumes: `RollingChecksum` (Task 1), `DeltaSignature`/`SignatureBlock` (Task 2), `DeltaCopySignatureBuilder.ReadFullAsync` (Task 2, `internal`, stesso assembly).
- Produces: `Sbroglione.Services.DeltaCopyScanner.ScanAsync(string sourcePath, DeltaSignature signature, int blockSizeBytes, CancellationToken ct) -> Task<IReadOnlyList<DeltaCopyInstruction>>`.

- [x] **Step 1: Scrivi i test che falliscono**

```csharp
// Sbroglione.Tests/DeltaCopyScannerTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyScannerTests : IDisposable
{
    private readonly string _root;

    public DeltaCopyScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltascan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private async Task<string> WriteAsync(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    [Fact]
    public async Task ScanAsync_IdenticalContent_ProducesOnlyCopyBlockInstructions()
    {
        byte[] content = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        string destPath = await WriteAsync("dest.bin", content);
        string sourcePath = await WriteAsync("source.bin", content);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        Assert.All(instructions, i => Assert.IsType<CopyBlockInstruction>(i));
        Assert.Equal(4, instructions.Count);
    }

    [Fact]
    public async Task ScanAsync_ByteInsertedAtStart_StillFindsShiftedBlocks()
    {
        byte[] original = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        byte[] shifted = new byte[] { 255 }.Concat(original).ToArray();
        string destPath = await WriteAsync("dest.bin", original);
        string sourcePath = await WriteAsync("source.bin", shifted);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        // Un byte letterale iniziale, poi i blocchi originali (shiftati di 1 nella sorgente
        // ma identici nel contenuto) devono essere trovati come CopyBlockInstruction.
        Assert.IsType<LiteralInstruction>(instructions[0]);
        Assert.Single((instructions[0] as LiteralInstruction)!.Data);
        Assert.Contains(instructions, i => i is CopyBlockInstruction);
    }

    [Fact]
    public async Task ScanAsync_MiddleBlockModified_ProducesLiteralForThatBlockOnly()
    {
        byte[] content = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        byte[] modified = (byte[])content.Clone();
        for (int i = 10; i < 20; i++) modified[i] = (byte)(255 - i);

        string destPath = await WriteAsync("dest.bin", content);
        string sourcePath = await WriteAsync("source.bin", modified);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        Assert.Equal(3, instructions.Count);
        Assert.IsType<CopyBlockInstruction>(instructions[0]);
        Assert.IsType<LiteralInstruction>(instructions[1]);
        Assert.Equal(10, ((LiteralInstruction)instructions[1]).Data.Length);
        Assert.IsType<CopyBlockInstruction>(instructions[2]);
    }

    [Fact]
    public async Task ScanAsync_SourceShorterThanDest_ReconstructsExactSourceContent()
    {
        byte[] destContent = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();
        byte[] sourceContent = destContent.Take(15).ToArray();

        string destPath = await WriteAsync("dest.bin", destContent);
        string sourcePath = await WriteAsync("source.bin", sourceContent);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        byte[] reconstructed = Reconstruct(instructions, destContent);
        Assert.Equal(sourceContent, reconstructed);
    }

    [Fact]
    public async Task ScanAsync_CompletelyDifferentContent_ProducesOnlyLiteralInstructions()
    {
        byte[] destContent = Enumerable.Repeat((byte)1, 20).ToArray();
        byte[] sourceContent = Enumerable.Repeat((byte)2, 20).ToArray();

        string destPath = await WriteAsync("dest.bin", destContent);
        string sourcePath = await WriteAsync("source.bin", sourceContent);

        var signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes: 10, CancellationToken.None);
        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes: 10, CancellationToken.None);

        byte[] reconstructed = Reconstruct(instructions, destContent);
        Assert.Equal(sourceContent, reconstructed);
    }

    private static byte[] Reconstruct(IReadOnlyList<DeltaCopyInstruction> instructions, byte[] oldDestContent)
    {
        using var output = new MemoryStream();
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case LiteralInstruction literal:
                    output.Write(literal.Data);
                    break;
                case CopyBlockInstruction block:
                    output.Write(oldDestContent, (int)block.DestOffset, block.Length);
                    break;
            }
        }
        return output.ToArray();
    }
}
```

- [x] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test --filter DeltaCopyScannerTests`
Expected: FAIL (compile error, `DeltaCopyScanner` non esiste)

- [x] **Step 3: Implementa**

```csharp
// Sbroglione/Services/DeltaCopyScanner.cs
using System.Security.Cryptography;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Scorre la sorgente con una finestra scorrevole di <c>blockSizeBytes</c> byte, cercando
/// corrispondenze contro la <see cref="DeltaSignature"/> della destinazione esistente.
/// Su match: emette <see cref="CopyBlockInstruction"/> e salta la finestra in avanti di un
/// blocco intero (lettura fresca, nessun rolling). Su mancata corrispondenza: il primo byte
/// della finestra diventa letterale e la finestra scorre di un byte (rolling O(1)) — questo
/// gestisce anche gli shift da inserimento/rimozione byte che romperebbero un confronto a
/// blocchi allineati.
/// </summary>
public static class DeltaCopyScanner
{
    public static async Task<IReadOnlyList<DeltaCopyInstruction>> ScanAsync(
        string sourcePath, DeltaSignature signature, int blockSizeBytes, CancellationToken ct)
    {
        var instructions = new List<DeltaCopyInstruction>();
        var literalBuffer = new List<byte>();

        var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            var window = new byte[blockSizeBytes];
            int windowLen = await DeltaCopySignatureBuilder.ReadFullAsync(stream, window, blockSizeBytes, ct).ConfigureAwait(false);
            if (windowLen == 0)
                return instructions;

            var rolling = new RollingChecksum();
            for (int i = 0; i < windowLen; i++)
                rolling.AddByte(window[i]);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (TryMatch(window, windowLen, rolling.Value, signature, out var matched))
                {
                    FlushLiteral(literalBuffer, instructions);
                    instructions.Add(new CopyBlockInstruction(matched!.Offset, matched.Length));

                    windowLen = await DeltaCopySignatureBuilder.ReadFullAsync(stream, window, blockSizeBytes, ct).ConfigureAwait(false);
                    if (windowLen == 0)
                        break;

                    rolling.Reset();
                    for (int i = 0; i < windowLen; i++)
                        rolling.AddByte(window[i]);
                    continue;
                }

                literalBuffer.Add(window[0]);
                if (literalBuffer.Count >= blockSizeBytes)
                    FlushLiteral(literalBuffer, instructions);

                int nextByte = stream.ReadByte();
                if (nextByte < 0)
                {
                    for (int i = 1; i < windowLen; i++)
                        literalBuffer.Add(window[i]);
                    break;
                }

                byte outgoing = window[0];
                Array.Copy(window, 1, window, 0, windowLen - 1);
                window[windowLen - 1] = (byte)nextByte;
                rolling.Roll(outgoing, (byte)nextByte);
            }
        }

        FlushLiteral(literalBuffer, instructions);
        return instructions;
    }

    private static bool TryMatch(
        byte[] window, int windowLen, uint weak, DeltaSignature signature, out SignatureBlock? matched)
    {
        matched = null;
        if (!signature.BlocksByWeak.TryGetValue(weak, out var candidates))
            return false;

        byte[]? strong = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Length != windowLen)
                continue;

            strong ??= SHA256.HashData(window.AsSpan(0, windowLen));
            if (strong.AsSpan().SequenceEqual(candidate.StrongHash))
            {
                matched = candidate;
                return true;
            }
        }
        return false;
    }

    private static void FlushLiteral(List<byte> buffer, List<DeltaCopyInstruction> instructions)
    {
        if (buffer.Count == 0)
            return;
        instructions.Add(new LiteralInstruction(buffer.ToArray()));
        buffer.Clear();
    }
}
```

- [x] **Step 4: Esegui e verifica il successo**

Run: `dotnet test --filter DeltaCopyScannerTests`
Expected: PASS (5/5)

- [x] **Step 5: Commit**

```bash
git add Sbroglione/Services/DeltaCopyScanner.cs Sbroglione.Tests/DeltaCopyScannerTests.cs
git commit -m "feat: add DeltaCopyScanner (rsync-style rolling match)"
```

---

### Task 4: DeltaCopyApplier

**Model:** sonnet (I/O sequenziale su istruzioni già definite, pattern temp-file+rename già visto altrove nel repo)

**Files:**
- Create: `Sbroglione/Services/DeltaCopyApplier.cs`
- Test: `Sbroglione.Tests/DeltaCopyApplierTests.cs`

**Interfaces:**
- Consumes: `DeltaCopyInstruction`/`CopyBlockInstruction`/`LiteralInstruction` (Task 2), `IoThrottleService.WaitAsync` (esistente).
- Produces: `Sbroglione.Services.DeltaCopyApplier.ApplyAsync(IReadOnlyList<DeltaCopyInstruction> instructions, string oldDestPath, string finalDestPath, Action<long>? onBytesCopied, CancellationToken ct) -> Task`.

- [x] **Step 1: Scrivi i test che falliscono**

```csharp
// Sbroglione.Tests/DeltaCopyApplierTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyApplierTests : IDisposable
{
    private readonly string _root;

    public DeltaCopyApplierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltaapply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public async Task ApplyAsync_MixedInstructions_ReconstructsExactBytes()
    {
        byte[] oldContent = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction>
        {
            new LiteralInstruction(new byte[] { 100, 101 }),
            new CopyBlockInstruction(DestOffset: 5, Length: 3),
            new LiteralInstruction(new byte[] { 102 })
        };

        long totalReported = 0;
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, len => totalReported += len, CancellationToken.None);

        byte[] result = await File.ReadAllBytesAsync(destPath);
        Assert.Equal(new byte[] { 100, 101, 5, 6, 7, 102 }, result);
        Assert.Equal(6, totalReported);
    }

    [Fact]
    public async Task ApplyAsync_NoTempFileLeftBehindAfterSuccess()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction> { new CopyBlockInstruction(0, 3) };
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, CancellationToken.None);

        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ApplyAsync_EmptyInstructions_ProducesEmptyFile()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        await DeltaCopyApplier.ApplyAsync(new List<DeltaCopyInstruction>(), destPath, destPath, null, CancellationToken.None);

        Assert.Empty(await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task ApplyAsync_Cancelled_DoesNotLeaveTempFile()
    {
        byte[] oldContent = { 1, 2, 3 };
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(destPath, oldContent);

        var instructions = new List<DeltaCopyInstruction> { new LiteralInstruction(new byte[] { 9 }) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, null, cts.Token));

        Assert.Single(Directory.GetFiles(_root)); // solo dest.bin originale, nessun .tmp orfano
    }
}
```

- [x] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test --filter DeltaCopyApplierTests`
Expected: FAIL (compile error, `DeltaCopyApplier` non esiste)

- [x] **Step 3: Implementa**

```csharp
// Sbroglione/Services/DeltaCopyApplier.cs
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Applica le istruzioni prodotte da <see cref="DeltaCopyScanner"/>: scrive un file
/// temporaneo nella stessa cartella di <paramref name="finalDestPath"/> (letterali dalla
/// sorgente già incorporati nelle istruzioni, blocchi letti dal vecchio <paramref
/// name="oldDestPath"/>), poi lo sostituisce con un <see cref="File.Move"/> atomico.
/// A differenza della copia piena attuale, un crash a metà lascia la destinazione intatta
/// (il file temporaneo viene eliminato, mai la destinazione).
/// </summary>
public static class DeltaCopyApplier
{
    public static async Task ApplyAsync(
        IReadOnlyList<DeltaCopyInstruction> instructions,
        string oldDestPath,
        string finalDestPath,
        Action<long>? onBytesCopied,
        CancellationToken ct)
    {
        string tempPath = finalDestPath + ".sbroglione-delta-tmp";
        try
        {
            var oldDest = new FileStream(oldDestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using (oldDest.ConfigureAwait(false))
            {
                var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using (output.ConfigureAwait(false))
                {
                    foreach (var instruction in instructions)
                    {
                        ct.ThrowIfCancellationRequested();

                        switch (instruction)
                        {
                            case LiteralInstruction literal:
                                await IoThrottleService.WaitAsync(literal.Data.Length, ct).ConfigureAwait(false);
                                await output.WriteAsync(literal.Data, ct).ConfigureAwait(false);
                                onBytesCopied?.Invoke(literal.Data.Length);
                                break;

                            case CopyBlockInstruction block:
                                oldDest.Seek(block.DestOffset, SeekOrigin.Begin);
                                var buffer = new byte[block.Length];
                                int totalRead = 0;
                                while (totalRead < block.Length)
                                {
                                    int read = await oldDest.ReadAsync(
                                        buffer.AsMemory(totalRead, block.Length - totalRead), ct).ConfigureAwait(false);
                                    if (read == 0) break;
                                    totalRead += read;
                                }
                                await output.WriteAsync(buffer.AsMemory(0, totalRead), ct).ConfigureAwait(false);
                                onBytesCopied?.Invoke(totalRead);
                                break;
                        }
                    }

                    await output.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, finalDestPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}
```

- [x] **Step 4: Esegui e verifica il successo**

Run: `dotnet test --filter DeltaCopyApplierTests`
Expected: PASS (4/4)

- [x] **Step 5: Commit**

```bash
git add Sbroglione/Services/DeltaCopyApplier.cs Sbroglione.Tests/DeltaCopyApplierTests.cs
git commit -m "feat: add DeltaCopyApplier with atomic temp-file rename"
```

---

### Task 5: DeltaCopyService (orchestratore + fallback)

**Model:** sonnet (orchestrazione lineare di pezzi già testati, regole di fallback esplicite)

**Files:**
- Create: `Sbroglione/Services/DeltaCopyService.cs`
- Modify: `Sbroglione/Models/AppSettings.cs` (nuovo campo `DeltaBlockSizeKB`)
- Test: `Sbroglione.Tests/DeltaCopyServiceTests.cs`

**Interfaces:**
- Consumes: `DeltaCopySignatureBuilder.BuildAsync` (Task 2), `DeltaCopyScanner.ScanAsync` (Task 3), `DeltaCopyApplier.ApplyAsync` (Task 4), `AppSettingsStore.Current` (esistente).
- Produces: `Sbroglione.Services.DeltaCopyService.TryDeltaCopyAsync(string sourcePath, string destPath, Action<long>? onBytesCopied, CancellationToken ct) -> Task<bool>` (usato da Task 6).

- [x] **Step 1: Aggiungi il campo alle impostazioni**

In `Sbroglione/Models/AppSettings.cs`, accanto a `ThrottleMBps`:

```csharp
    /// <summary>Dimensione del blocco (KB) usato dal delta-copy per il rolling checksum.</summary>
    public int DeltaBlockSizeKB { get; set; } = 128;
```

- [x] **Step 2: Scrivi i test che falliscono**

```csharp
// Sbroglione.Tests/DeltaCopyServiceTests.cs
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class DeltaCopyServiceTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;

    public DeltaCopyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-deltasvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings { DeltaBlockSizeKB = 1 }; // blocchi da 1KB nei test
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task TryDeltaCopyAsync_DestinationMissing_ReturnsFalse()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, new byte[3000]);
        string destPath = Path.Combine(_root, "dest.bin");

        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.False(result);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task TryDeltaCopyAsync_SourceSmallerThanOneBlock_ReturnsFalse()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(sourcePath, new byte[10]);
        await File.WriteAllBytesAsync(destPath, new byte[10]);

        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryDeltaCopyAsync_ModifiedMiddleSection_ProducesByteExactResultAndReportsProgress()
    {
        byte[] original = new byte[5000];
        new Random(42).NextBytes(original);
        byte[] modified = (byte[])original.Clone();
        for (int i = 2000; i < 2100; i++) modified[i] = (byte)~modified[i];

        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(sourcePath, modified);
        await File.WriteAllBytesAsync(destPath, original);

        long reported = 0;
        bool result = await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, len => reported += len, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(modified, await File.ReadAllBytesAsync(destPath));
        Assert.Equal(modified.Length, reported);
    }

    [Fact]
    public async Task TryDeltaCopyAsync_PreservesSourceLastWriteTime()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        var content = new byte[3000];
        await File.WriteAllBytesAsync(sourcePath, content);
        await File.WriteAllBytesAsync(destPath, content);
        var sourceTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceTime);

        await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destPath, null, CancellationToken.None);

        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(destPath));
    }
}
```

- [x] **Step 3: Esegui e verifica il fallimento**

Run: `dotnet test --filter DeltaCopyServiceTests`
Expected: FAIL (compile error, `DeltaCopyService` non esiste)

- [x] **Step 4: Implementa**

```csharp
// Sbroglione/Services/DeltaCopyService.cs
namespace Sbroglione.Services;

/// <summary>
/// Orchestratore del delta-copy: se la destinazione esiste ed è abbastanza grande, calcola
/// la signature, scansiona la sorgente e applica il risultato. Ritorna <c>false</c> (nessuna
/// modifica su disco) in tutti i casi in cui conviene lasciare al chiamante la copia piena.
/// </summary>
public static class DeltaCopyService
{
    public static async Task<bool> TryDeltaCopyAsync(
        string sourcePath, string destPath, Action<long>? onBytesCopied, CancellationToken ct)
    {
        if (!File.Exists(destPath))
            return false;

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            return false;

        int blockSizeBytes = Math.Max(1, AppSettingsStore.Current.DeltaBlockSizeKB) * 1024;

        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length < blockSizeBytes)
            return false;

        DeltaSignature signature;
        try
        {
            signature = await DeltaCopySignatureBuilder.BuildAsync(destPath, blockSizeBytes, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var instructions = await DeltaCopyScanner.ScanAsync(sourcePath, signature, blockSizeBytes, ct).ConfigureAwait(false);
        await DeltaCopyApplier.ApplyAsync(instructions, destPath, destPath, onBytesCopied, ct).ConfigureAwait(false);

        File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        return true;
    }
}
```

- [x] **Step 5: Esegui e verifica il successo**

Run: `dotnet test --filter DeltaCopyServiceTests`
Expected: PASS (4/4)

- [x] **Step 6: Commit**

```bash
git add Sbroglione/Services/DeltaCopyService.cs Sbroglione/Models/AppSettings.cs Sbroglione.Tests/DeltaCopyServiceTests.cs
git commit -m "feat: add DeltaCopyService orchestrator with fallback rules"
```

---

### Task 6: Integrazione in FileCopyService (singolo file + multi-destinazione)

**Model:** sonnet (modifica di un file esistente sensibile con concorrenza già presente — richiede attenzione ma segue un pattern chiaro dato dal piano)

**Files:**
- Modify: `Sbroglione/Services/FileCopyService.cs:46` (`CopyFileAsync`), `:102` (`CopyFileToManyAsync`)
- Test: `Sbroglione.Tests/FileCopyServiceTests.cs` (nuovi casi)

**Interfaces:**
- Consumes: `DeltaCopyService.TryDeltaCopyAsync` (Task 5).
- Produces: `CopyFileAsync(..., bool deltaCopyEnabled = false)`, `CopyFileToManyAsync(..., bool deltaCopyEnabled = false)` — nuovo parametro opzionale, default `false` invariato per tutti i chiamanti esistenti.

- [x] **Step 1: Scrivi i test che falliscono**

Aggiungi a `Sbroglione.Tests/FileCopyServiceTests.cs`:

```csharp
    [Fact]
    public async Task CopyFileAsync_DeltaCopyEnabled_DestExists_ReconstructsExactContent()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        byte[] original = new byte[300 * 1024];
        new Random(1).NextBytes(original);
        byte[] modified = (byte[])original.Clone();
        for (int i = 0; i < 500; i++) modified[i] = (byte)~modified[i];

        await File.WriteAllBytesAsync(destPath, original);
        await File.WriteAllBytesAsync(sourcePath, modified);

        await FileCopyService.CopyFileAsync(sourcePath, destPath, null, CancellationToken.None, deltaCopyEnabled: true);

        Assert.Equal(modified, await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task CopyFileAsync_DeltaCopyEnabled_DestMissing_FallsBackToFullCopy()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        string destPath = Path.Combine(_root, "dest.bin");
        await File.WriteAllBytesAsync(sourcePath, new byte[300 * 1024]);

        await FileCopyService.CopyFileAsync(sourcePath, destPath, null, CancellationToken.None, deltaCopyEnabled: true);

        Assert.True(File.Exists(destPath));
        Assert.Equal(await File.ReadAllBytesAsync(sourcePath), await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task CopyFileToManyAsync_DeltaCopyEnabled_EachDestinationReconstructedIndependently()
    {
        string sourcePath = Path.Combine(_root, "source.bin");
        byte[] source = new byte[300 * 1024];
        new Random(2).NextBytes(source);
        await File.WriteAllBytesAsync(sourcePath, source);

        string dest1 = Path.Combine(_root, "d1.bin");
        string dest2 = Path.Combine(_root, "d2.bin");
        await File.WriteAllBytesAsync(dest1, source); // identico: tutto CopyBlock
        await File.WriteAllBytesAsync(dest2, new byte[300 * 1024]); // tutto diverso: tutto Literal

        var result = await FileCopyService.CopyFileToManyAsync(
            sourcePath, new[] { dest1, dest2 }, null, CancellationToken.None, deltaCopyEnabled: true);

        Assert.Equal(2, result.SucceededDestinations.Count);
        Assert.Equal(source, await File.ReadAllBytesAsync(dest1));
        Assert.Equal(source, await File.ReadAllBytesAsync(dest2));
    }
```

- [x] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: FAIL (compile error, overload `deltaCopyEnabled` non esiste)

- [x] **Step 3: Implementa — `CopyFileAsync`**

In `Sbroglione/Services/FileCopyService.cs`, modifica la firma e l'inizio del metodo (riga 46):

```csharp
    public static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool deltaCopyEnabled = false)
    {
        if (deltaCopyEnabled &&
            await DeltaCopyService.TryDeltaCopyAsync(sourcePath, destinationPath, onBytesCopied, ct).ConfigureAwait(false))
            return;

        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        // ... resto del metodo invariato ...
```

- [x] **Step 4: Implementa — `CopyFileToManyAsync`**

Rinomina il corpo esistente del metodo (righe 102-208) in un metodo privato `CopyFileToManyWithFanOutAsync` con la stessa firma, poi sostituisci `CopyFileToManyAsync` con:

```csharp
    public static Task<CopyToManyResult> CopyFileToManyAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        Action<string, long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool deltaCopyEnabled = false)
    {
        if (deltaCopyEnabled)
            return CopyFileToManyWithDeltaAsync(sourcePath, destinationPaths, onBytesCopied, ct, bufferSize);

        return CopyFileToManyWithFanOutAsync(sourcePath, destinationPaths, onBytesCopied, ct, bufferSize);
    }

    /// <summary>
    /// Variante delta-copy di <see cref="CopyFileToManyAsync"/>: nessun fan-out condiviso,
    /// ogni destinazione fa la propria scansione indipendente della sorgente (letture
    /// ripetute accettate: ogni destinazione ha un contenuto "vecchio" diverso).
    /// </summary>
    private static async Task<CopyToManyResult> CopyFileToManyWithDeltaAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        Action<string, long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize)
    {
        if (destinationPaths.Count == 0)
            return new CopyToManyResult(Array.Empty<string>(), new Dictionary<string, Exception>());

        var failed = new ConcurrentDictionary<string, Exception>();

        var tasks = destinationPaths.Select(destination => Task.Run(async () =>
        {
            try
            {
                bool delta = await DeltaCopyService.TryDeltaCopyAsync(
                    sourcePath, destination, len => onBytesCopied?.Invoke(destination, len), ct).ConfigureAwait(false);
                if (!delta)
                {
                    await CopyFileAsync(sourcePath, destination,
                        len => onBytesCopied?.Invoke(destination, len), ct, bufferSize).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed[destination] = ex;
            }
        })).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var succeeded = destinationPaths.Where(d => !failed.ContainsKey(d)).ToList();
        if (succeeded.Count == 0)
            throw failed.Values.First();

        DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        foreach (var destination in succeeded)
            File.SetLastWriteTimeUtc(destination, sourceTime);

        return new CopyToManyResult(succeeded, failed);
    }
```

Nota: `CopyFileToManyWithFanOutAsync` è il corpo esistente rinominato (stessa firma di prima, `async Task<CopyToManyResult> ... (string sourcePath, IReadOnlyList<string> destinationPaths, Action<string, long>? onBytesCopied, CancellationToken ct, int bufferSize = DefaultBufferSize)` — rimuovi solo il default `bufferSize` duplicato dato che ora è chiamato sempre con argomento esplicito).

- [x] **Step 5: Esegui e verifica il successo**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: PASS (tutti, inclusi i 3 nuovi + quelli esistenti invariati)

- [x] **Step 6: Esegui l'intera suite per assicurarti di non aver rotto nulla**

Run: `dotnet test`
Expected: PASS (tutti i test, nessuna regressione)

- [x] **Step 7: Commit**

```bash
git add Sbroglione/Services/FileCopyService.cs Sbroglione.Tests/FileCopyServiceTests.cs
git commit -m "feat: wire delta-copy into CopyFileAsync and CopyFileToManyAsync"
```

---

### Task 7: Integrazione in CopyDirectoryAsync/CopyDirectoryToManyAsync

**Model:** haiku (aggiungere e passare un parametro attraverso firme già note, meccanico)

**Files:**
- Modify: `Sbroglione/Services/FileCopyService.cs:215` (`CopyDirectoryAsync`), `:302` (`CopyDirectoryToManyAsync`)
- Test: `Sbroglione.Tests/FileCopyServiceTests.cs` (nuovi casi)

**Interfaces:**
- Consumes: `CopyFileAsync`/`CopyFileToManyAsync` con `deltaCopyEnabled` (Task 6).
- Produces: `CopyDirectoryAsync(..., bool deltaCopyEnabled = false)`, `CopyDirectoryToManyAsync(..., bool deltaCopyEnabled = false)`.

- [x] **Step 1: Scrivi i test che falliscono**

Aggiungi a `Sbroglione.Tests/FileCopyServiceTests.cs`:

```csharp
    [Fact]
    public async Task CopyDirectoryAsync_DeltaCopyEnabled_ExistingFileReconstructedExactly()
    {
        string sourceDir = Path.Combine(_root, "src");
        string destDir = Path.Combine(_root, "dst");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        byte[] original = new byte[300 * 1024];
        new Random(3).NextBytes(original);
        byte[] modified = (byte[])original.Clone();
        modified[0] = (byte)~modified[0];

        await File.WriteAllBytesAsync(Path.Combine(destDir, "f.bin"), original);
        await File.WriteAllBytesAsync(Path.Combine(sourceDir, "f.bin"), modified);

        await FileCopyService.CopyDirectoryAsync(
            sourceDir, destDir, maxDegreeOfParallelism: 1, onProgress: null, CancellationToken.None,
            deltaCopyEnabled: true);

        Assert.Equal(modified, await File.ReadAllBytesAsync(Path.Combine(destDir, "f.bin")));
    }

    [Fact]
    public async Task CopyDirectoryToManyAsync_DeltaCopyEnabled_BothDestinationsReconstructed()
    {
        string sourceDir = Path.Combine(_root, "src2");
        string dst1 = Path.Combine(_root, "dst2a");
        string dst2 = Path.Combine(_root, "dst2b");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(dst1);
        Directory.CreateDirectory(dst2);

        byte[] source = new byte[300 * 1024];
        new Random(4).NextBytes(source);
        await File.WriteAllBytesAsync(Path.Combine(sourceDir, "f.bin"), source);
        await File.WriteAllBytesAsync(Path.Combine(dst1, "f.bin"), source);
        await File.WriteAllBytesAsync(Path.Combine(dst2, "f.bin"), new byte[300 * 1024]);

        await FileCopyService.CopyDirectoryToManyAsync(
            sourceDir, new[] { dst1, dst2 }, maxDegreeOfParallelism: 1, onProgress: null, CancellationToken.None,
            deltaCopyEnabled: true);

        Assert.Equal(source, await File.ReadAllBytesAsync(Path.Combine(dst1, "f.bin")));
        Assert.Equal(source, await File.ReadAllBytesAsync(Path.Combine(dst2, "f.bin")));
    }
```

- [x] **Step 2: Esegui e verifica il fallimento**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: FAIL (compile error, overload `deltaCopyEnabled` non esiste su `CopyDirectoryAsync`/`CopyDirectoryToManyAsync`)

- [x] **Step 3: Implementa**

In `CopyDirectoryAsync` (riga 215), aggiungi il parametro e passalo alla chiamata a `CopyFileAsync` (riga ~272):

```csharp
    public static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false,
        Action<string>? onFileStarted = null,
        Action<string>? onFileCompleted = null,
        ExtensionFilter? extensionFilter = null,
        bool deltaCopyEnabled = false)
```

```csharp
                await CopyFileAsync(sourceFile, destinationFile, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize, deltaCopyEnabled).ConfigureAwait(false);
```

In `CopyDirectoryToManyAsync` (riga 302), stesso pattern: aggiungi il parametro e passalo alla chiamata a `CopyFileToManyAsync` (riga ~406):

```csharp
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
        Action<string, string, Exception>? onFileFailed = null,
        ExtensionFilter? extensionFilter = null,
        bool deltaCopyEnabled = false)
```

```csharp
                    copyResult = await CopyFileToManyAsync(sourceFile, copyDestinationFiles, (destinationFile, deltaBytes) =>
                    {
                        string root = rootByDestinationFile[destinationFile];
                        long newTotal = Interlocked.Add(ref counters[root].Value, deltaBytes);
                        onProgress?.Invoke(root, new CopyProgress(newTotal, totalBytes, files.Count));
                    }, ct, bufferSize, deltaCopyEnabled).ConfigureAwait(false);
```

- [x] **Step 4: Esegui e verifica il successo**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: PASS

- [x] **Step 5: Esegui l'intera suite**

Run: `dotnet test`
Expected: PASS (nessuna regressione)

- [x] **Step 6: Commit**

```bash
git add Sbroglione/Services/FileCopyService.cs Sbroglione.Tests/FileCopyServiceTests.cs
git commit -m "feat: thread deltaCopyEnabled through directory copy methods"
```

---

### Task 8: Impostazioni UI per il block size

**Model:** haiku (nuovo campo impostazioni che ricalca esattamente il pattern ThrottleMBps esistente)

**Files:**
- Modify: `Sbroglione/ViewModels/SettingsViewModel.cs` (nuova proprietà accanto a `ThrottleMBps`, righe 118-129)
- Modify: `Sbroglione/Views/SettingsView.axaml` (nuovo controllo accanto al blocco throttle, righe 50-63)
- Modify: `Sbroglione/Services/Localization/StringsEn.cs`, `Sbroglione/Services/Localization/StringsIt.cs`

**Interfaces:**
- Consumes: `AppSettingsStore.Current.DeltaBlockSizeKB` (Task 5).
- Produces: nessuna nuova interfaccia consumata da altri task — è un endpoint UI.

- [ ] **Step 1: Aggiungi le stringhe localizzate**

In `Sbroglione/Services/Localization/StringsEn.cs`, accanto alle chiavi `Str.Settings.*` esistenti per il throttle:

```csharp
        ["Str.Settings.DeltaBlockSize"] = "Delta-copy block size (KB)",
```

In `Sbroglione/Services/Localization/StringsIt.cs`:

```csharp
        ["Str.Settings.DeltaBlockSize"] = "Dimensione blocco delta-copy (KB)",
```

(Cerca la chiave esatta `Str.Settings.Throttle` in entrambi i file per posizionare la nuova voce nello stesso blocco.)

- [ ] **Step 2: Aggiungi la proprietà al ViewModel**

In `Sbroglione/ViewModels/SettingsViewModel.cs`, accanto a `ThrottleMBps` (righe 118-129):

```csharp
    public int DeltaBlockSizeKB
    {
        get => AppSettingsStore.Current.DeltaBlockSizeKB;
        set
        {
            int clamped = Math.Max(1, value);
            if (AppSettingsStore.Current.DeltaBlockSizeKB == clamped)
                return;
            AppSettingsStore.Current.DeltaBlockSizeKB = clamped;
            this.RaisePropertyChanged();
            AppSettingsStore.SaveBestEffort();
        }
    }
```

(Verifica il nome esatto del metodo di salvataggio guardando come termina il setter di `ThrottleMBps` in questo stesso file — usa lo stesso.)

- [ ] **Step 3: Aggiungi il controllo XAML**

In `Sbroglione/Views/SettingsView.axaml`, subito dopo il blocco del throttle (righe 50-63):

```xml
            <Grid ColumnDefinitions="*,Auto" Margin="0,8,0,0">
              <TextBlock Grid.Column="0" Text="{DynamicResource Str.Settings.DeltaBlockSize}" VerticalAlignment="Center" />
              <NumericUpDown Grid.Column="1" Minimum="1" Maximum="65536"
                             Value="{Binding DeltaBlockSizeKB}" Width="120" />
            </Grid>
```

- [ ] **Step 4: Verifica manuale**

Run: `dotnet build Sbroglione.sln` — deve compilare senza errori.
Avvia l'app (`dotnet run --project Sbroglione.Desktop`), vai in Impostazioni, verifica che il campo compaia, accetti solo interi ≥1, e che il valore persista dopo riavvio (legge/scrive `AppSettingsStore`).

- [ ] **Step 5: Commit**

```bash
git add Sbroglione/ViewModels/SettingsViewModel.cs Sbroglione/Views/SettingsView.axaml Sbroglione/Services/Localization/StringsEn.cs Sbroglione/Services/Localization/StringsIt.cs
git commit -m "feat: add delta-copy block size setting to Impostazioni"
```

---

### Task 9: Toggle per-pair, persistenza profilo e wiring copia

**Model:** sonnet (tocca più file collegati — ViewModel, modello persistito, XAML, localizzazione — serve tenere insieme il quadro completo)

**Files:**
- Modify: `Sbroglione/ViewModels/FolderFilePairViewModel.cs:337` (accanto a `SkipUnchanged`)
- Modify: `Sbroglione/Models/CopyProfile.cs:20` (accanto a `SkipUnchanged` in `CopyProfilePair`)
- Modify: `Sbroglione/ViewModels/CopyPairsViewModel.cs` (righe ~234, ~282, ~660, ~835 — vedi sotto)
- Modify: `Sbroglione/Views/CopyPairsView.axaml:157` (accanto al `CheckBox` di `ClearDestinationBeforeCopy`)
- Modify: `Sbroglione/Services/Localization/StringsEn.cs`, `Sbroglione/Services/Localization/StringsIt.cs`
- Test: `Sbroglione.Tests/CopyProfileStoreTests.cs`, `Sbroglione.Tests/CopyPairsViewModelTests.cs` (nuovi casi)

**Interfaces:**
- Consumes: `CopyFileToManyAsync`/`CopyDirectoryToManyAsync` con `deltaCopyEnabled` (Task 6, 7).
- Produces: `FolderFilePairViewModel.DeltaCopyEnabled` (bool), `CopyProfilePair.DeltaCopyEnabled` (bool) — persistiti/letti come `SkipUnchanged`.

- [x] **Step 1: Aggiungi le stringhe localizzate**

`Sbroglione/Services/Localization/StringsEn.cs`, accanto a `Str.CopyPairs.ClearDestination`:

```csharp
        ["Str.CopyPairs.DeltaCopy"] = "Delta-copy (only changed blocks)",
```

`Sbroglione/Services/Localization/StringsIt.cs`:

```csharp
        ["Str.CopyPairs.DeltaCopy"] = "Delta-copy (solo blocchi cambiati)",
```

- [x] **Step 2: Aggiungi la proprietà al modello persistito**

In `Sbroglione/Models/CopyProfile.cs`, in `CopyProfilePair` accanto a `SkipUnchanged` (riga 20):

```csharp
    public bool DeltaCopyEnabled { get; set; }
```

- [x] **Step 3: Aggiungi la proprietà al ViewModel della pair**

In `Sbroglione/ViewModels/FolderFilePairViewModel.cs`, accanto a `SkipUnchanged` (riga 337):

```csharp
    /// <summary>Se true, per i file già esistenti in destinazione copia solo i blocchi cambiati (rolling checksum).</summary>
    public bool DeltaCopyEnabled { get; set; }
```

- [x] **Step 4: Scrivi i test che falliscono per la persistenza**

Aggiungi a `Sbroglione.Tests/CopyProfileStoreTests.cs` (segui il pattern dei test esistenti in quel file per save/load roundtrip — cerca un test che salva e ricarica un `CopyProfile` con `SkipUnchanged = true` e usalo come modello):

```csharp
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsDeltaCopyEnabled()
    {
        var profile = new CopyProfile
        {
            Name = "Test",
            Pairs = new List<CopyProfilePair>
            {
                new() { SourcePath = "C:\\a", DestinationPath = "C:\\b", DeltaCopyEnabled = true }
            }
        };

        await CopyProfileStore.SaveAsync(new[] { profile });
        var loaded = await CopyProfileStore.LoadAsync();

        Assert.True(loaded.Single().Pairs.Single().DeltaCopyEnabled);
    }
```

(Adatta `CurrentPath`/setup allo stile esatto già usato nelle altre classi di test di questo file, es. `CopyProfileStore.CurrentPath = ...` su un path temporaneo nel costruttore/`IDisposable.Dispose`.)

- [x] **Step 5: Esegui e verifica il fallimento**

Run: `dotnet test --filter CopyProfileStoreTests`
Expected: FAIL (compile error, `DeltaCopyEnabled` non esiste su `CopyProfilePair`) — se il test fallisce già a compilazione per lo Step 2/3 non ancora fatti, applica prima quegli step (l'ordine sopra li mette prima apposta).

- [x] **Step 6: Wiring salvataggio/caricamento profilo in CopyPairsViewModel**

In `Sbroglione/ViewModels/CopyPairsViewModel.cs`, nel blocco di `SaveProfileAsync` (righe 229-237, dentro il `.Select(p => new CopyProfilePair { ... })`):

```csharp
            .Select(p => new CopyProfilePair
            {
                SourcePath = p.SourcePath ?? string.Empty,
                DestinationPath = p.DestinationPath ?? string.Empty,
                ExtraDestinations = p.ExtraDestinations.Select(e => e.Path).ToList(),
                SkipUnchanged = p.SkipUnchanged,
                DeltaCopyEnabled = p.DeltaCopyEnabled,
                ExtensionFilterMode = p.ExtensionFilterMode,
                ExtensionFilterText = p.ExtensionFilterText
            })
```

Nel blocco di `ApplyProfile` (righe 278-285, dentro il `new FolderFilePairViewModel { ... }`):

```csharp
            var pair = new FolderFilePairViewModel
            {
                SourcePath = stored.SourcePath,
                DestinationPath = stored.DestinationPath,
                SkipUnchanged = stored.SkipUnchanged,
                DeltaCopyEnabled = stored.DeltaCopyEnabled,
                ExtensionFilterMode = stored.ExtensionFilterMode,
                ExtensionFilterText = stored.ExtensionFilterText
            };
```

- [x] **Step 7: Wiring avvio copia (singolo file e cartella)**

Alla chiamata `FileCopyService.CopyFileToManyAsync` (riga ~660), aggiungi l'argomento finale:

```csharp
        var copyResult = await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, (destinationFile, deltaBytes) =>
        {
            // ... corpo invariato ...
        }, ct, bufferSize: AppSettingsStore.Current.BufferSizeBytes, deltaCopyEnabled: pair.DeltaCopyEnabled);
```

(Verifica il valore esatto di `bufferSize` già passato in questa chiamata prima di aggiungere `deltaCopyEnabled` — deve restare invariato, si aggiunge solo il nuovo argomento.)

Alla chiamata `FileCopyService.CopyDirectoryToManyAsync` (righe 828-836):

```csharp
        var result = await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: (destination, progress) => publisherByRoot[destination].Report(progress),
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged,
            deltaCopyEnabled: pair.DeltaCopyEnabled,
            extensionFilter: pair.BuildExtensionFilter(),
            onFileStarted: (destination, sourceFile) => { /* invariato */ },
            onFileCompleted: (destination, sourceFile) => { /* invariato */ });
```

- [x] **Step 8: Aggiungi il checkbox in XAML**

In `Sbroglione/Views/CopyPairsView.axaml`, subito dopo il `CheckBox` di `ClearDestinationBeforeCopy` (righe 157-159):

```xml
                  <CheckBox Content="{DynamicResource Str.CopyPairs.DeltaCopy}"
                            IsChecked="{Binding DeltaCopyEnabled}"
                            IsEnabled="{Binding !IsCopying}" />
```

- [x] **Step 9: Esegui e verifica il successo**

Run: `dotnet test --filter "CopyProfileStoreTests|CopyPairsViewModelTests|FileCopyServiceTests"`
Expected: PASS

- [x] **Step 10: Esegui l'intera suite**

Run: `dotnet test`
Expected: PASS (nessuna regressione)

- [ ] **Step 11: Verifica manuale** (skipped: no display in this environment)

Run: `dotnet build Sbroglione.sln && dotnet run --project Sbroglione.Desktop`
Nella tab Copia: aggiungi una coppia, spunta "Delta-copy", copia una cartella con un file grande (>1 blocco) già esistente in destinazione con una piccola modifica, verifica che il risultato sia byte-identico alla sorgente e che il salvataggio/caricamento profilo mantenga lo stato del checkbox.

- [x] **Step 12: Commit**

```bash
git add Sbroglione/ViewModels/FolderFilePairViewModel.cs Sbroglione/Models/CopyProfile.cs Sbroglione/ViewModels/CopyPairsViewModel.cs Sbroglione/Views/CopyPairsView.axaml Sbroglione/Services/Localization/StringsEn.cs Sbroglione/Services/Localization/StringsIt.cs Sbroglione.Tests/CopyProfileStoreTests.cs
git commit -m "feat: expose delta-copy toggle per copy pair with profile persistence"
```

---

### Task 10: Aggiornamento IDEE.md

**Model:** haiku (modifica testuale di documentazione)

**Files:**
- Modify: `IDEE.md:19`

**Interfaces:** nessuna (documentazione).

- [x] **Step 1: Aggiorna la voce**

In `IDEE.md`, sostituisci la riga 19:

```markdown
5. `[x]` **Delta-copy stile rsync** — se il file di destinazione esiste, copiare solo i blocchi cambiati (rolling checksum). Enorme risparmio su file grandi modificati poco (VM, database, video in editing). *(A)* *(implementata Fase 1: copia locale/locale, algoritmo rsync a due livelli weak+strong hash in `DeltaCopyScanner`, applicazione via temp-file+rename atomico in `DeltaCopyApplier`, opt-in per coppia (`DeltaCopyEnabled`), block size configurabile in Impostazioni. FTP/SFTP fuori scope: richiederebbe bypassare le API whole-file di FluentFTP/SSH.NET, nessuna primitiva a blocchi/offset esposta oggi — da valutare come voce separata)*
```

- [ ] **Step 2: Commit**

```bash
git add IDEE.md
git commit -m "docs: mark IDEE #5 (delta-copy) done for local/local scope"
```

---

## Self-review

- **Copertura spec**: signature builder §2 → Task 2; scanner §3 → Task 3; applier §4 → Task 4; orchestratore §5 → Task 5; integrazione FileCopyService §6 → Task 6, 7; UI/persistenza §7 → Task 8, 9; edge case §8 (file più corto, identico, cancellazione) → coperti nei test di Task 3 (`ScanAsync_SourceShorterThanDest_*`) e Task 4 (`ApplyAsync_Cancelled_*`, `ApplyAsync_EmptyInstructions_*`).
- **Placeholder**: nessuno — ogni step ha codice completo, nessun "TBD"/"gestisci edge case" generico.
- **Coerenza tipi**: `DeltaCopyInstruction`/`CopyBlockInstruction`/`LiteralInstruction` (Task 2) usati identicamente in Task 3/4/6; `DeltaSignature.BlocksByWeak` stesso tipo (`IReadOnlyDictionary<uint, List<SignatureBlock>>`) in Task 2/3; `TryDeltaCopyAsync` stessa firma in Task 5/6; `deltaCopyEnabled` stesso nome/tipo in Task 6/7/9.
