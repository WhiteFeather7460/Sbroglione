# FTP/SFTP Upload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. After each completed task, tick its checkboxes in THIS file before starting the next task (CLAUDE.md rule). Each task declares a **Model** — dispatch its subagent with that model.

**Goal:** Nella tab "Server remoto" aggiungere il caricamento (upload) di file e cartelle locali verso il server FTP/FTPS/SFTP connesso, con opzione di sovrascrittura dei file già presenti.

**Architecture:** Mirror simmetrico del flusso di download già esistente. `IRemoteFileClient` guadagna `UploadFileAsync`, implementato in `FtpRemoteClient` (FluentFTP, crea le cartelle remote mancanti nativamente) e `SftpRemoteClient` (SSH.NET, cartelle create a mano camminando i segmenti del percorso). Nuovo `UploadService` statico orchestra il batch: lista una volta la cartella remota di destinazione per sapere cosa esiste già, salta i file identici (stessa dimensione e data, tolleranza 2s) a meno di overwrite forzato. `RemoteBrowserViewModel` espone `UploadFilesAsync`/`UploadFolderAsync`/`CancelUpload`, riusando `SelectPathDialog` esistente (stesso dialogo usato per la cartella di destinazione dei download) per scegliere file/cartella locali — **scelta deliberata**: niente file picker nativo multi-selezione, per restare coerenti con l'unico pattern di selezione percorsi già presente nell'app. "Carica file" seleziona quindi un file locale alla volta (ripetibile); "Carica cartella" carica ricorsivamente (rispettando il toggle "Includi sottocartelle" già esistente, riusato invariato).

**Tech Stack:** .NET 8 (progetto core), Avalonia 11 + ReactiveUI, FluentFTP 54.2.0, SSH.NET 2026.0.0, xunit (test project net10.0).

**Spec:** Nessun documento spec separato — feature bounded (mirror di un flusso già esistente), design concordato in chat. Questo piano contiene l'intero design nella sezione Architecture sopra e nei task sotto.

## Global Constraints

- Branch di lavoro: `feature/ftp-upload` (già creato, attivo; mai commit su `main`; consegna via PR).
- Tutte le stringhe UI in **italiano** (come il resto dell'app).
- Mai colori hardcoded nelle view: solo `{DynamicResource Brush.*}`; icone `fa-*` Projektanker.
- Tutto l'I/O (rete e disco) async con `CancellationToken`; mai I/O sul thread UI.
- Mai eccezioni silenziate: errori classificati (`RemoteError`) e mostrati (stesso pattern del download).
- Test: `dotnet test FileExplorer.sln` dalla root; build: `dotnet build FileExplorer.sln`.
- Commit frequenti, messaggi convenzionali, **niente co-author Claude**.
- `dotnet format whitespace` gira in automatico via hook sui file editati: non serve lanciarlo.
- Namespace: modelli in `FileExplorer.Models`, servizi in `FileExplorer.Services`, VM in `FileExplorer.ViewModels`, test in `FileExplorer.Tests`.
- Percorsi remoti usano sempre `/` come separatore; `RemoteItem.FullPath` è assoluto (es. `/home/user/docs/a.txt`).
- Nessuna protezione file-parziale lato remoto in caso di cancellazione a metà upload (stesso livello di rischio già accettato per il download sul lato remoto — la mitigazione con file `.part` esiste solo lato locale, non applicabile qui).
- `IRemoteFileClient` ha 4 implementazioni reali/di produzione (`FtpRemoteClient`, `SftpRemoteClient`) e diverse implementazioni di test (`FakeRemoteClient` + classi annidate `CancellingRemoteClient` in `DownloadServiceTests.cs`, `GatedDownloadClient` in `RemoteBrowserDownloadTests.cs`, `GatedListingClient`/`GatedConnectClient`/`GatedDisposeClient` in `RemoteBrowserViewModelTests.cs`): **ogni** implementazione va aggiornata con `UploadFileAsync`, altrimenti la solution non compila.

---

### Task 1: `IRemoteFileClient.UploadFileAsync` — interfaccia, client reali, tutti i test double

**Model:** `sonnet` (tocca API reali di FluentFTP/SSH.NET, serve precisione sulle firme)

**Files:**
- Modify: `FileExplorer/Services/IRemoteFileClient.cs`
- Modify: `FileExplorer/Services/FtpRemoteClient.cs`
- Modify: `FileExplorer/Services/SftpRemoteClient.cs`
- Modify: `FileExplorer.Tests/FakeRemoteClient.cs`
- Modify: `FileExplorer.Tests/DownloadServiceTests.cs` (classe annidata `CancellingRemoteClient`)
- Modify: `FileExplorer.Tests/RemoteBrowserDownloadTests.cs` (classe annidata `GatedDownloadClient`)
- Modify: `FileExplorer.Tests/RemoteBrowserViewModelTests.cs` (classi annidate `GatedListingClient`, `GatedConnectClient`, `GatedDisposeClient`)

**Interfaces:**
- Consumes: niente (primo task).
- Produces: `IRemoteFileClient.UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct) : Task<RemoteError?>` — usato da tutti i task successivi. `FakeRemoteClient.FailingUploads : HashSet<string>` (percorsi remoti target il cui upload simulato deve fallire) e `FakeRemoteClient.Entries` (già esistente) ora popolato anche dagli upload — usati dai test dei task successivi.

- [ ] **Step 1: Aggiungi il metodo all'interfaccia**

In `FileExplorer/Services/IRemoteFileClient.cs`, dopo `DownloadFileAsync`:

```csharp
    /// <summary>Carica un file locale su <paramref name="remoteFullPath"/>, creando le cartelle remote mancanti. Null = successo.</summary>
    Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct);
```

- [ ] **Step 2: Implementa in `FtpRemoteClient`**

In `FileExplorer/Services/FtpRemoteClient.cs`, dopo il metodo `DownloadFileAsync` esistente:

```csharp
    public async Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
    {
        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

        try
        {
            var ftpProgress = progress is null
                ? null
                : new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes));

            var status = await _client.UploadFile(localPath, remoteFullPath, FtpRemoteExists.Overwrite,
                createRemoteDir: true, progress: ftpProgress, token: ct);

            if (status != FtpStatus.Success)
                return new RemoteError(RemoteErrorKind.TransferFailed, $"Caricamento di {Path.GetFileName(localPath)} non riuscito.");

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TranslateError(ex);
        }
    }
```

`createRemoteDir: true` fa creare a FluentFTP le cartelle remote mancanti prima di caricare — nessuna logica manuale necessaria qui (a differenza di SFTP, Step 3).

- [ ] **Step 3: Implementa in `SftpRemoteClient`**

In `FileExplorer/Services/SftpRemoteClient.cs`, dopo il metodo `DownloadFileAsync` esistente:

```csharp
    public async Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(localPath);
        ArgumentNullException.ThrowIfNull(remoteFullPath);

        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

        try
        {
            int lastSlash = remoteFullPath.LastIndexOf('/');
            string remoteDir = lastSlash <= 0 ? "/" : remoteFullPath[..lastSlash];
            await EnsureRemoteDirectoryAsync(remoteDir, ct);

            var sftpProgress = progress is null
                ? null
                : new Progress<UploadFileProgressReport>(p => progress.Report((long)p.TotalBytesUploaded));

            // Lo stream va aperto in read e chiuso dopo l'upload: SSH.NET legge da qui, non serve
            // riscrivere alcuna data locale (a differenza del download, che scrive su disco).
            await using (var stream = File.OpenRead(localPath))
            {
                await _client.UploadFileAsync(stream, remoteFullPath, canOverride: true, sftpProgress, ct);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TranslateError(ex);
        }
    }

    /// <summary>
    /// Crea <paramref name="remoteDir"/> se manca, risalendo ricorsivamente i genitori mancanti:
    /// SSH.NET non offre una CreateDirectory ricorsiva ("mkdir -p") nativa.
    /// </summary>
    private async Task EnsureRemoteDirectoryAsync(string remoteDir, CancellationToken ct)
    {
        if (_client is null || remoteDir is "/" || remoteDir.Length == 0)
            return;

        if (await _client.ExistsAsync(remoteDir, ct))
            return;

        int lastSlash = remoteDir.LastIndexOf('/');
        string parent = lastSlash <= 0 ? "/" : remoteDir[..lastSlash];
        await EnsureRemoteDirectoryAsync(parent, ct);
        await _client.CreateDirectoryAsync(remoteDir, ct);
    }
```

Firme confermate per riflessione sull'assembly installato (`ssh.net` 2026.0.0): `SftpClient.UploadFileAsync(Stream input, string path, bool canOverride, IProgress<UploadFileProgressReport> uploadProgress, CancellationToken cancellationToken) : Task`; `UploadFileProgressReport.TotalBytesUploaded : ulong`; `SftpClient.ExistsAsync(string path, CancellationToken ct) : Task<bool>`; `SftpClient.CreateDirectoryAsync(string path, CancellationToken ct) : Task`. Tutte nel namespace `Renci.SshNet`, già importato in questo file — nessun nuovo `using` necessario.

- [ ] **Step 4: Aggiorna `FakeRemoteClient` (test double principale)**

In `FileExplorer.Tests/FakeRemoteClient.cs`, aggiungi dopo `FailingDownloads`:

```csharp
    /// <summary>Percorsi remoti (target) il cui upload simulato deve fallire.</summary>
    public HashSet<string> FailingUploads { get; } = new();
```

E dopo il metodo `DownloadFileAsync`:

```csharp
    public async Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingUploads.Contains(remoteFullPath))
            return new RemoteError(RemoteErrorKind.TransferFailed, "Trasferimento fallito (simulato).");

        byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
        string name = remoteFullPath[(remoteFullPath.LastIndexOf('/') + 1)..];
        var modified = File.GetLastWriteTime(localPath);
        Entries[remoteFullPath] = (new RemoteItem(name, remoteFullPath, false, bytes.Length, modified), bytes);
        progress?.Report(bytes.Length);
        return null;
    }
```

- [ ] **Step 5: Aggiorna le classi annidate che implementano `IRemoteFileClient`**

In `FileExplorer.Tests/DownloadServiceTests.cs`, dentro `CancellingRemoteClient` (implementazione diretta, non delega a un `_inner`), aggiungi:

```csharp
        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);
```

In `FileExplorer.Tests/RemoteBrowserDownloadTests.cs`, dentro `GatedDownloadClient` (delega a `_inner`), aggiungi:

```csharp
        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);
```

In `FileExplorer.Tests/RemoteBrowserViewModelTests.cs`, dentro CIASCUNA delle tre classi `GatedListingClient`, `GatedConnectClient`, `GatedDisposeClient` (tutte delegano a `_inner`), aggiungi lo stesso metodo pass-through:

```csharp
        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);
```

- [ ] **Step 6: Verifica che la solution compili**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, nessun errore (tutte le 8 implementazioni di `IRemoteFileClient` ora hanno il nuovo metodo).

- [ ] **Step 7: Commit**

```bash
git add FileExplorer/Services/IRemoteFileClient.cs FileExplorer/Services/FtpRemoteClient.cs FileExplorer/Services/SftpRemoteClient.cs FileExplorer.Tests/FakeRemoteClient.cs FileExplorer.Tests/DownloadServiceTests.cs FileExplorer.Tests/RemoteBrowserDownloadTests.cs FileExplorer.Tests/RemoteBrowserViewModelTests.cs
git commit -m "feat(remote): aggiungi UploadFileAsync a IRemoteFileClient e implementazioni"
```

---

### Task 2: Modelli upload

**Model:** `haiku` (dati puri, nessuna logica)

**Files:**
- Create: `FileExplorer/Models/UploadEntry.cs`
- Create: `FileExplorer/Models/UploadReport.cs`

**Interfaces:**
- Consumes: niente.
- Produces: `UploadEntry(string LocalPath, string RemoteRelativePath)`, `UploadFailure(UploadEntry Entry, string Reason)`, `UploadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred)`, `UploadReport(IReadOnlyList<UploadEntry> Uploaded, IReadOnlyList<UploadEntry> Skipped, IReadOnlyList<UploadFailure> Failed)` — usati da Task 3 e Task 4.

- [ ] **Step 1: Crea i file dei modelli**

`FileExplorer/Models/UploadEntry.cs`:

```csharp
namespace FileExplorer.Models;

/// <summary>File locale da caricare: percorso assoluto e percorso remoto relativo alla cartella di destinazione.</summary>
public sealed record UploadEntry(string LocalPath, string RemoteRelativePath);
```

`FileExplorer/Models/UploadReport.cs`:

```csharp
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Voce fallita con motivo presentabile.</summary>
public sealed record UploadFailure(UploadEntry Entry, string Reason);

/// <summary>Avanzamento del batch di upload.</summary>
public sealed record UploadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di upload.</summary>
public sealed record UploadReport(
    IReadOnlyList<UploadEntry> Uploaded,
    IReadOnlyList<UploadEntry> Skipped,
    IReadOnlyList<UploadFailure> Failed);
```

- [ ] **Step 2: Verifica che la solution compili**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add FileExplorer/Models/UploadEntry.cs FileExplorer/Models/UploadReport.cs
git commit -m "feat(remote): aggiungi modelli UploadEntry e UploadReport"
```

---

### Task 3: `UploadService`

**Model:** `sonnet` (logica di batch/skip da testare con cura, TDD)

**Files:**
- Create: `FileExplorer/Services/UploadService.cs`
- Test: `FileExplorer.Tests/UploadServiceTests.cs`

**Interfaces:**
- Consumes: `IRemoteFileClient.UploadFileAsync` (Task 1), `IRemoteFileClient.ListRecursiveAsync` (già esistente), `UploadEntry`/`UploadReport`/`UploadFailure`/`UploadProgress` (Task 2), `FakeRemoteClient` con `Entries`/`FailingUploads`/`AddFile` (Task 1 + preesistente).
- Produces: `UploadService.CombineRemotePath(string remoteBasePath, string relativePath) : string`, `UploadService.UploadAsync(IRemoteFileClient client, IReadOnlyList<UploadEntry> entries, string remoteBasePath, bool overwriteAlways, IProgress<UploadProgress>? progress, CancellationToken ct) : Task<UploadReport>` — usati da Task 4.

- [ ] **Step 1: Scrivi i test (falliranno: `UploadService` non esiste ancora)**

Crea `FileExplorer.Tests/UploadServiceTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class UploadServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRemoteClient _client = new();

    public UploadServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-up-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreateLocalFile(string relativeName, string content, DateTime? modified = null)
    {
        string path = Path.Combine(_root, relativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } m)
            File.SetLastWriteTime(path, m);
        return path;
    }

    private Task<UploadReport> RunAsync(
        IReadOnlyList<UploadEntry> entries,
        bool overwriteAlways = false,
        CancellationToken ct = default) =>
        UploadService.UploadAsync(_client, entries, "/srv", overwriteAlways, progress: null, ct);

    [Fact]
    public void CombineRemotePath_JoinsBaseAndRelative()
    {
        Assert.Equal("/srv/sub/c.txt", UploadService.CombineRemotePath("/srv", "sub/c.txt"));
    }

    [Fact]
    public void CombineRemotePath_TrimsSlashesAndConvertsBackslashes()
    {
        Assert.Equal("/srv/sub/c.txt", UploadService.CombineRemotePath("/srv/", @"sub\c.txt"));
    }

    [Fact]
    public async Task UploadAsync_UploadsMissingFiles()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        string b = CreateLocalFile("b.txt", "BBB");

        var report = await RunAsync(new[]
        {
            new UploadEntry(a, "a.txt"),
            new UploadEntry(b, "b.txt"),
        });

        Assert.Equal(2, report.Uploaded.Count);
        Assert.Empty(report.Skipped);
        Assert.Empty(report.Failed);
        Assert.True(_client.Entries.ContainsKey("/srv/a.txt"));
        Assert.Equal("AAA", System.Text.Encoding.UTF8.GetString(_client.Entries["/srv/a.txt"].Content));
    }

    [Fact]
    public async Task UploadAsync_CreatesRemoteSubfolders()
    {
        string c = CreateLocalFile(Path.Combine("sub", "deep", "c.txt"), "CCC");

        var report = await RunAsync(new[] { new UploadEntry(c, "sub/deep/c.txt") });

        Assert.Single(report.Uploaded);
        Assert.True(_client.Entries.ContainsKey("/srv/sub/deep/c.txt"));
    }

    [Fact]
    public async Task UploadAsync_SkipsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateLocalFile("a.txt", "AAA", modified);
        _client.AddFile("/srv/a.txt", "AAA", modified);

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") });

        Assert.Empty(report.Uploaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task UploadAsync_OverwritesDifferentFiles()
    {
        string a = CreateLocalFile("a.txt", "NUOVO CONTENUTO");
        _client.AddFile("/srv/a.txt", "vecchio");

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") });

        Assert.Single(report.Uploaded);
        Assert.Equal("NUOVO CONTENUTO", System.Text.Encoding.UTF8.GetString(_client.Entries["/srv/a.txt"].Content));
    }

    [Fact]
    public async Task UploadAsync_OverwriteAlways_UploadsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateLocalFile("a.txt", "AAA", modified);
        _client.AddFile("/srv/a.txt", "AAA", modified);

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") }, overwriteAlways: true);

        Assert.Single(report.Uploaded);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public async Task UploadAsync_FailedFile_DoesNotStopBatch()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        string b = CreateLocalFile("b.txt", "BBB");
        _client.FailingUploads.Add("/srv/a.txt");

        var report = await RunAsync(new[]
        {
            new UploadEntry(a, "a.txt"),
            new UploadEntry(b, "b.txt"),
        });

        Assert.Single(report.Uploaded);
        Assert.Single(report.Failed);
        Assert.Equal("a.txt", Path.GetFileName(report.Failed[0].Entry.LocalPath));
        Assert.False(string.IsNullOrWhiteSpace(report.Failed[0].Reason));
    }

    [Fact]
    public async Task UploadAsync_ListingFails_TreatsAllAsMissing()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        var failingListClient = new FailingListClient();

        var report = await UploadService.UploadAsync(
            failingListClient, new[] { new UploadEntry(a, "a.txt") }, "/srv",
            overwriteAlways: false, progress: null, CancellationToken.None);

        Assert.Single(report.Uploaded);
    }

    [Fact]
    public async Task UploadAsync_Cancellation_Throws()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(new[] { new UploadEntry(a, "a.txt") }, ct: cts.Token));
    }

    /// <summary>Client il cui ListRecursiveAsync fallisce sempre, per testare il fallback "nessun file esistente".</summary>
    private sealed class FailingListClient : IRemoteFileClient
    {
        public bool IsConnected => true;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(Array.Empty<RemoteItem>(), null));

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(
                Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, "boom")));

        public Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano (UploadService non esiste)**

Run: `dotnet test FileExplorer.sln --filter FullyQualifiedName~UploadServiceTests`
Expected: FAIL — errore di compilazione, "UploadService" non trovato nel namespace `FileExplorer.Services`.

- [ ] **Step 3: Implementa `UploadService`**

Crea `FileExplorer/Services/UploadService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Orchestrazione degli upload remoti: check di esistenza sul server, skip/overwrite e report batch.
/// </summary>
public static class UploadService
{
    /// <summary>Tolleranza sul confronto delle date di modifica (timestamp FTP poco precisi).</summary>
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Combina la cartella remota di destinazione con un percorso relativo (sempre con '/').</summary>
    public static string CombineRemotePath(string remoteBasePath, string relativePath) =>
        remoteBasePath.TrimEnd('/') + "/" + relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Carica in sequenza le voci indicate. Una voce già presente sul server con stessa
    /// dimensione e data viene saltata a meno di <paramref name="overwriteAlways"/>.
    /// Un errore su un file non interrompe il batch; l'annullamento sì.
    /// </summary>
    public static async Task<UploadReport> UploadAsync(
        IRemoteFileClient client,
        IReadOnlyList<UploadEntry> entries,
        string remoteBasePath,
        bool overwriteAlways,
        IProgress<UploadProgress>? progress,
        CancellationToken ct)
    {
        var uploaded = new List<UploadEntry>();
        var skipped = new List<UploadEntry>();
        var failed = new List<UploadFailure>();

        var existing = await BuildExistingRemoteMapAsync(client, remoteBasePath, ct);

        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[i];
            progress?.Report(new UploadProgress(i + 1, entries.Count, Path.GetFileName(entry.LocalPath), 0));

            string remotePath = CombineRemotePath(remoteBasePath, entry.RemoteRelativePath);

            if (!overwriteAlways && existing.TryGetValue(remotePath, out var remoteItem)
                && IsSameAsLocal(entry.LocalPath, remoteItem))
            {
                skipped.Add(entry);
                continue;
            }

            int index = i;
            var byteProgress = progress is null
                ? null
                : new Progress<long>(bytes =>
                    progress.Report(new UploadProgress(index + 1, entries.Count, Path.GetFileName(entry.LocalPath), bytes)));

            var error = await client.UploadFileAsync(entry.LocalPath, remotePath, byteProgress, ct);
            if (error is null)
                uploaded.Add(entry);
            else
                failed.Add(new UploadFailure(entry, error.Message));
        }

        return new UploadReport(uploaded, skipped, failed);
    }

    /// <summary>
    /// Elenco ricorsivo del server sotto la cartella di destinazione, indicizzato per percorso
    /// completo. Un errore di listing (es. cartella non ancora esistente) non blocca l'upload:
    /// semplicemente nessun file viene considerato già presente.
    /// </summary>
    private static async Task<Dictionary<string, RemoteItem>> BuildExistingRemoteMapAsync(
        IRemoteFileClient client, string remoteBasePath, CancellationToken ct)
    {
        var result = await client.ListRecursiveAsync(remoteBasePath, ct);
        if (result.Error is not null)
            return new Dictionary<string, RemoteItem>();

        return result.Items.ToDictionary(i => i.FullPath, i => i);
    }

    private static bool IsSameAsLocal(string localPath, RemoteItem remote)
    {
        var info = new FileInfo(localPath);
        return info.Exists
            && info.Length == remote.Size
            && (info.LastWriteTime - remote.Modified).Duration() <= DateTolerance;
    }
}
```

- [ ] **Step 4: Esegui i test e verifica che passino**

Run: `dotnet test FileExplorer.sln --filter FullyQualifiedName~UploadServiceTests`
Expected: PASS, tutti i test verdi.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/UploadService.cs FileExplorer.Tests/UploadServiceTests.cs
git commit -m "feat(remote): aggiungi UploadService con skip/overwrite e report batch"
```

---

### Task 4: `RemoteBrowserViewModel` — comandi di upload

**Model:** `sonnet` (concorrenza/guardie di rientranza da replicare con precisione, TDD)

**Files:**
- Modify: `FileExplorer/ViewModels/RemoteBrowserViewModel.cs`
- Test: `FileExplorer.Tests/RemoteBrowserUploadTests.cs`

**Interfaces:**
- Consumes: `UploadService.UploadAsync` (Task 3), `UploadEntry`/`UploadReport`/`UploadProgress` (Task 2), `IRemoteFileClient` (esistente + Task 1).
- Produces: `RemoteBrowserViewModel.UploadFilesAsync(IReadOnlyList<string> localPaths) : Task`, `UploadFolderAsync(string localFolderPath) : Task`, `CancelUpload() : void`, proprietà bindabili `IsUploading : bool`, `UploadProgressValue : double`, `UploadStatusText : string?`, `UploadOverwriteAlways : bool` — usati da Task 5 (view).

- [ ] **Step 1: Scrivi i test (falliranno: i metodi/proprietà non esistono ancora)**

Crea `FileExplorer.Tests/RemoteBrowserUploadTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserUploadTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserUploadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vmup-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<RemoteBrowserViewModel> CreateConnectedAsync(IRemoteFileClient? client = null)
    {
        var vm = new RemoteBrowserViewModel(
            _ => client ?? _client, new NullCredentialStore(), Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        return vm;
    }

    private string CreateSourceFile(string relativeName, string content)
    {
        string path = Path.Combine(_source, relativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task UploadFiles_UploadsSelectedLocalFiles()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        string b = CreateSourceFile("b.txt", "BBB");
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a, b });

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.True(_client.Entries.ContainsKey("/b.txt"));
        Assert.Contains("Caricati 2", vm.StatusMessage);
    }

    [Fact]
    public async Task UploadFiles_TargetsCurrentPath()
    {
        _client.AddDirectory("/docs");
        string a = CreateSourceFile("a.txt", "AAA");
        var vm = await CreateConnectedAsync();
        await vm.OpenDirectoryAsync(vm.Items.Single(i => i.Name == "docs"));

        await vm.UploadFilesAsync(new[] { a });

        Assert.True(_client.Entries.ContainsKey("/docs/a.txt"));
    }

    [Fact]
    public async Task UploadFolder_NonRecursive_TopLevelOnly()
    {
        CreateSourceFile("a.txt", "AAA");
        CreateSourceFile(Path.Combine("sub", "deep.txt"), "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = false;

        await vm.UploadFolderAsync(_source);

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.False(_client.Entries.ContainsKey("/sub/deep.txt"));
    }

    [Fact]
    public async Task UploadFolder_Recursive_PreservesStructure()
    {
        CreateSourceFile("a.txt", "AAA");
        CreateSourceFile(Path.Combine("sub", "deep.txt"), "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        await vm.UploadFolderAsync(_source);

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.True(_client.Entries.ContainsKey("/sub/deep.txt"));
    }

    [Fact]
    public async Task Upload_SkipsIdenticalRemoteFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateSourceFile("a.txt", "AAA");
        File.SetLastWriteTime(a, modified);
        _client.AddFile("/a.txt", "AAA", modified);
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains("saltati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Upload_OverwriteAlways_ReplacesIdenticalRemoteFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateSourceFile("a.txt", "AAA");
        File.SetLastWriteTime(a, modified);
        _client.AddFile("/a.txt", "AAA", modified);
        var vm = await CreateConnectedAsync();
        vm.UploadOverwriteAlways = true;

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains("Caricati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Upload_RefreshesListingAfterCompletion()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains(vm.Items, i => i.Name == "a.txt");
    }

    [Fact]
    public async Task CancelUpload_StopsBatchAndReportsCancellation()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        var gated = new GatedUploadClient(_client);
        var vm = await CreateConnectedAsync(gated);

        var upload = vm.UploadFilesAsync(new[] { a });
        await gated.FirstUploadStarted;
        vm.CancelUpload();
        await upload;

        Assert.Equal("Caricamento annullato.", vm.StatusMessage);
        Assert.False(vm.IsUploading);
        Assert.False(_client.Entries.ContainsKey("/a.txt"));
    }

    /// <summary>Client che sospende gli upload finché non vengono rilasciati o annullati.</summary>
    private sealed class GatedUploadClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedUploadClient(FakeRemoteClient inner) => _inner = inner;

        public Task FirstUploadStarted => _started.Task;

        public bool IsConnected => _inner.IsConnected;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
            => _inner.ListDirectoryAsync(path, ct);

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public async Task<RemoteError?> UploadFileAsync(
            string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
        {
            _started.TrySetResult();
            using (ct.Register(() => _gate.TrySetCanceled(ct)))
                await _gate.Task;
            return await _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter FullyQualifiedName~RemoteBrowserUploadTests`
Expected: FAIL — errore di compilazione, `UploadFilesAsync`/`UploadFolderAsync`/`CancelUpload`/`IsUploading`/`UploadOverwriteAlways` non esistono su `RemoteBrowserViewModel`.

- [ ] **Step 3: Aggiungi le proprietà di stato**

In `FileExplorer/ViewModels/RemoteBrowserViewModel.cs`, subito dopo il campo `private CancellationTokenSource? _downloadCts;` (fine della sezione "----- Download -----"), aggiungi:

```csharp
    // ----- Upload -----

    private bool _uploadOverwriteAlways;
    public bool UploadOverwriteAlways
    {
        get => _uploadOverwriteAlways;
        set => this.RaiseAndSetIfChanged(ref _uploadOverwriteAlways, value);
    }

    private bool _isUploading;
    public bool IsUploading
    {
        get => _isUploading;
        private set => this.RaiseAndSetIfChanged(ref _isUploading, value);
    }

    private double _uploadProgressValue;
    public double UploadProgressValue
    {
        get => _uploadProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _uploadProgressValue, value);
    }

    private string? _uploadStatusText;
    public string? UploadStatusText
    {
        get => _uploadStatusText;
        private set => this.RaiseAndSetIfChanged(ref _uploadStatusText, value);
    }

    private CancellationTokenSource? _uploadCts;
```

- [ ] **Step 4: Aggiungi `|| IsUploading` alle guardie di rientranza esistenti**

Nello stesso file, aggiungi `|| IsUploading` alla condizione di guardia in questi metodi (stesso ruolo di `IsDownloading`: un'operazione sul client è in corso, non permetterne un'altra):

- `ConnectAsync`: `if (profile is null || IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading`
- `DisconnectAsync`: `if (IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading`
- `OpenDirectoryAsync`: `if (!entry.IsDirectory || _client is null || IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading`
- `NavigateUpAsync`: `if (_client is null || IsBusy || IsDownloading || CurrentPath == "/") return;` → aggiungi `|| IsUploading` (prima del check su `CurrentPath`)
- `DeleteProfileAsync`: `if (profile is null || IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading`
- `StartDownloadAsync`: `if (_client is null || IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading` (non si può scaricare mentre si carica)
- `LoadListingAsync`: `if (_client is null || IsBusy || IsDownloading) return;` → aggiungi `|| IsUploading`

- [ ] **Step 5: Aggiungi i metodi di upload**

Nello stesso file, dopo il metodo `RunDownloadAsync` (prima di `BuildFilter`), aggiungi:

```csharp
    /// <summary>Carica i file locali indicati (percorsi assoluti) nella cartella corrente, senza struttura.</summary>
    public Task UploadFilesAsync(IReadOnlyList<string> localPaths)
    {
        var entries = localPaths
            .Select(path => new UploadEntry(path, Path.GetFileName(path)))
            .ToList();
        return RunUploadAsync(entries);
    }

    /// <summary>
    /// Carica il contenuto di una cartella locale nella cartella corrente, ricorsivamente se
    /// <see cref="IncludeSubfolders"/> è attiva, preservando la struttura relativa.
    /// </summary>
    public Task UploadFolderAsync(string localFolderPath)
    {
        var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.EnumerateFiles(localFolderPath, "*", searchOption)
            .Select(path => new UploadEntry(
                path, Path.GetRelativePath(localFolderPath, path).Replace(Path.DirectorySeparatorChar, '/')))
            .ToList();
        return RunUploadAsync(entries);
    }

    /// <summary>Annulla il batch di upload in corso: termina con "Caricamento annullato."</summary>
    public void CancelUpload() => _uploadCts?.Cancel();

    /// <summary>Guardia unica degli upload: mai in corso insieme a un download o un'altra operazione sul client.</summary>
    private async Task RunUploadAsync(IReadOnlyList<UploadEntry> entries)
    {
        if (_client is null || IsBusy || IsDownloading || IsUploading || entries.Count == 0)
            return;

        IsUploading = true;
        ErrorMessage = null;
        _uploadCts = new CancellationTokenSource();

        var progress = new Progress<UploadProgress>(p =>
        {
            UploadProgressValue = p.TotalFiles == 0 ? 0 : (double)p.FileIndex / p.TotalFiles;
            UploadStatusText = $"{p.FileIndex}/{p.TotalFiles} — {p.CurrentFile}";
        });

        try
        {
            var report = await UploadService.UploadAsync(
                _client, entries, CurrentPath, UploadOverwriteAlways, progress, _uploadCts.Token);

            StatusMessage =
                $"Caricati {report.Uploaded.Count}, saltati {report.Skipped.Count}, falliti {report.Failed.Count}.";
            if (report.Failed.Count > 0)
                ErrorMessage = $"{report.Failed.Count} file falliti. Primo errore: {report.Failed[0].Reason}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Caricamento annullato.";
        }
        finally
        {
            UploadStatusText = null;
            UploadProgressValue = 0;
            _uploadCts.Dispose();
            _uploadCts = null;
            IsUploading = false;
        }

        // Rientra nella cartella corrente per mostrare i file appena caricati: fuori dal blocco
        // IsUploading, così LoadListingAsync (che guarda anche IsUploading) non si blocca da solo.
        await RefreshAsync();
    }
```

`UploadEntry`/`UploadProgress` arrivano da `FileExplorer.Models`, già importato in cima al file; `SearchOption` da `System.IO`, già importato.

- [ ] **Step 6: Aggiorna il commento del `SuppressMessage` di classe**

Il campo `_uploadCts` ha la stessa natura di `_downloadCts` (creato e distrutto dentro il proprio metodo Run, mai disposable state persistente). Aggiorna il testo esistente dell'attributo `SuppressMessage` sulla classe per menzionare entrambi:

```csharp
[SuppressMessage(
    "Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "I CancellationTokenSource di download e upload sono creati e distrutti dentro " +
                    "RunDownloadAsync/RunUploadAsync: i campi sono solo l'appiglio per Cancel* e restano " +
                    "null fuori dal batch, quindi la viewmodel non ha uno stato disposable da liberare.")]
```

- [ ] **Step 7: Esegui i test e verifica che passino**

Run: `dotnet test FileExplorer.sln --filter FullyQualifiedName~RemoteBrowserUploadTests`
Expected: PASS, tutti i test verdi.

- [ ] **Step 8: Esegui l'intera suite per assicurarti di non aver rotto il download**

Run: `dotnet test FileExplorer.sln`
Expected: PASS, nessuna regressione su `RemoteBrowserDownloadTests`/`RemoteBrowserViewModelTests`/`DownloadServiceTests`.

- [ ] **Step 9: Commit**

```bash
git add FileExplorer/ViewModels/RemoteBrowserViewModel.cs FileExplorer.Tests/RemoteBrowserUploadTests.cs
git commit -m "feat(remote): aggiungi comandi di upload alla RemoteBrowserViewModel"
```

---

### Task 5: UI — bottoni upload, checkbox overwrite, barra progresso

**Model:** `sonnet` (XAML/code-behind, nessun test automatico headless possibile per questa parte)

**Files:**
- Modify: `FileExplorer/Views/RemoteBrowserView.axaml`
- Modify: `FileExplorer/Views/RemoteBrowserView.axaml.cs`

**Interfaces:**
- Consumes: `RemoteBrowserViewModel.UploadFilesAsync`/`UploadFolderAsync`/`CancelUpload`/`IsUploading`/`UploadProgressValue`/`UploadStatusText`/`UploadOverwriteAlways` (Task 4), `SelectPathDialog`/`SelectPathDialogViewModel` (esistenti, stesso contratto usato da `OnBrowseDestinationClick`: costruttore `SelectPathDialogViewModel(bool directoriesOnly, string startPath)`, `dialog.ShowDialog<string?>(owner)` ritorna il percorso scelto o null).
- Produces: niente (ultimo livello).

- [ ] **Step 1: Aggiungi la riga upload nella "Barra download" esistente**

In `FileExplorer/Views/RemoteBrowserView.axaml`, dentro il `<Border DockPanel.Dock="Bottom" ...>` commentato `<!-- Barra download (in basso) -->`, dentro lo `<StackPanel Spacing="8">` che contiene già i due `<Grid>` (cartella destinazione, e progresso/bottoni download), aggiungi un terzo `<Grid>` subito dopo il secondo (dopo la chiusura del `<Grid ColumnDefinitions="*,Auto,Auto,Auto">` dei bottoni download, prima della chiusura `</StackPanel>`):

```xml
        <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto">
          <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
            <ProgressBar Width="220" Minimum="0" Maximum="1" Value="{Binding UploadProgressValue}"
                         IsVisible="{Binding IsUploading}" />
            <TextBlock Text="{Binding UploadStatusText}" VerticalAlignment="Center"
                       Foreground="{DynamicResource Brush.TextMuted}" />
          </StackPanel>
          <CheckBox Grid.Column="1" Content="Sovrascrivi se già presente" IsChecked="{Binding UploadOverwriteAlways}"
                     VerticalAlignment="Center" Margin="8,0,0,0" />
          <Button Grid.Column="2" Classes="primary" Click="OnUploadFilesClick" Margin="8,0,0,0">
            <Button.IsEnabled>
              <MultiBinding Converter="{StaticResource NotAny}">
                <Binding Path="IsBusy" />
                <Binding Path="IsDownloading" />
                <Binding Path="IsUploading" />
              </MultiBinding>
            </Button.IsEnabled>
            <StackPanel Orientation="Horizontal" Spacing="8">
              <i:Icon Value="fa-solid fa-upload" />
              <TextBlock Text="Carica file" />
            </StackPanel>
          </Button>
          <Button Grid.Column="3" Classes="primary" Click="OnUploadFolderClick" Margin="8,0,0,0">
            <Button.IsEnabled>
              <MultiBinding Converter="{StaticResource NotAny}">
                <Binding Path="IsBusy" />
                <Binding Path="IsDownloading" />
                <Binding Path="IsUploading" />
              </MultiBinding>
            </Button.IsEnabled>
            <StackPanel Orientation="Horizontal" Spacing="8">
              <i:Icon Value="fa-solid fa-folder-plus" />
              <TextBlock Text="Carica cartella" />
            </StackPanel>
          </Button>
          <Button Grid.Column="4" Classes="secondary" Content="Annulla" Margin="8,0,0,0"
                  Click="OnCancelUploadClick" IsEnabled="{Binding IsUploading}" />
        </Grid>
```

- [ ] **Step 2: Estendi le guardie `IsEnabled` esistenti con `IsUploading`**

Nello stesso file, in OGNUNO dei seguenti `MultiBinding Converter="{StaticResource NotAny}"` già presenti, aggiungi `<Binding Path="IsUploading" />` come ultima riga del MultiBinding (mantenendo le righe esistenti invariate):

1. Bottone "Elimina profilo" (`OnDeleteProfileClick`) — MultiBinding con `IsBusy`, `IsDownloading`, `SelectedProfile` (converter `IsNull`).
2. Bottone "Connetti" (`OnConnectClick`, visibile se `!IsConnected`).
3. Bottone "Disconnetti" (`OnDisconnectClick`, visibile se `IsConnected`).
4. Bottone "Accedi" nel prompt password (`OnConnectClick`).
5. Bottone "Accetta e connetti" nel banner host key (`OnAcceptFingerprintClick`).
6. Bottone icona "cartella superiore" (`OnNavigateUpClick`).
7. Bottone icona "aggiorna" (`OnRefreshClick`).

- [ ] **Step 3: Disabilita i bottoni di download anche durante l'upload**

Nello stesso file, i due bottoni "Scarica selezionati" (`OnDownloadSelectedClick`) e "Scarica directory" (`OnDownloadDirectoryClick`) attualmente usano `IsEnabled="{Binding !IsDownloading}"` (binding semplice, non MultiBinding). Sostituisci ENTRAMBI con:

```xml
          <Button.IsEnabled>
            <MultiBinding Converter="{StaticResource NotAny}">
              <Binding Path="IsDownloading" />
              <Binding Path="IsUploading" />
            </MultiBinding>
          </Button.IsEnabled>
```

(va dentro l'apertura del rispettivo `<Button ...>`, al posto dell'attributo `IsEnabled="{Binding !IsDownloading}"` rimosso dalla riga di apertura del tag).

- [ ] **Step 4: Aggiungi gli handler nel code-behind**

In `FileExplorer/Views/RemoteBrowserView.axaml.cs`, aggiungi `using System.IO;` in cima (sotto gli altri `using`), poi aggiungi questi tre metodi dopo `OnBrowseDestinationClick`:

```csharp
    private async void OnUploadFilesClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        // Un file alla volta: stesso contratto di SelectPathDialog usato per la destinazione dei
        // download (nessun file picker nativo multi-selezione nell'app). Ripetibile per più file.
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: false,
                startPath: System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);

        // Senza elemento selezionato SelectPathDialog ritorna la cartella corrente: non è un file
        // valido da caricare, va ignorato invece di far fallire l'upload.
        if (!string.IsNullOrWhiteSpace(result) && !Directory.Exists(result))
            await _viewModel.UploadFilesAsync(new[] { result });
    }

    private async void OnUploadFolderClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: true,
                startPath: System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(result))
            await _viewModel.UploadFolderAsync(result);
    }

    private void OnCancelUploadClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelUpload();
```

- [ ] **Step 5: Verifica che la solution compili**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, nessun errore XAML o C#.

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Views/RemoteBrowserView.axaml FileExplorer/Views/RemoteBrowserView.axaml.cs
git commit -m "feat(remote): aggiungi bottoni carica file/cartella e stato upload alla view"
```

---

### Task 6: Verifica finale

**Model:** `sonnet` (interpretare eventuali fallimenti residui e correggerli)

**Files:**
- Nessuno (solo verifica; eventuali fix minimi ai file dei task precedenti se la build/i test falliscono).

**Interfaces:**
- Consumes: tutto quanto prodotto dai Task 1-5.
- Produces: niente.

- [ ] **Step 1: Build completa**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori.

- [ ] **Step 2: Suite di test completa**

Run: `dotnet test FileExplorer.sln`
Expected: PASS, tutti i test verdi (inclusi quelli preesistenti su download — nessuna regressione).

- [ ] **Step 3: Se qualcosa fallisce, correggi e ripeti Step 1-2**

Non procedere oltre finché build e test non sono entrambi verdi.

- [ ] **Step 4: Nota per verifica manuale (non automatizzabile in questo ambiente)**

Questo è un'app desktop Avalonia: la verifica visiva/funzionale reale (connettersi a un server FTP/SFTP di prova, caricare un file, una cartella, verificare lo skip/overwrite e l'annullamento) richiede di lanciare `dotnet run --project FileExplorer.Desktop` con un display grafico disponibile. Segnala questo al termine del task come promemoria per l'utente — non bloccare il task su questo punto.

- [ ] **Step 5: Commit finale (solo se Step 3 ha prodotto modifiche non ancora committate)**

```bash
git add -A
git commit -m "test(remote): verifica finale build e suite upload"
```

Se non ci sono modifiche pendenti, salta questo step.
