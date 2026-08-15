# Remote Browser (FTP/SFTP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. After each completed task, tick its checkboxes in THIS file before starting the next task (CLAUDE.md rule). Each task declares a **Model** — dispatch its subagent with that model.

**Goal:** Nuova tab "Server remoto" che si connette a server FTP/FTPS/SFTP, naviga directory remote e scarica file selezionati o l'intera directory aperta, con filtri e check dei file già presenti su disco.

**Architecture:** Interfaccia unificata `IRemoteFileClient` con implementazioni FluentFTP (FTP/FTPS) e SSH.NET (SFTP); `DownloadService` statico orchestra filtri, check esistenza e report; profili in JSON AppData senza segreti, password nel keyring OS via `ICredentialStore` (backend nativi per OS, nessun NuGet keyring); `RemoteBrowserViewModel` ReactiveUI creato dalla view, shell a 2 tab in MainWindow.

**Tech Stack:** .NET 8 (progetto core), Avalonia 11 + ReactiveUI, FluentFTP, SSH.NET, xunit (test project net10.0).

**Spec:** `docs/superpowers/specs/2026-08-15-remote-browser-design.md`

## Global Constraints

- Branch di lavoro: `feature/remote-browser` (mai commit su `main`; consegna via PR).
- Tutte le stringhe UI in **italiano** (come il resto dell'app).
- Mai colori hardcoded nelle view: solo `{DynamicResource Brush.*}`; icone `fa-*` Projektanker.
- Tutto l'I/O (rete e disco) async con `CancellationToken`; mai I/O sul thread UI.
- Mai eccezioni silenziate: errori classificati (`RemoteError`) e mostrati.
- Password mai su disco né nei log.
- Test: `dotnet test FileExplorer.sln` dalla root; build: `dotnet build FileExplorer.sln`.
- Commit frequenti, messaggi convenzionali, **niente co-author Claude**.
- `dotnet format whitespace` gira in automatico via hook sui file editati: non serve lanciarlo.
- Namespace: modelli in `FileExplorer.Models`, servizi in `FileExplorer.Services`, VM in `FileExplorer.ViewModels`, test in `FileExplorer.Tests`.

### Nota su percorsi remoti

I percorsi remoti usano sempre `/` come separatore. `RemoteItem.FullPath` è il percorso remoto assoluto (es. `/home/user/docs/a.txt`).

---

### Task 1: Modelli remoti

**Model:** `haiku` (dati puri, nessuna logica)

**Files:**
- Create: `FileExplorer/Models/RemoteProtocol.cs`
- Create: `FileExplorer/Models/ConnectionProfile.cs`
- Create: `FileExplorer/Models/RemoteItem.cs`
- Create: `FileExplorer/Models/RemoteError.cs`
- Create: `FileExplorer/Models/RemoteListingResult.cs`
- Create: `FileExplorer/Models/LocalFileStatus.cs`
- Create: `FileExplorer/Models/DownloadReport.cs`

**Interfaces:**
- Consumes: niente.
- Produces: tutti i tipi sotto, usati da ogni task successivo. Copiarli ESATTAMENTE.

- [x] **Step 1: Crea i file dei modelli**

`FileExplorer/Models/RemoteProtocol.cs`:

```csharp
namespace FileExplorer.Models;

/// <summary>Protocollo di connessione a un server remoto.</summary>
public enum RemoteProtocol
{
    Ftp,
    Ftps,
    Sftp
}
```

`FileExplorer/Models/ConnectionProfile.cs`:

```csharp
using System;

namespace FileExplorer.Models;

/// <summary>
/// Profilo di connessione salvato su disco. Non contiene MAI la password:
/// quella vive nel keyring del sistema operativo, indicizzata da <see cref="Id"/>.
/// </summary>
public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.Sftp;

    /// <summary>Ultima cartella di destinazione scelta per i download.</summary>
    public string? LastDestinationFolder { get; set; }

    /// <summary>Fingerprint SHA-256 della host key SFTP accettata dall'utente.</summary>
    public string? AcceptedHostKeyFingerprint { get; set; }
}
```

`FileExplorer/Models/RemoteItem.cs`:

```csharp
using System;

namespace FileExplorer.Models;

/// <summary>Voce (file o cartella) di un elenco remoto. Percorsi con separatore '/'.</summary>
public sealed record RemoteItem(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified);
```

`FileExplorer/Models/RemoteError.cs`:

```csharp
namespace FileExplorer.Models;

/// <summary>Categoria di errore di un'operazione remota.</summary>
public enum RemoteErrorKind
{
    AuthFailed,
    HostUnreachable,
    Timeout,
    PermissionDenied,
    NotFound,
    TransferFailed,

    /// <summary>Host key SFTP sconosciuta o diversa da quella accettata (possibile MITM).</summary>
    HostKeyMismatch
}

/// <summary>
/// Errore remoto con messaggio presentabile. <paramref name="Fingerprint"/> è valorizzata
/// solo per <see cref="RemoteErrorKind.HostKeyMismatch"/> (fingerprint SHA-256 ricevuta).
/// </summary>
public sealed record RemoteError(RemoteErrorKind Kind, string Message, string? Fingerprint = null);
```

`FileExplorer/Models/RemoteListingResult.cs`:

```csharp
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Esito di un elenco remoto: voci trovate ed eventuale errore (elenco vuoto se errore).</summary>
public sealed record RemoteListingResult(IReadOnlyList<RemoteItem> Items, RemoteError? Error);
```

`FileExplorer/Models/LocalFileStatus.cs`:

```csharp
namespace FileExplorer.Models;

/// <summary>Stato di un file remoto rispetto alla cartella locale di destinazione.</summary>
public enum LocalFileStatus
{
    /// <summary>Non esiste in locale.</summary>
    Missing,

    /// <summary>Esiste con stessa dimensione e stessa data (tolleranza 2 s).</summary>
    Present,

    /// <summary>Esiste ma dimensione o data differiscono.</summary>
    Different
}
```

`FileExplorer/Models/DownloadReport.cs`:

```csharp
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>File fallito con motivo presentabile.</summary>
public sealed record DownloadFailure(RemoteItem Item, string Reason);

/// <summary>Avanzamento del batch di download.</summary>
public sealed record DownloadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di download.</summary>
public sealed record DownloadReport(
    IReadOnlyList<RemoteItem> Downloaded,
    IReadOnlyList<RemoteItem> Skipped,
    IReadOnlyList<DownloadFailure> Failed);
```

- [x] **Step 2: Build**

Run: `dotnet build FileExplorer.sln`
Expected: 0 errori, 0 warning.

- [x] **Step 3: Commit**

```bash
git add FileExplorer/Models/
git commit -m "feat(remote): add remote browser data models"
```

---

### Task 2: DownloadFilter con matching

**Model:** `sonnet`

**Files:**
- Create: `FileExplorer/Models/DownloadFilter.cs`
- Create: `FileExplorer.Tests/DownloadFilterTests.cs`

**Interfaces:**
- Consumes: `RemoteItem` (Task 1).
- Produces: `DownloadFilter` con proprietà `NamePattern`, `MinSize`, `MaxSize`, `ModifiedAfter`, `ModifiedBefore`, `OnlyMissing`, `Recursive` e metodo `bool Matches(RemoteItem item)`. `OnlyMissing`/`Recursive` NON sono valutati da `Matches` (servono a `DownloadService`/ViewModel).

- [x] **Step 1: Scrivi i test (falliranno: tipo inesistente)**

`FileExplorer.Tests/DownloadFilterTests.cs`:

```csharp
using FileExplorer.Models;

namespace FileExplorer.Tests;

public sealed class DownloadFilterTests
{
    private static RemoteItem File(string name, long size = 100, DateTime? modified = null) =>
        new(name, "/dir/" + name, IsDirectory: false, size, modified ?? new DateTime(2026, 6, 1, 12, 0, 0));

    [Fact]
    public void Matches_EmptyFilter_MatchesEverything()
    {
        var filter = new DownloadFilter();
        Assert.True(filter.Matches(File("a.txt")));
    }

    [Theory]
    [InlineData("*.jpg", "foto.jpg", true)]
    [InlineData("*.jpg", "foto.png", false)]
    [InlineData("*.JPG", "foto.jpg", true)]           // case-insensitive
    [InlineData("report*", "report_2026.pdf", true)]
    [InlineData("report*", "old_report.pdf", false)]
    [InlineData("*.jpg;*.png", "foto.png", true)]     // pattern multipli separati da ';'
    [InlineData(" *.jpg ; *.png ", "foto.png", true)] // spazi tollerati
    [InlineData("*.jpg;*.png", "doc.pdf", false)]
    public void Matches_NamePattern(string pattern, string fileName, bool expected)
    {
        var filter = new DownloadFilter { NamePattern = pattern };
        Assert.Equal(expected, filter.Matches(File(fileName)));
    }

    [Fact]
    public void Matches_SizeRange()
    {
        var filter = new DownloadFilter { MinSize = 50, MaxSize = 150 };
        Assert.True(filter.Matches(File("a", size: 100)));
        Assert.True(filter.Matches(File("a", size: 50)));   // estremi inclusi
        Assert.True(filter.Matches(File("a", size: 150)));
        Assert.False(filter.Matches(File("a", size: 49)));
        Assert.False(filter.Matches(File("a", size: 151)));
    }

    [Fact]
    public void Matches_DateRange()
    {
        var filter = new DownloadFilter
        {
            ModifiedAfter = new DateTime(2026, 1, 1),
            ModifiedBefore = new DateTime(2026, 12, 31)
        };
        Assert.True(filter.Matches(File("a", modified: new DateTime(2026, 6, 1))));
        Assert.False(filter.Matches(File("a", modified: new DateTime(2025, 6, 1))));
        Assert.False(filter.Matches(File("a", modified: new DateTime(2027, 6, 1))));
    }

    [Fact]
    public void Matches_CombinedCriteria_AllMustPass()
    {
        var filter = new DownloadFilter { NamePattern = "*.jpg", MinSize = 50 };
        Assert.True(filter.Matches(File("a.jpg", size: 100)));
        Assert.False(filter.Matches(File("a.jpg", size: 10)));
        Assert.False(filter.Matches(File("a.png", size: 100)));
    }

    [Fact]
    public void Matches_Directory_IgnoresSizeAndPattern()
    {
        // Le cartelle passano sempre: i filtri si applicano ai file.
        var filter = new DownloadFilter { NamePattern = "*.jpg", MinSize = 1000 };
        var dir = new RemoteItem("sub", "/dir/sub", IsDirectory: true, 0, new DateTime(2026, 6, 1));
        Assert.True(filter.Matches(dir));
    }
}
```

- [x] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter DownloadFilterTests`
Expected: errore di compilazione "DownloadFilter non trovato".

- [x] **Step 3: Implementa**

`FileExplorer/Models/DownloadFilter.cs`:

```csharp
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileExplorer.Models;

/// <summary>
/// Criteri di filtro per i download. <see cref="Matches"/> valuta nome, dimensione e data;
/// <see cref="OnlyMissing"/> e <see cref="Recursive"/> sono gestiti da DownloadService/ViewModel.
/// </summary>
public sealed class DownloadFilter
{
    /// <summary>Pattern wildcard separati da ';' (es. "*.jpg;report*"). Vuoto o null = tutti.</summary>
    public string? NamePattern { get; set; }

    /// <summary>Dimensione minima in byte (inclusa).</summary>
    public long? MinSize { get; set; }

    /// <summary>Dimensione massima in byte (inclusa).</summary>
    public long? MaxSize { get; set; }

    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }

    /// <summary>Scarica solo i file assenti dalla destinazione.</summary>
    public bool OnlyMissing { get; set; }

    /// <summary>Includi le sottocartelle nel download della directory.</summary>
    public bool Recursive { get; set; }

    /// <summary>True se il file passa nome, dimensione e data. Le cartelle passano sempre.</summary>
    public bool Matches(RemoteItem item)
    {
        if (item.IsDirectory)
            return true;

        if (!MatchesName(item.Name))
            return false;

        if (MinSize is not null && item.Size < MinSize)
            return false;

        if (MaxSize is not null && item.Size > MaxSize)
            return false;

        if (ModifiedAfter is not null && item.Modified < ModifiedAfter)
            return false;

        if (ModifiedBefore is not null && item.Modified > ModifiedBefore)
            return false;

        return true;
    }

    private bool MatchesName(string name)
    {
        if (string.IsNullOrWhiteSpace(NamePattern))
            return true;

        return NamePattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(pattern => Regex.IsMatch(
                name,
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase));
    }
}
```

- [x] **Step 4: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter DownloadFilterTests`
Expected: tutti PASS.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Models/DownloadFilter.cs FileExplorer.Tests/DownloadFilterTests.cs
git commit -m "feat(remote): download filter with wildcard, size and date matching"
```

---

### Task 3: Check esistenza su disco (GetLocalStatus)

**Model:** `sonnet`

**Files:**
- Create: `FileExplorer/Services/DownloadService.cs` (solo `GetLocalStatus`; il resto arriva nel Task 6)
- Create: `FileExplorer.Tests/DownloadServiceStatusTests.cs`

**Interfaces:**
- Consumes: `RemoteItem`, `LocalFileStatus` (Task 1).
- Produces: `DownloadService.GetLocalStatus(RemoteItem item, string localPath)` → `LocalFileStatus` (statico). Tolleranza data: 2 secondi.

- [x] **Step 1: Scrivi i test**

`FileExplorer.Tests/DownloadServiceStatusTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DownloadServiceStatusTests : IDisposable
{
    private readonly string _root;

    public DownloadServiceStatusTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreateLocalFile(string name, string content, DateTime modified)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, modified);
        return path;
    }

    private static RemoteItem Remote(string name, long size, DateTime modified) =>
        new(name, "/srv/" + name, IsDirectory: false, size, modified);

    [Fact]
    public void GetLocalStatus_FileDoesNotExist_Missing()
    {
        var status = DownloadService.GetLocalStatus(
            Remote("a.txt", 5, DateTime.Now), Path.Combine(_root, "a.txt"));
        Assert.Equal(LocalFileStatus.Missing, status);
    }

    [Fact]
    public void GetLocalStatus_SameSizeAndDate_Present()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified), path);
        Assert.Equal(LocalFileStatus.Present, status);
    }

    [Fact]
    public void GetLocalStatus_DateWithinTwoSeconds_Present()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified.AddSeconds(2)), path);
        Assert.Equal(LocalFileStatus.Present, status);
    }

    [Fact]
    public void GetLocalStatus_DifferentSize_Different()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 999, modified), path);
        Assert.Equal(LocalFileStatus.Different, status);
    }

    [Fact]
    public void GetLocalStatus_DifferentDate_Different()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified.AddMinutes(5)), path);
        Assert.Equal(LocalFileStatus.Different, status);
    }
}
```

- [x] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter DownloadServiceStatusTests`
Expected: errore di compilazione "DownloadService non trovato".

- [x] **Step 3: Implementa**

`FileExplorer/Services/DownloadService.cs`:

```csharp
using System;
using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Orchestrazione dei download remoti: check di esistenza locale, filtri, batch e report.
/// </summary>
public static class DownloadService
{
    /// <summary>Tolleranza sul confronto delle date di modifica (timestamp FTP poco precisi).</summary>
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Stato del file remoto rispetto a <paramref name="localPath"/>:
    /// Present se dimensione uguale e data entro la tolleranza, Different altrimenti.
    /// </summary>
    public static LocalFileStatus GetLocalStatus(RemoteItem item, string localPath)
    {
        var info = new FileInfo(localPath);
        if (!info.Exists)
            return LocalFileStatus.Missing;

        bool sameSize = info.Length == item.Size;
        bool sameDate = (info.LastWriteTime - item.Modified).Duration() <= DateTolerance;

        return sameSize && sameDate ? LocalFileStatus.Present : LocalFileStatus.Different;
    }
}
```

- [x] **Step 4: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter DownloadServiceStatusTests`
Expected: tutti PASS.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/DownloadService.cs FileExplorer.Tests/DownloadServiceStatusTests.cs
git commit -m "feat(remote): local file status check (missing/present/different)"
```

---

### Task 4: ProfileStore (persistenza JSON)

**Model:** `sonnet`

**Files:**
- Create: `FileExplorer/Services/ProfileStore.cs`
- Create: `FileExplorer.Tests/ProfileStoreTests.cs`

**Interfaces:**
- Consumes: `ConnectionProfile` (Task 1).
- Produces:
  - `ProfileStore.DefaultPath` → `string` (percorso JSON in AppData)
  - `Task<List<ConnectionProfile>> ProfileStore.LoadAsync(string path)` (lista vuota se file assente o corrotto)
  - `Task ProfileStore.SaveAsync(string path, IReadOnlyList<ConnectionProfile> profiles)`

- [x] **Step 1: Scrivi i test**

`FileExplorer.Tests/ProfileStoreTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root;

    public ProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string StorePath => Path.Combine(_root, "sub", "profiles.json");

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        var profiles = await ProfileStore.LoadAsync(StorePath);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsAllFields()
    {
        var profile = new ConnectionProfile
        {
            Name = "NAS",
            Host = "nas.local",
            Port = 2222,
            Username = "utente",
            Protocol = RemoteProtocol.Sftp,
            LastDestinationFolder = "/tmp/dl",
            AcceptedHostKeyFingerprint = "SHA256:abc"
        };

        await ProfileStore.SaveAsync(StorePath, new[] { profile });
        var loaded = await ProfileStore.LoadAsync(StorePath);

        var round = Assert.Single(loaded);
        Assert.Equal(profile.Id, round.Id);
        Assert.Equal("NAS", round.Name);
        Assert.Equal("nas.local", round.Host);
        Assert.Equal(2222, round.Port);
        Assert.Equal("utente", round.Username);
        Assert.Equal(RemoteProtocol.Sftp, round.Protocol);
        Assert.Equal("/tmp/dl", round.LastDestinationFolder);
        Assert.Equal("SHA256:abc", round.AcceptedHostKeyFingerprint);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, "{ non-json !!!");

        var profiles = await ProfileStore.LoadAsync(StorePath);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_NeverWritesPasswordProperty()
    {
        await ProfileStore.SaveAsync(StorePath, new[] { new ConnectionProfile { Name = "x" } });
        string json = await File.ReadAllTextAsync(StorePath);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [x] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter ProfileStoreTests`
Expected: errore di compilazione "ProfileStore non trovato".

- [x] **Step 3: Implementa**

`FileExplorer/Services/ProfileStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza dei profili di connessione in JSON (AppData). Il file non contiene
/// mai password: quelle vivono nel keyring del sistema operativo.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Percorso predefinito del file profili.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "profiles.json");

    /// <summary>Carica i profili; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<ConnectionProfile>> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new List<ConnectionProfile>();

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(stream, Options)
                   ?? new List<ConnectionProfile>();
        }
        catch (Exception)
        {
            // File corrotto o inaccessibile: si riparte da zero, i profili sono ricreabili.
            return new List<ConnectionProfile>();
        }
    }

    /// <summary>Salva i profili creando la cartella se assente.</summary>
    public static async Task SaveAsync(string path, IReadOnlyList<ConnectionProfile> profiles)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profiles, Options);
    }
}
```

- [x] **Step 4: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter ProfileStoreTests`
Expected: tutti PASS.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/ProfileStore.cs FileExplorer.Tests/ProfileStoreTests.cs
git commit -m "feat(remote): connection profile JSON persistence"
```

---

### Task 5: ICredentialStore e backend keyring OS

**Model:** `opus` (P/Invoke e processi esterni, codice security-sensitive)

**Files:**
- Create: `FileExplorer/Services/ICredentialStore.cs`
- Create: `FileExplorer/Services/WindowsCredentialStore.cs`
- Create: `FileExplorer/Services/SecretToolCredentialStore.cs`
- Create: `FileExplorer/Services/MacKeychainCredentialStore.cs`
- Create: `FileExplorer/Services/NullCredentialStore.cs`
- Create: `FileExplorer/Services/CredentialStoreFactory.cs`
- Create: `FileExplorer.Tests/CredentialStoreFactoryTests.cs`

**Interfaces:**
- Consumes: niente dai task precedenti (usa `System.Guid`).
- Produces:

```csharp
public interface ICredentialStore
{
    bool IsAvailable { get; }
    Task<string?> GetPasswordAsync(Guid profileId);
    Task SetPasswordAsync(Guid profileId, string password);
    Task DeletePasswordAsync(Guid profileId);
}
```

  e `CredentialStoreFactory.Create()` → `ICredentialStore` (backend per OS; `NullCredentialStore` con `IsAvailable == false` se nessun keyring).

**Nota security (leggere con attenzione):** la password non deve MAI finire su disco, nei log, o come argomento visibile di riga di comando dove evitabile. Su Linux la password passa a `secret-tool` via stdin. Su macOS `security add-generic-password -w <pw>` espone brevemente la password nella process list: limite noto e documentato nel codice; macOS è best-effort in questo progetto.

- [x] **Step 1: Scrivi i test (solo componenti testabili senza keyring reale)**

`FileExplorer.Tests/CredentialStoreFactoryTests.cs`:

```csharp
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CredentialStoreFactoryTests
{
    [Fact]
    public void Create_ReturnsNonNullStore()
    {
        var store = CredentialStoreFactory.Create();
        Assert.NotNull(store);
    }

    [Fact]
    public async Task NullCredentialStore_IsUnavailable_AndReturnsNoPassword()
    {
        var store = new NullCredentialStore();
        Assert.False(store.IsAvailable);
        Assert.Null(await store.GetPasswordAsync(Guid.NewGuid()));
        // Set e Delete non devono lanciare: sono no-op.
        await store.SetPasswordAsync(Guid.NewGuid(), "x");
        await store.DeletePasswordAsync(Guid.NewGuid());
    }
}
```

- [x] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter CredentialStoreFactoryTests`
Expected: errore di compilazione.

- [x] **Step 3: Implementa interfaccia, null store e factory**

`FileExplorer/Services/ICredentialStore.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Accesso al keyring del sistema operativo per le password dei profili.
/// Chiave logica: servizio "FileExplorer" + Guid del profilo.
/// </summary>
public interface ICredentialStore
{
    /// <summary>False se sul sistema non c'è un keyring utilizzabile.</summary>
    bool IsAvailable { get; }

    /// <summary>Password salvata per il profilo, o null se assente.</summary>
    Task<string?> GetPasswordAsync(Guid profileId);

    Task SetPasswordAsync(Guid profileId, string password);

    Task DeletePasswordAsync(Guid profileId);
}
```

`FileExplorer/Services/NullCredentialStore.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Backend usato quando nessun keyring è disponibile: la password viene chiesta
/// a ogni connessione e mai salvata (nessun fallback su file, per scelta di design).
/// </summary>
public sealed class NullCredentialStore : ICredentialStore
{
    public bool IsAvailable => false;

    public Task<string?> GetPasswordAsync(Guid profileId) => Task.FromResult<string?>(null);

    public Task SetPasswordAsync(Guid profileId, string password) => Task.CompletedTask;

    public Task DeletePasswordAsync(Guid profileId) => Task.CompletedTask;
}
```

`FileExplorer/Services/CredentialStoreFactory.cs`:

```csharp
using System;

namespace FileExplorer.Services;

/// <summary>Sceglie il backend keyring adatto al sistema operativo corrente.</summary>
public static class CredentialStoreFactory
{
    public static ICredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialStore();

        if (OperatingSystem.IsMacOS())
        {
            var mac = new MacKeychainCredentialStore();
            return mac.IsAvailable ? mac : new NullCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            var linux = new SecretToolCredentialStore();
            return linux.IsAvailable ? linux : new NullCredentialStore();
        }

        return new NullCredentialStore();
    }
}
```

- [x] **Step 4: Implementa il backend Linux (secret-tool / libsecret)**

`FileExplorer/Services/SecretToolCredentialStore.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring Linux via CLI 'secret-tool' (libsecret: GNOME Keyring, KWallet con bridge).
/// La password passa esclusivamente via stdin, mai come argomento.
/// </summary>
public sealed class SecretToolCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    public bool IsAvailable { get; } = ProbeSecretTool();

    private static bool ProbeSecretTool()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                ArgumentList = { "lookup", "service", "FileExplorer", "probe", "probe" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process!.WaitForExit(3000);
            // Exit code 1 = "non trovato" ma il tool e il keyring funzionano.
            return process.HasExited && process.ExitCode is 0 or 1;
        }
        catch (Exception)
        {
            return false; // secret-tool assente o keyring non attivo.
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "lookup", "service", Service, "profile", profileId.ToString("N") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        string output = await process!.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? output.TrimEnd('\n') : null;
    }

    public async Task SetPasswordAsync(Guid profileId, string password)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList =
            {
                "store", "--label", $"FileExplorer {profileId:N}",
                "service", Service, "profile", profileId.ToString("N")
            },
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.StandardInput.WriteAsync(password);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "secret-tool",
            ArgumentList = { "clear", "service", Service, "profile", profileId.ToString("N") },
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }
}
```

- [x] **Step 5: Implementa il backend Windows (Credential Manager, P/Invoke)**

`FileExplorer/Services/WindowsCredentialStore.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Keyring Windows via Credential Manager (advapi32, credenziali generiche).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public bool IsAvailable => true;

    private static string TargetName(Guid profileId) => $"FileExplorer/{profileId:N}";

    public Task<string?> GetPasswordAsync(Guid profileId) => Task.Run(() =>
    {
        if (!CredRead(TargetName(profileId), CredTypeGeneric, 0, out IntPtr credentialPtr))
            return (string?)null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;

            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    });

    public Task SetPasswordAsync(Guid profileId, string password) => Task.Run(() =>
    {
        byte[] blob = Encoding.UTF8.GetBytes(password);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName(profileId),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException(
                    $"Scrittura nel Credential Manager fallita (errore {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    });

    public Task DeletePasswordAsync(Guid profileId) => Task.Run(() =>
    {
        CredDelete(TargetName(profileId), CredTypeGeneric, 0);
    });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
```

- [x] **Step 6: Implementa il backend macOS (security CLI)**

`FileExplorer/Services/MacKeychainCredentialStore.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Keyring macOS via CLI 'security' (Keychain). Limite noto: 'add-generic-password -w'
/// espone brevemente la password nella process list; macOS è best-effort in questo progetto.
/// </summary>
public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string Service = "FileExplorer";

    public bool IsAvailable { get; } = ProbeSecurityCli();

    private static bool ProbeSecurityCli()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "security",
                ArgumentList = { "help" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process!.WaitForExit(3000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetPasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList =
            {
                "find-generic-password", "-a", profileId.ToString("N"), "-s", Service, "-w"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        string output = await process!.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? output.TrimEnd('\n') : null;
    }

    public async Task SetPasswordAsync(Guid profileId, string password)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList =
            {
                "add-generic-password", "-U",
                "-a", profileId.ToString("N"), "-s", Service, "-w", password
            },
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }

    public async Task DeletePasswordAsync(Guid profileId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            ArgumentList = { "delete-generic-password", "-a", profileId.ToString("N"), "-s", Service },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        await process!.WaitForExitAsync();
    }
}
```

- [x] **Step 7: Build e test**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln --filter CredentialStoreFactoryTests`
Expected: build pulita, test PASS. (I backend reali si verificano manualmente nel Task 12.)

- [x] **Step 8: Commit**

```bash
git add FileExplorer/Services/ICredentialStore.cs FileExplorer/Services/WindowsCredentialStore.cs FileExplorer/Services/SecretToolCredentialStore.cs FileExplorer/Services/MacKeychainCredentialStore.cs FileExplorer/Services/NullCredentialStore.cs FileExplorer/Services/CredentialStoreFactory.cs FileExplorer.Tests/CredentialStoreFactoryTests.cs
git commit -m "feat(remote): OS keyring credential store (Credential Manager, libsecret, Keychain)"
```

---

### Task 6: IRemoteFileClient, FakeRemoteClient e DownloadService.DownloadAsync

**Model:** `opus` (logica core: filtri, skip, ricorsione, cancellazione, report)

**Files:**
- Create: `FileExplorer/Services/IRemoteFileClient.cs`
- Modify: `FileExplorer/Services/DownloadService.cs` (aggiungi `DownloadAsync` e `GetRelativeLocalPath` alla classe del Task 3)
- Create: `FileExplorer.Tests/FakeRemoteClient.cs`
- Create: `FileExplorer.Tests/DownloadServiceTests.cs`

**Interfaces:**
- Consumes: tutti i modelli (Task 1), `DownloadFilter.Matches` (Task 2), `DownloadService.GetLocalStatus` (Task 3).
- Produces:

```csharp
public interface IRemoteFileClient : IAsyncDisposable
{
    bool IsConnected { get; }
    Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct);
    Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct);
    Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct);
    Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct);
}
```

  e in `DownloadService`:

```csharp
public static string GetRelativeLocalPath(RemoteItem item, string remoteBasePath);
public static Task<DownloadReport> DownloadAsync(
    IRemoteFileClient client, IReadOnlyList<RemoteItem> files, string remoteBasePath,
    string destinationFolder, DownloadFilter filter, bool overwriteAlways,
    IProgress<DownloadProgress>? progress, CancellationToken ct);
```

  `FakeRemoteClient` (nel progetto test) usato anche dai task 9-11.

Regole di `DownloadAsync` (contratto, da coprire nei test):
1. Solo file (le directory nella lista vengono ignorate).
2. `filter.Matches` falso → file in `Skipped`.
3. Percorso locale = `destinationFolder` + `GetRelativeLocalPath` (sottocartelle ricreate).
4. `filter.OnlyMissing` e stato ≠ `Missing` → `Skipped`.
5. Senza `overwriteAlways`, stato `Present` → `Skipped`; stato `Different` → scaricato (sovrascritto).
6. Con `overwriteAlways` → scaricato anche se `Present`.
7. Errore su un file → `Failed` con motivo, il batch continua.
8. Cancellazione → `OperationCanceledException` propagata e file parziale eliminato.

- [x] **Step 1: Scrivi interfaccia e fake**

`FileExplorer/Services/IRemoteFileClient.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Client unificato verso un server remoto (FTP/FTPS/SFTP). Gli errori sono
/// ritornati come <see cref="RemoteError"/>, mai lanciati come eccezioni
/// (eccetto <see cref="OperationCanceledException"/> su annullamento).
/// </summary>
public interface IRemoteFileClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Connette e autentica. Null = successo.</summary>
    Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct);

    /// <summary>Elenco del contenuto diretto di <paramref name="path"/> remoto.</summary>
    Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Elenco ricorsivo dei soli file sotto <paramref name="path"/> remoto.</summary>
    Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct);

    /// <summary>Scarica un file remoto su <paramref name="localPath"/>. Null = successo.</summary>
    Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct);
}
```

`FileExplorer.Tests/FakeRemoteClient.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

/// <summary>
/// Client remoto in-memory per i test: file simulati per percorso, errori
/// configurabili per connessione e per singolo download.
/// </summary>
public sealed class FakeRemoteClient : IRemoteFileClient
{
    /// <summary>Percorso remoto → (voce, contenuto). Le directory hanno contenuto vuoto.</summary>
    public Dictionary<string, (RemoteItem Item, byte[] Content)> Entries { get; } = new();

    /// <summary>Errore da ritornare alla connessione (null = successo).</summary>
    public RemoteError? ConnectError { get; set; }

    /// <summary>Percorsi remoti il cui download deve fallire.</summary>
    public HashSet<string> FailingDownloads { get; } = new();

    public bool IsConnected { get; private set; }

    public void AddFile(string fullPath, string content, DateTime? modified = null)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        string name = fullPath[(fullPath.LastIndexOf('/') + 1)..];
        var item = new RemoteItem(name, fullPath, IsDirectory: false, bytes.Length,
            modified ?? new DateTime(2026, 6, 1, 12, 0, 0));
        Entries[fullPath] = (item, bytes);
    }

    public void AddDirectory(string fullPath)
    {
        string name = fullPath.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var item = new RemoteItem(name, fullPath, IsDirectory: true, 0, new DateTime(2026, 6, 1));
        Entries[fullPath] = (item, Array.Empty<byte>());
    }

    public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        IsConnected = ConnectError is null;
        return Task.FromResult(ConnectError);
    }

    public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
    {
        string prefix = path.TrimEnd('/') + "/";
        var items = Entries.Values
            .Select(e => e.Item)
            .Where(i => i.FullPath.StartsWith(prefix, StringComparison.Ordinal)
                        && !i.FullPath[prefix.Length..].TrimEnd('/').Contains('/'))
            .OrderBy(i => i.FullPath)
            .ToList();
        return Task.FromResult(new RemoteListingResult(items, null));
    }

    public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
    {
        string prefix = path.TrimEnd('/') + "/";
        var items = Entries.Values
            .Select(e => e.Item)
            .Where(i => !i.IsDirectory && i.FullPath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(i => i.FullPath)
            .ToList();
        return Task.FromResult(new RemoteListingResult(items, null));
    }

    public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingDownloads.Contains(item.FullPath))
            return new RemoteError(RemoteErrorKind.TransferFailed, "Trasferimento fallito (simulato).");

        if (!Entries.TryGetValue(item.FullPath, out var entry))
            return new RemoteError(RemoteErrorKind.NotFound, "File remoto inesistente.");

        await File.WriteAllBytesAsync(localPath, entry.Content, ct);
        File.SetLastWriteTime(localPath, entry.Item.Modified);
        progress?.Report(entry.Content.Length);
        return null;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
```

- [x] **Step 2: Scrivi i test di DownloadAsync**

`FileExplorer.Tests/DownloadServiceTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DownloadServiceTests : IDisposable
{
    private readonly string _dest;
    private readonly FakeRemoteClient _client = new();

    public DownloadServiceTests()
    {
        _dest = Path.Combine(Path.GetTempPath(), "fe-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dest, recursive: true); } catch { /* best effort */ }
    }

    private Task<DownloadReport> RunAsync(
        IReadOnlyList<RemoteItem> files,
        DownloadFilter? filter = null,
        bool overwriteAlways = false,
        CancellationToken ct = default) =>
        DownloadService.DownloadAsync(
            _client, files, "/srv", _dest, filter ?? new DownloadFilter(),
            overwriteAlways, progress: null, ct);

    private IReadOnlyList<RemoteItem> AllRemoteFiles() =>
        _client.Entries.Values.Select(e => e.Item).Where(i => !i.IsDirectory).ToList();

    [Fact]
    public void GetRelativeLocalPath_StripsBaseAndConvertsSeparators()
    {
        var item = new RemoteItem("c.txt", "/srv/sub/c.txt", false, 1, DateTime.Now);
        string expected = Path.Combine("sub", "c.txt");
        Assert.Equal(expected, DownloadService.GetRelativeLocalPath(item, "/srv"));
    }

    [Fact]
    public void GetRelativeLocalPath_OutsideBase_FallsBackToName()
    {
        var item = new RemoteItem("c.txt", "/altro/c.txt", false, 1, DateTime.Now);
        Assert.Equal("c.txt", DownloadService.GetRelativeLocalPath(item, "/srv"));
    }

    [Fact]
    public async Task DownloadAsync_DownloadsMissingFiles()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        _client.AddFile("/srv/b.txt", "BBB");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Equal(2, report.Downloaded.Count);
        Assert.Empty(report.Skipped);
        Assert.Empty(report.Failed);
        Assert.Equal("AAA", await File.ReadAllTextAsync(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task DownloadAsync_RecreatesSubfolders()
    {
        _client.AddFile("/srv/sub/deep/c.txt", "CCC");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Equal("CCC", await File.ReadAllTextAsync(Path.Combine(_dest, "sub", "deep", "c.txt")));
    }

    [Fact]
    public async Task DownloadAsync_SkipsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/srv/a.txt", "AAA", modified);
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "AAA");
        File.SetLastWriteTime(Path.Combine(_dest, "a.txt"), modified);

        var report = await RunAsync(AllRemoteFiles());

        Assert.Empty(report.Downloaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_OverwritesDifferentFiles()
    {
        _client.AddFile("/srv/a.txt", "NUOVO CONTENUTO");
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "vecchio");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Equal("NUOVO CONTENUTO", await File.ReadAllTextAsync(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task DownloadAsync_OverwriteAlways_DownloadsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/srv/a.txt", "AAA", modified);
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "AAA");
        File.SetLastWriteTime(Path.Combine(_dest, "a.txt"), modified);

        var report = await RunAsync(AllRemoteFiles(), overwriteAlways: true);

        Assert.Single(report.Downloaded);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_OnlyMissing_SkipsDifferentToo()
    {
        _client.AddFile("/srv/a.txt", "NUOVO");
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "vecchio diverso");

        var report = await RunAsync(AllRemoteFiles(), new DownloadFilter { OnlyMissing = true });

        Assert.Empty(report.Downloaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_FilterExcluded_GoesToSkipped()
    {
        _client.AddFile("/srv/a.jpg", "IMG");
        _client.AddFile("/srv/b.txt", "TXT");

        var report = await RunAsync(AllRemoteFiles(), new DownloadFilter { NamePattern = "*.jpg" });

        Assert.Single(report.Downloaded);
        Assert.Single(report.Skipped);
        Assert.Equal("a.jpg", report.Downloaded[0].Name);
    }

    [Fact]
    public async Task DownloadAsync_FailedFile_DoesNotStopBatch()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        _client.AddFile("/srv/b.txt", "BBB");
        _client.FailingDownloads.Add("/srv/a.txt");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Single(report.Failed);
        Assert.Equal("a.txt", report.Failed[0].Item.Name);
        Assert.False(string.IsNullOrWhiteSpace(report.Failed[0].Reason));
    }

    [Fact]
    public async Task DownloadAsync_IgnoresDirectoriesInList()
    {
        _client.AddDirectory("/srv/sub");
        _client.AddFile("/srv/a.txt", "AAA");
        var all = _client.Entries.Values.Select(e => e.Item).ToList();

        var report = await RunAsync(all);

        Assert.Single(report.Downloaded);
    }

    [Fact]
    public async Task DownloadAsync_Cancellation_Throws()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(AllRemoteFiles(), ct: cts.Token));
    }
}
```

- [x] **Step 3: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter DownloadServiceTests`
Expected: errore di compilazione (`DownloadAsync` inesistente).

- [x] **Step 4: Implementa DownloadAsync**

Aggiungi a `FileExplorer/Services/DownloadService.cs` (dentro la classe esistente; aggiorna gli using: servono anche `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`):

```csharp
    /// <summary>
    /// Percorso locale relativo del file: FullPath meno il prefisso <paramref name="remoteBasePath"/>,
    /// con separatori convertiti. Se il file è fuori dalla base, solo il nome.
    /// </summary>
    public static string GetRelativeLocalPath(RemoteItem item, string remoteBasePath)
    {
        string basePrefix = remoteBasePath.TrimEnd('/') + "/";
        if (!item.FullPath.StartsWith(basePrefix, StringComparison.Ordinal))
            return item.Name;

        string relative = item.FullPath[basePrefix.Length..];
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Scarica in sequenza i file della lista applicando filtro e check di esistenza.
    /// Un errore su un file non interrompe il batch; l'annullamento sì (file parziale rimosso).
    /// </summary>
    public static async Task<DownloadReport> DownloadAsync(
        IRemoteFileClient client,
        IReadOnlyList<RemoteItem> files,
        string remoteBasePath,
        string destinationFolder,
        DownloadFilter filter,
        bool overwriteAlways,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var downloaded = new List<RemoteItem>();
        var skipped = new List<RemoteItem>();
        var failed = new List<DownloadFailure>();

        var candidates = new List<RemoteItem>();
        foreach (var item in files)
        {
            if (item.IsDirectory)
                continue;
            candidates.Add(item);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = candidates[i];
            progress?.Report(new DownloadProgress(i + 1, candidates.Count, item.Name, 0));

            if (!filter.Matches(item))
            {
                skipped.Add(item);
                continue;
            }

            string localPath = Path.Combine(destinationFolder, GetRelativeLocalPath(item, remoteBasePath));
            var status = GetLocalStatus(item, localPath);

            if (filter.OnlyMissing && status != LocalFileStatus.Missing)
            {
                skipped.Add(item);
                continue;
            }

            if (!overwriteAlways && status == LocalFileStatus.Present)
            {
                skipped.Add(item);
                continue;
            }

            string? directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var byteProgress = progress is null
                ? null
                : new Progress<long>(bytes =>
                    progress.Report(new DownloadProgress(i + 1, candidates.Count, item.Name, bytes)));

            try
            {
                var error = await client.DownloadFileAsync(item, localPath, byteProgress, ct);
                if (error is null)
                {
                    downloaded.Add(item);
                }
                else
                {
                    DeletePartialFile(localPath);
                    failed.Add(new DownloadFailure(item, error.Message));
                }
            }
            catch (OperationCanceledException)
            {
                DeletePartialFile(localPath);
                throw;
            }
        }

        return new DownloadReport(downloaded, skipped, failed);
    }

    private static void DeletePartialFile(string localPath)
    {
        try
        {
            if (File.Exists(localPath))
                File.Delete(localPath);
        }
        catch (IOException)
        {
            // Pulizia best effort: un parziale non eliminabile non deve mascherare l'errore originale.
        }
    }
```

- [x] **Step 5: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter "DownloadServiceTests|DownloadServiceStatusTests"`
Expected: tutti PASS.

- [x] **Step 6: Commit**

```bash
git add FileExplorer/Services/IRemoteFileClient.cs FileExplorer/Services/DownloadService.cs FileExplorer.Tests/FakeRemoteClient.cs FileExplorer.Tests/DownloadServiceTests.cs
git commit -m "feat(remote): unified remote client interface and download orchestration"
```

---

### Task 7: FtpRemoteClient (FluentFTP) + RemoteClientFactory

**Model:** `sonnet`

**Files:**
- Modify: `FileExplorer/FileExplorer.csproj` (aggiungi PackageReference)
- Create: `FileExplorer/Services/FtpRemoteClient.cs`
- Create: `FileExplorer/Services/RemoteClientFactory.cs`

**Interfaces:**
- Consumes: `IRemoteFileClient`, modelli Task 1.
- Produces: `FtpRemoteClient : IRemoteFileClient` (gestisce `RemoteProtocol.Ftp` e `Ftps`); `RemoteClientFactory.Create(ConnectionProfile profile)` → `IRemoteFileClient` (per ora: Ftp/Ftps → `FtpRemoteClient`; Sftp → `NotSupportedException`, sostituita nel Task 8).

Nessun test automatico (serve un server FTP reale): verifica = build pulita + smoke test manuale nel Task 12.

- [x] **Step 1: Aggiungi FluentFTP**

Run: `dotnet add FileExplorer/FileExplorer.csproj package FluentFTP`
Expected: PackageReference aggiunta, restore ok.

- [x] **Step 2: Implementa FtpRemoteClient**

`FileExplorer/Services/FtpRemoteClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileExplorer.Services;

/// <summary>Client FTP/FTPS basato su FluentFTP.</summary>
public sealed class FtpRemoteClient : IRemoteFileClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private AsyncFtpClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        try
        {
            _client = new AsyncFtpClient(profile.Host, profile.Username, password, profile.Port);
            _client.Config.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
            _client.Config.EncryptionMode = profile.Protocol == RemoteProtocol.Ftps
                ? FtpEncryptionMode.Explicit
                : FtpEncryptionMode.None;

            await _client.Connect(ct);
            return null;
        }
        catch (Exception ex)
        {
            _client = null;
            return TranslateError(ex);
        }
    }

    public async Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
    {
        if (_client is null)
            return NotConnectedResult();

        try
        {
            var listing = await _client.GetListing(path, ct);
            var items = new List<RemoteItem>();
            foreach (var entry in listing)
            {
                if (entry.Type is not (FtpObjectType.File or FtpObjectType.Directory))
                    continue;
                items.Add(new RemoteItem(
                    entry.Name,
                    entry.FullName,
                    entry.Type == FtpObjectType.Directory,
                    entry.Size < 0 ? 0 : entry.Size,
                    entry.Modified));
            }
            return new RemoteListingResult(items, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RemoteListingResult(Array.Empty<RemoteItem>(), TranslateError(ex));
        }
    }

    public async Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
    {
        if (_client is null)
            return NotConnectedResult();

        try
        {
            var listing = await _client.GetListing(path, FtpListOption.Recursive, ct);
            var items = new List<RemoteItem>();
            foreach (var entry in listing)
            {
                if (entry.Type != FtpObjectType.File)
                    continue;
                items.Add(new RemoteItem(entry.Name, entry.FullName, false,
                    entry.Size < 0 ? 0 : entry.Size, entry.Modified));
            }
            return new RemoteListingResult(items, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RemoteListingResult(Array.Empty<RemoteItem>(), TranslateError(ex));
        }
    }

    public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

        try
        {
            var ftpProgress = progress is null
                ? null
                : new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes));

            var status = await _client.DownloadFile(localPath, item.FullPath,
                FtpLocalExists.Overwrite, progress: ftpProgress, token: ct);

            return status == FtpStatus.Success
                ? null
                : new RemoteError(RemoteErrorKind.TransferFailed, $"Download di {item.Name} non riuscito.");
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

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { await _client.Disconnect(); } catch (Exception) { /* già disconnesso */ }
            _client.Dispose();
            _client = null;
        }
    }

    private static RemoteListingResult NotConnectedResult() =>
        new(Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso."));

    private static RemoteError TranslateError(Exception ex) => ex switch
    {
        FtpAuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita: utente o password errati."),
        FtpSecurityNotAvailableException or AuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, "Il server non supporta la cifratura richiesta (FTPS)."),
        FtpMissingObjectException =>
            new RemoteError(RemoteErrorKind.NotFound, "Percorso remoto inesistente."),
        FtpCommandException cmd when cmd.CompletionCode == "550" =>
            new RemoteError(RemoteErrorKind.PermissionDenied, "Permesso negato dal server."),
        TimeoutException =>
            new RemoteError(RemoteErrorKind.Timeout, "Timeout di connessione al server."),
        SocketException =>
            new RemoteError(RemoteErrorKind.HostUnreachable, "Server non raggiungibile."),
        _ => new RemoteError(RemoteErrorKind.TransferFailed, ex.Message)
    };
}
```

Nota per l'implementatore: i nomi delle API FluentFTP sopra sono per la serie 52.x
(`AsyncFtpClient`, `GetListing`, `DownloadFile`, `FtpObjectType`). Se la versione
installata differisce, adegua i nomi consultando il pacchetto installato — l'interfaccia
`IRemoteFileClient` e la mappatura errori NON cambiano.

- [x] **Step 3: Implementa RemoteClientFactory**

`FileExplorer/Services/RemoteClientFactory.cs`:

```csharp
using System;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>Crea il client giusto per il protocollo del profilo.</summary>
public static class RemoteClientFactory
{
    public static IRemoteFileClient Create(ConnectionProfile profile) => profile.Protocol switch
    {
        RemoteProtocol.Ftp or RemoteProtocol.Ftps => new FtpRemoteClient(),
        RemoteProtocol.Sftp => throw new NotSupportedException("SFTP arriva nel task successivo."),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
}
```

- [x] **Step 4: Build e test completi**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln`
Expected: build pulita, tutti i test PASS.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/FileExplorer.csproj FileExplorer/Services/FtpRemoteClient.cs FileExplorer/Services/RemoteClientFactory.cs
git commit -m "feat(remote): FTP/FTPS client via FluentFTP"
```

---

### Task 8: SftpRemoteClient (SSH.NET) con verifica host key

**Model:** `opus` (verifica fingerprint = codice security-sensitive)

**Files:**
- Modify: `FileExplorer/FileExplorer.csproj` (aggiungi PackageReference SSH.NET)
- Create: `FileExplorer/Services/SftpRemoteClient.cs`
- Modify: `FileExplorer/Services/RemoteClientFactory.cs` (sostituisci il ramo Sftp)

**Interfaces:**
- Consumes: `IRemoteFileClient`, modelli Task 1.
- Produces: `SftpRemoteClient : IRemoteFileClient`. Contratto host key:
  - `profile.AcceptedHostKeyFingerprint == null` → `ConnectAsync` ritorna `RemoteError(HostKeyMismatch, msg, Fingerprint: "<sha256>")` SENZA connettersi (prima connessione: l'utente deve accettare).
  - Fingerprint presente e diversa → stesso errore (possibile MITM).
  - Fingerprint presente e uguale → connessione procede.

Nessun test automatico (serve server SSH reale): build + smoke test manuale nel Task 12.

- [x] **Step 1: Aggiungi SSH.NET**

Run: `dotnet add FileExplorer/FileExplorer.csproj package SSH.NET`
Expected: PackageReference aggiunta, restore ok.

- [x] **Step 2: Implementa SftpRemoteClient**

`FileExplorer/Services/SftpRemoteClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace FileExplorer.Services;

/// <summary>
/// Client SFTP basato su SSH.NET con verifica della host key:
/// la fingerprint SHA-256 deve corrispondere a quella accettata nel profilo.
/// </summary>
public sealed class SftpRemoteClient : IRemoteFileClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private SftpClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;

    public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct) =>
        Task.Run<RemoteError?>(() =>
        {
            string? receivedFingerprint = null;
            try
            {
                var client = new SftpClient(profile.Host, profile.Port, profile.Username, password);
                client.ConnectionInfo.Timeout = ConnectTimeout;

                client.HostKeyReceived += (_, e) =>
                {
                    receivedFingerprint = ComputeSha256Fingerprint(e.HostKey);
                    e.CanTrust = receivedFingerprint == profile.AcceptedHostKeyFingerprint;
                };

                client.Connect();
                _client = client;
                return null;
            }
            catch (SshConnectionException) when (receivedFingerprint is not null
                && receivedFingerprint != profile.AcceptedHostKeyFingerprint)
            {
                // Host key sconosciuta (primo accesso) o cambiata: mai connettersi in silenzio.
                string message = profile.AcceptedHostKeyFingerprint is null
                    ? $"Prima connessione a {profile.Host}: verifica e accetta la fingerprint del server."
                    : $"ATTENZIONE: la host key di {profile.Host} è CAMBIATA (possibile attacco). Accetta solo se il cambio è atteso.";
                return new RemoteError(RemoteErrorKind.HostKeyMismatch, message, receivedFingerprint);
            }
            catch (Exception ex)
            {
                return TranslateError(ex);
            }
        }, ct);

    /// <summary>Fingerprint in formato "SHA256:&lt;base64 senza padding&gt;" (stesso formato di OpenSSH).</summary>
    internal static string ComputeSha256Fingerprint(byte[] hostKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (_client is null)
                return NotConnectedResult();

            try
            {
                var items = new List<RemoteItem>();
                foreach (var entry in _client.ListDirectory(path))
                {
                    if (entry.Name is "." or "..")
                        continue;
                    if (!entry.IsRegularFile && !entry.IsDirectory)
                        continue;
                    items.Add(new RemoteItem(entry.Name, entry.FullName, entry.IsDirectory,
                        entry.IsDirectory ? 0 : entry.Length, entry.LastWriteTime));
                }
                return new RemoteListingResult(items, null);
            }
            catch (Exception ex)
            {
                return new RemoteListingResult(Array.Empty<RemoteItem>(), TranslateError(ex));
            }
        }, ct);

    public async Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
    {
        var all = new List<RemoteItem>();
        var pending = new Queue<string>();
        pending.Enqueue(path);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ListDirectoryAsync(pending.Dequeue(), ct);
            if (result.Error is not null)
                return new RemoteListingResult(Array.Empty<RemoteItem>(), result.Error);

            foreach (var item in result.Items)
            {
                if (item.IsDirectory)
                    pending.Enqueue(item.FullPath);
                else
                    all.Add(item);
            }
        }

        all.Sort((a, b) => string.CompareOrdinal(a.FullPath, b.FullPath));
        return new RemoteListingResult(all, null);
    }

    public Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run<RemoteError?>(() =>
        {
            if (_client is null)
                return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

            try
            {
                using var stream = File.Create(localPath);
                _client.DownloadFile(item.FullPath, stream,
                    bytes =>
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report((long)bytes);
                    });
                return null;
            }
            catch (Exception ex)
            {
                return TranslateError(ex);
            }
        }, ct);

    public ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { _client.Disconnect(); } catch (Exception) { /* già disconnesso */ }
            _client.Dispose();
            _client = null;
        }
        return ValueTask.CompletedTask;
    }

    private static RemoteListingResult NotConnectedResult() =>
        new(Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso."));

    private static RemoteError TranslateError(Exception ex) => ex switch
    {
        SshAuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita: utente o password errati."),
        SftpPathNotFoundException =>
            new RemoteError(RemoteErrorKind.NotFound, "Percorso remoto inesistente."),
        SftpPermissionDeniedException =>
            new RemoteError(RemoteErrorKind.PermissionDenied, "Permesso negato dal server."),
        SshOperationTimeoutException =>
            new RemoteError(RemoteErrorKind.Timeout, "Timeout di connessione al server."),
        SocketException =>
            new RemoteError(RemoteErrorKind.HostUnreachable, "Server non raggiungibile."),
        _ => new RemoteError(RemoteErrorKind.TransferFailed, ex.Message)
    };
}
```

Nota per l'implementatore: se la versione SSH.NET installata espone
`HostKeyEventArgs.FingerPrintSHA256`, puoi usarla al posto di `ComputeSha256Fingerprint`
purché il formato salvato resti identico tra le connessioni. Se `SshConnectionException`
non è il tipo effettivamente lanciato quando `CanTrust=false`, verifica il tipo reale
e adatta il `catch`, mantenendo il contratto: fingerprint non accettata → `RemoteError(HostKeyMismatch, …, fingerprint)`.

- [x] **Step 3: Aggiorna la factory**

In `FileExplorer/Services/RemoteClientFactory.cs` sostituisci il ramo Sftp:

```csharp
        RemoteProtocol.Sftp => new SftpRemoteClient(),
```

(rimuovendo la `NotSupportedException`).

- [x] **Step 4: Build e test completi**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln`
Expected: build pulita, tutti PASS.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/FileExplorer.csproj FileExplorer/Services/SftpRemoteClient.cs FileExplorer/Services/RemoteClientFactory.cs
git commit -m "feat(remote): SFTP client via SSH.NET with host key verification"
```

---

### Task 9: RemoteBrowserViewModel — connessione e navigazione

**Model:** `opus` (stato async, race, flussi password/host key)

**Files:**
- Create: `FileExplorer/ViewModels/RemoteEntryViewModel.cs`
- Create: `FileExplorer/ViewModels/RemoteBrowserViewModel.cs`
- Create: `FileExplorer.Tests/RemoteBrowserViewModelTests.cs`

**Interfaces:**
- Consumes: `IRemoteFileClient`, `ICredentialStore`, `ProfileStore`, `RemoteClientFactory`, `CredentialStoreFactory`, `DownloadService.GetLocalStatus`, modelli Task 1-2. `FakeRemoteClient` e `NullCredentialStore` nei test.
- Produces (usati da Task 10-11):

```csharp
public class RemoteEntryViewModel : ViewModelBase
{
    public RemoteItem Item { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public string SizeDisplay { get; }       // "" per directory, altrimenti "N KB"
    public string ModifiedDisplay { get; }   // "dd/MM/yyyy HH:mm"
    public LocalFileStatus? LocalStatus { get; set; }  // null per directory o senza destinazione
    public RemoteEntryViewModel(RemoteItem item);
}

public class RemoteBrowserViewModel : ViewModelBase
{
    // Costruttore testabile + costruttore default per la view
    public RemoteBrowserViewModel(Func<ConnectionProfile, IRemoteFileClient> clientFactory,
                                  ICredentialStore credentialStore, string profilesFilePath);
    public RemoteBrowserViewModel(); // usa RemoteClientFactory.Create, CredentialStoreFactory.Create(), ProfileStore.DefaultPath

    public ObservableCollection<ConnectionProfile> Profiles { get; }
    public ConnectionProfile? SelectedProfile { get; set; }
    public bool IsConnected { get; }
    public bool IsBusy { get; }
    public string? ErrorMessage { get; }
    public string? StatusMessage { get; }
    public string CurrentPath { get; }
    public ObservableCollection<RemoteEntryViewModel> Items { get; }

    public bool IsPasswordPromptVisible { get; }
    public string? PasswordInput { get; set; }
    public bool CanSavePassword { get; }        // credentialStore.IsAvailable
    public bool SavePassword { get; set; }      // default true se CanSavePassword

    public string? PendingFingerprint { get; }  // non null → banner host key visibile

    public Task LoadProfilesAsync();
    public Task ConnectAsync();
    public Task DisconnectAsync();
    public Task OpenDirectoryAsync(RemoteEntryViewModel entry);
    public Task NavigateUpAsync();
    public Task RefreshAsync();
    public Task AcceptFingerprintAsync();
    public void RejectFingerprint();
}
```

Comportamento richiesto (contratto per i test):
- `ConnectAsync` senza password nel keyring e senza `PasswordInput` → `IsPasswordPromptVisible = true`, nessuna connessione tentata.
- `ConnectAsync` con `PasswordInput` → connette; se ok e `SavePassword && CanSavePassword` → salva la password nel keyring; poi lista `CurrentPath = "/"`.
- Errore `HostKeyMismatch` → `PendingFingerprint` valorizzata, `ErrorMessage` col messaggio; `AcceptFingerprintAsync` salva la fingerprint nel profilo (via `ProfileStore.SaveAsync`) e riprova la connessione; `RejectFingerprint` pulisce e resta disconnesso.
- Altri errori di connessione → `ErrorMessage`, `IsConnected == false`.
- `OpenDirectoryAsync` su directory → aggiorna `CurrentPath` e ricarica `Items`.
- `NavigateUpAsync` da `/sub` → `/`; da `/` resta `/`.
- `IsBusy` true durante le operazioni, false alla fine (anche su errore).
- Directory prima dei file in `Items`, ordinati per nome.

- [ ] **Step 1: Scrivi i test**

`FileExplorer.Tests/RemoteBrowserViewModelTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private RemoteBrowserViewModel CreateViewModel(ICredentialStore? store = null)
    {
        var vm = new RemoteBrowserViewModel(
            _ => _client,
            store ?? new NullCredentialStore(),
            Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        return vm;
    }

    [Fact]
    public async Task ConnectAsync_NoStoredPassword_ShowsPasswordPrompt()
    {
        var vm = CreateViewModel();

        await vm.ConnectAsync();

        Assert.True(vm.IsPasswordPromptVisible);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithPasswordInput_ConnectsAndLists()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.True(vm.IsConnected);
        Assert.False(vm.IsPasswordPromptVisible);
        Assert.Equal("/", vm.CurrentPath);
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.Items[0].IsDirectory);          // directory prima dei file
        Assert.Equal("docs", vm.Items[0].Name);
        Assert.Equal("a.txt", vm.Items[1].Name);
    }

    [Fact]
    public async Task ConnectAsync_AuthError_SetsErrorMessage()
    {
        _client.ConnectError = new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita.");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw-sbagliata";

        await vm.ConnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Contains("Autenticazione", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ConnectAsync_HostKeyMismatch_ShowsPendingFingerprint()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.Equal("SHA256:xyz", vm.PendingFingerprint);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task AcceptFingerprint_SavesToProfileAndReconnects()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        _client.ConnectError = null; // il server ora è "fidato"
        await vm.AcceptFingerprintAsync();

        Assert.Equal("SHA256:xyz", vm.SelectedProfile!.AcceptedHostKeyFingerprint);
        Assert.Null(vm.PendingFingerprint);
        Assert.True(vm.IsConnected);
    }

    [Fact]
    public async Task RejectFingerprint_ClearsPendingAndStaysDisconnected()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        vm.RejectFingerprint();

        Assert.Null(vm.PendingFingerprint);
        Assert.False(vm.IsConnected);
        Assert.Null(vm.SelectedProfile!.AcceptedHostKeyFingerprint);
    }

    [Fact]
    public async Task OpenDirectory_NavigatesAndLists()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/b.txt", "BBB");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        await vm.OpenDirectoryAsync(vm.Items.First(i => i.IsDirectory));

        Assert.Equal("/docs", vm.CurrentPath);
        var entry = Assert.Single(vm.Items);
        Assert.Equal("b.txt", entry.Name);
    }

    [Fact]
    public async Task NavigateUp_FromSubdir_GoesToParent_AndStopsAtRoot()
    {
        _client.AddDirectory("/docs");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        await vm.OpenDirectoryAsync(vm.Items[0]);

        await vm.NavigateUpAsync();
        Assert.Equal("/", vm.CurrentPath);

        await vm.NavigateUpAsync();
        Assert.Equal("/", vm.CurrentPath); // dalla radice non si sale
    }

    [Fact]
    public async Task DisconnectAsync_ClearsState()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        await vm.DisconnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Items);
    }
}
```

- [ ] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter RemoteBrowserViewModelTests`
Expected: errore di compilazione.

- [ ] **Step 3: Implementa RemoteEntryViewModel**

`FileExplorer/ViewModels/RemoteEntryViewModel.cs`:

```csharp
using FileExplorer.Models;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Voce remota mostrata nella lista, con stato locale calcolato.</summary>
public class RemoteEntryViewModel : ViewModelBase
{
    public RemoteItem Item { get; }

    public string Name => Item.Name;
    public bool IsDirectory => Item.IsDirectory;
    public string SizeDisplay => Item.IsDirectory ? "" : $"{Item.Size / 1024} KB";
    public string ModifiedDisplay => Item.Modified.ToString("dd/MM/yyyy HH:mm");

    private LocalFileStatus? _localStatus;

    /// <summary>Null per le directory o quando non c'è una destinazione impostata.</summary>
    public LocalFileStatus? LocalStatus
    {
        get => _localStatus;
        set => this.RaiseAndSetIfChanged(ref _localStatus, value);
    }

    public RemoteEntryViewModel(RemoteItem item)
    {
        Item = item;
    }
}
```

- [ ] **Step 4: Implementa RemoteBrowserViewModel (parte connessione/navigazione)**

`FileExplorer/ViewModels/RemoteBrowserViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Server remoto": connessione FTP/FTPS/SFTP, navigazione e download con filtri.
/// </summary>
public class RemoteBrowserViewModel : ViewModelBase
{
    private readonly Func<ConnectionProfile, IRemoteFileClient> _clientFactory;
    private readonly ICredentialStore _credentialStore;
    private readonly string _profilesFilePath;

    private IRemoteFileClient? _client;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    public ObservableCollection<RemoteEntryViewModel> Items { get; } = new();

    private ConnectionProfile? _selectedProfile;
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private string _currentPath = "/";
    public string CurrentPath
    {
        get => _currentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    private bool _isPasswordPromptVisible;
    public bool IsPasswordPromptVisible
    {
        get => _isPasswordPromptVisible;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordPromptVisible, value);
    }

    private string? _passwordInput;
    public string? PasswordInput
    {
        get => _passwordInput;
        set => this.RaiseAndSetIfChanged(ref _passwordInput, value);
    }

    public bool CanSavePassword => _credentialStore.IsAvailable;

    private bool _savePassword;
    public bool SavePassword
    {
        get => _savePassword;
        set => this.RaiseAndSetIfChanged(ref _savePassword, value);
    }

    private string? _pendingFingerprint;
    public string? PendingFingerprint
    {
        get => _pendingFingerprint;
        private set => this.RaiseAndSetIfChanged(ref _pendingFingerprint, value);
    }

    /// <summary>Costruttore per la view: dipendenze reali.</summary>
    public RemoteBrowserViewModel()
        : this(RemoteClientFactory.Create, CredentialStoreFactory.Create(), ProfileStore.DefaultPath)
    {
    }

    /// <summary>Costruttore testabile con dipendenze iniettate.</summary>
    public RemoteBrowserViewModel(
        Func<ConnectionProfile, IRemoteFileClient> clientFactory,
        ICredentialStore credentialStore,
        string profilesFilePath)
    {
        _clientFactory = clientFactory;
        _credentialStore = credentialStore;
        _profilesFilePath = profilesFilePath;
        _savePassword = credentialStore.IsAvailable;
    }

    /// <summary>Carica i profili salvati (chiamata dalla view all'avvio).</summary>
    public async Task LoadProfilesAsync()
    {
        var profiles = await ProfileStore.LoadAsync(_profilesFilePath);
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public async Task ConnectAsync()
    {
        if (SelectedProfile is null || IsBusy)
            return;

        ErrorMessage = null;
        PendingFingerprint = null;

        string? password = PasswordInput;
        if (string.IsNullOrEmpty(password))
            password = await _credentialStore.GetPasswordAsync(SelectedProfile.Id);

        if (string.IsNullOrEmpty(password))
        {
            IsPasswordPromptVisible = true;
            StatusMessage = _credentialStore.IsAvailable
                ? "Inserire la password."
                : "Keyring di sistema non disponibile: la password va inserita a ogni connessione.";
            return;
        }

        IsBusy = true;
        try
        {
            await DisposeClientAsync();
            var client = _clientFactory(SelectedProfile);
            var error = await client.ConnectAsync(SelectedProfile, password, CancellationToken.None);

            if (error is not null)
            {
                await client.DisposeAsync();
                ErrorMessage = error.Message;
                if (error.Kind == RemoteErrorKind.HostKeyMismatch)
                    PendingFingerprint = error.Fingerprint;
                return;
            }

            _client = client;
            IsConnected = true;
            IsPasswordPromptVisible = false;

            if (!string.IsNullOrEmpty(PasswordInput) && SavePassword && _credentialStore.IsAvailable)
                await _credentialStore.SetPasswordAsync(SelectedProfile.Id, PasswordInput);
            PasswordInput = null;

            CurrentPath = "/";
            await LoadListingAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        await DisposeClientAsync();
        IsConnected = false;
        Items.Clear();
        StatusMessage = "Disconnesso.";
    }

    public async Task OpenDirectoryAsync(RemoteEntryViewModel entry)
    {
        if (!entry.IsDirectory || _client is null)
            return;

        CurrentPath = entry.Item.FullPath;
        await LoadListingAsync();
    }

    public async Task NavigateUpAsync()
    {
        if (_client is null || CurrentPath == "/")
            return;

        int lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        await LoadListingAsync();
    }

    public Task RefreshAsync() => _client is null ? Task.CompletedTask : LoadListingAsync();

    public async Task AcceptFingerprintAsync()
    {
        if (SelectedProfile is null || PendingFingerprint is null)
            return;

        SelectedProfile.AcceptedHostKeyFingerprint = PendingFingerprint;
        PendingFingerprint = null;
        await ProfileStore.SaveAsync(_profilesFilePath, Profiles.ToList());
        await ConnectAsync();
    }

    public void RejectFingerprint()
    {
        PendingFingerprint = null;
        StatusMessage = "Connessione rifiutata: host key non accettata.";
    }

    private async Task LoadListingAsync()
    {
        if (_client is null)
            return;

        IsBusy = true;
        try
        {
            ErrorMessage = null;
            var result = await _client.ListDirectoryAsync(CurrentPath, CancellationToken.None);
            Items.Clear();

            if (result.Error is not null)
            {
                ErrorMessage = result.Error.Message;
                return;
            }

            foreach (var item in result.Items
                         .OrderByDescending(i => i.IsDirectory)
                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                Items.Add(new RemoteEntryViewModel(item));
            }

            RefreshLocalStatuses();
            StatusMessage = $"{Items.Count} elementi in {CurrentPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ricalcola la colonna "Su disco". Ridefinita/estesa nel task download.</summary>
    protected virtual void RefreshLocalStatuses()
    {
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    /// <summary>Client corrente (per il task download).</summary>
    protected IRemoteFileClient? Client => _client;

    /// <summary>Percorso del file profili (per il task download/editor).</summary>
    protected string ProfilesFilePath => _profilesFilePath;
}
```

- [ ] **Step 5: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter RemoteBrowserViewModelTests`
Expected: tutti PASS.

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/ViewModels/RemoteEntryViewModel.cs FileExplorer/ViewModels/RemoteBrowserViewModel.cs FileExplorer.Tests/RemoteBrowserViewModelTests.cs
git commit -m "feat(remote): remote browser viewmodel - connection and navigation"
```

---

### Task 10: RemoteBrowserViewModel — filtri e download

**Model:** `opus`

**Files:**
- Modify: `FileExplorer/ViewModels/RemoteBrowserViewModel.cs`
- Create: `FileExplorer.Tests/RemoteBrowserDownloadTests.cs`

**Interfaces:**
- Consumes: tutto il Task 9, `DownloadService.DownloadAsync` (Task 6), `DownloadService.GetLocalStatus` (Task 3).
- Produces (usati dalla view, Task 12):

```csharp
// Filtri (bound alla UI)
public string? FilterPattern { get; set; }
public string? FilterMinSizeKb { get; set; }     // stringa: la UI ha TextBox; vuoto = nessun limite
public string? FilterMaxSizeKb { get; set; }
public DateTimeOffset? FilterModifiedAfter { get; set; }   // DatePicker Avalonia usa DateTimeOffset
public DateTimeOffset? FilterModifiedBefore { get; set; }
public bool OnlyMissing { get; set; }
public bool IncludeSubfolders { get; set; }
public bool OverwriteAlways { get; set; }

// Download
public string? DestinationFolder { get; set; }   // al set: persiste in profile.LastDestinationFolder e ricalcola gli stati
public bool IsDownloading { get; }
public double DownloadProgressValue { get; }     // 0..1
public string? DownloadStatusText { get; }       // "3/10 — nome.ext"
public Task DownloadSelectedAsync(IReadOnlyList<RemoteEntryViewModel> selected);
public Task DownloadCurrentDirectoryAsync();
public void CancelDownload();

// Vista filtrata
public ObservableCollection<RemoteEntryViewModel> VisibleItems { get; } // Items che passano il filtro; è ciò che la UI mostra
```

Comportamento richiesto:
- `BuildFilter()` interno converte le proprietà UI in `DownloadFilter` (KB → byte: valore * 1024; stringa non numerica = ignorata).
- `VisibleItems` ricalcolata quando cambiano `Items` o un filtro (le directory sempre visibili).
- `RefreshLocalStatuses` (override del hook Task 9): per ogni file in `Items`, se `DestinationFolder` valorizzata → `LocalStatus = DownloadService.GetLocalStatus(item, Path.Combine(DestinationFolder, item.Name))`; directory e senza destinazione → null.
- `DownloadSelectedAsync`: scarica le voci selezionate (base remota = `CurrentPath`); se una selezione è una directory, i suoi file si ottengono con `ListRecursiveAsync` solo se `IncludeSubfolders`, altrimenti la directory è ignorata.
- `DownloadCurrentDirectoryAsync`: `IncludeSubfolders` → `ListRecursiveAsync(CurrentPath)`; altrimenti i soli file di `Items`.
- Al termine: `StatusMessage` = "Scaricati X, saltati Y, falliti Z." e `RefreshLocalStatuses()`.
- `CancelDownload` annulla il `CancellationTokenSource` del batch; il download termina con `StatusMessage` "Download annullato."
- Errori di listing durante il download → `ErrorMessage`, download non parte.

- [ ] **Step 1: Scrivi i test**

`FileExplorer.Tests/RemoteBrowserDownloadTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserDownloadTests : IDisposable
{
    private readonly string _root;
    private readonly string _dest;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserDownloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vmdl-" + Guid.NewGuid().ToString("N"));
        _dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<RemoteBrowserViewModel> CreateConnectedAsync()
    {
        var vm = new RemoteBrowserViewModel(
            _ => _client, new NullCredentialStore(), Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        vm.DestinationFolder = _dest;
        return vm;
    }

    [Fact]
    public async Task VisibleItems_FilterPattern_HidesNonMatching_KeepsDirectories()
    {
        _client.AddFile("/a.jpg", "IMG");
        _client.AddFile("/b.txt", "TXT");
        _client.AddDirectory("/docs");
        var vm = await CreateConnectedAsync();

        vm.FilterPattern = "*.jpg";

        Assert.Equal(2, vm.VisibleItems.Count); // docs + a.jpg
        Assert.Contains(vm.VisibleItems, i => i.Name == "docs");
        Assert.Contains(vm.VisibleItems, i => i.Name == "a.jpg");
    }

    [Fact]
    public async Task RefreshLocalStatuses_MarksPresentFile()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/a.txt", "AAA", modified);
        string local = Path.Combine(_dest, "a.txt");
        await File.WriteAllTextAsync(local, "AAA");
        File.SetLastWriteTime(local, modified);

        var vm = await CreateConnectedAsync();

        var entry = vm.Items.Single(i => i.Name == "a.txt");
        Assert.Equal(LocalFileStatus.Present, entry.LocalStatus);
    }

    [Fact]
    public async Task DownloadSelected_DownloadsOnlySelection()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddFile("/b.txt", "BBB");
        var vm = await CreateConnectedAsync();

        var selection = vm.Items.Where(i => i.Name == "a.txt").ToList();
        await vm.DownloadSelectedAsync(selection);

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_dest, "b.txt")));
        Assert.Contains("Scaricati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadSelected_DirectoryWithSubfolders_DownloadsRecursively()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/sub1.txt", "S1");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        var selection = vm.Items.Where(i => i.IsDirectory).ToList();
        await vm.DownloadSelectedAsync(selection);

        Assert.True(File.Exists(Path.Combine(_dest, "docs", "sub1.txt")));
    }

    [Fact]
    public async Task DownloadCurrentDirectory_NonRecursive_TopLevelOnly()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/deep.txt", "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = false;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_dest, "docs", "deep.txt")));
    }

    [Fact]
    public async Task DownloadCurrentDirectory_Recursive_IncludesSubfolders()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/deep.txt", "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_dest, "docs", "deep.txt")));
    }

    [Fact]
    public async Task DownloadCurrentDirectory_AppliesFilter()
    {
        _client.AddFile("/a.jpg", "IMG");
        _client.AddFile("/b.txt", "TXT");
        var vm = await CreateConnectedAsync();
        vm.FilterPattern = "*.jpg";

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(_dest, "b.txt")));
    }

    [Fact]
    public async Task Download_ReportsSkippedInStatusMessage()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/a.txt", "AAA", modified);
        string local = Path.Combine(_dest, "a.txt");
        await File.WriteAllTextAsync(local, "AAA");
        File.SetLastWriteTime(local, modified);
        var vm = await CreateConnectedAsync();

        await vm.DownloadCurrentDirectoryAsync();

        Assert.Contains("saltati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Download_SetsDestinationOnProfile()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        Assert.Equal(_dest, vm.SelectedProfile!.LastDestinationFolder);
    }

    [Fact]
    public async Task Connect_RestoresLastDestinationFolderFromProfile()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = new RemoteBrowserViewModel(
            _ => _client, new NullCredentialStore(), Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile
        {
            Name = "test", Host = "h", Username = "u", LastDestinationFolder = _dest
        });
        vm.SelectedProfile = vm.Profiles[0];
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.Equal(_dest, vm.DestinationFolder);
    }

    [Fact]
    public async Task Download_PersistsProfilesWithDestination()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        await vm.DownloadCurrentDirectoryAsync();

        var saved = await ProfileStore.LoadAsync(Path.Combine(_root, "profiles.json"));
        Assert.Equal(_dest, Assert.Single(saved).LastDestinationFolder);
    }

    [Fact]
    public async Task DownloadWithoutDestination_SetsError()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();
        vm.DestinationFolder = null;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task FilterMinSizeKb_NonNumeric_Ignored()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        vm.FilterMinSizeKb = "abc"; // non numerico: nessun filtro applicato

        Assert.Single(vm.VisibleItems);
    }
}
```

- [ ] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter RemoteBrowserDownloadTests`
Expected: errore di compilazione.

- [ ] **Step 3: Implementa**

Aggiungi a `RemoteBrowserViewModel` (stessa classe; aggiungi `using System.Collections.Generic;` e `using System.IO;` se mancanti):

```csharp
    // ----- Filtri (bound alla UI) -----

    public ObservableCollection<RemoteEntryViewModel> VisibleItems { get; } = new();

    private string? _filterPattern;
    public string? FilterPattern
    {
        get => _filterPattern;
        set { this.RaiseAndSetIfChanged(ref _filterPattern, value); RebuildVisibleItems(); }
    }

    private string? _filterMinSizeKb;
    public string? FilterMinSizeKb
    {
        get => _filterMinSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMinSizeKb, value); RebuildVisibleItems(); }
    }

    private string? _filterMaxSizeKb;
    public string? FilterMaxSizeKb
    {
        get => _filterMaxSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMaxSizeKb, value); RebuildVisibleItems(); }
    }

    private DateTimeOffset? _filterModifiedAfter;
    public DateTimeOffset? FilterModifiedAfter
    {
        get => _filterModifiedAfter;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedAfter, value); RebuildVisibleItems(); }
    }

    private DateTimeOffset? _filterModifiedBefore;
    public DateTimeOffset? FilterModifiedBefore
    {
        get => _filterModifiedBefore;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedBefore, value); RebuildVisibleItems(); }
    }

    private bool _onlyMissing;
    public bool OnlyMissing
    {
        get => _onlyMissing;
        set => this.RaiseAndSetIfChanged(ref _onlyMissing, value);
    }

    private bool _includeSubfolders;
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => this.RaiseAndSetIfChanged(ref _includeSubfolders, value);
    }

    private bool _overwriteAlways;
    public bool OverwriteAlways
    {
        get => _overwriteAlways;
        set => this.RaiseAndSetIfChanged(ref _overwriteAlways, value);
    }

    // ----- Download -----

    private string? _destinationFolder;
    public string? DestinationFolder
    {
        get => _destinationFolder;
        set
        {
            this.RaiseAndSetIfChanged(ref _destinationFolder, value);
            if (SelectedProfile is not null)
                SelectedProfile.LastDestinationFolder = value;
            RefreshLocalStatuses();
        }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    private double _downloadProgressValue;
    public double DownloadProgressValue
    {
        get => _downloadProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _downloadProgressValue, value);
    }

    private string? _downloadStatusText;
    public string? DownloadStatusText
    {
        get => _downloadStatusText;
        private set => this.RaiseAndSetIfChanged(ref _downloadStatusText, value);
    }

    private CancellationTokenSource? _downloadCts;

    public async Task DownloadSelectedAsync(IReadOnlyList<RemoteEntryViewModel> selected)
    {
        var files = new List<RemoteItem>();
        foreach (var entry in selected)
        {
            if (!entry.IsDirectory)
            {
                files.Add(entry.Item);
            }
            else if (IncludeSubfolders && Client is not null)
            {
                var result = await Client.ListRecursiveAsync(entry.Item.FullPath, CancellationToken.None);
                if (result.Error is not null)
                {
                    ErrorMessage = result.Error.Message;
                    return;
                }
                files.AddRange(result.Items);
            }
        }
        await RunDownloadAsync(files);
    }

    public async Task DownloadCurrentDirectoryAsync()
    {
        if (Client is null)
            return;

        IReadOnlyList<RemoteItem> files;
        if (IncludeSubfolders)
        {
            var result = await Client.ListRecursiveAsync(CurrentPath, CancellationToken.None);
            if (result.Error is not null)
            {
                ErrorMessage = result.Error.Message;
                return;
            }
            files = result.Items;
        }
        else
        {
            files = Items.Where(i => !i.IsDirectory).Select(i => i.Item).ToList();
        }
        await RunDownloadAsync(files);
    }

    public void CancelDownload() => _downloadCts?.Cancel();

    private async Task RunDownloadAsync(IReadOnlyList<RemoteItem> files)
    {
        if (Client is null)
            return;

        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            ErrorMessage = "Scegliere una cartella di destinazione prima di scaricare.";
            return;
        }

        ErrorMessage = null;
        IsDownloading = true;
        _downloadCts = new CancellationTokenSource();

        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadProgressValue = p.TotalFiles == 0 ? 0 : (double)p.FileIndex / p.TotalFiles;
            DownloadStatusText = $"{p.FileIndex}/{p.TotalFiles} — {p.CurrentFile}";
        });

        try
        {
            var report = await DownloadService.DownloadAsync(
                Client, files, CurrentPath, DestinationFolder, BuildFilter(),
                OverwriteAlways, progress, _downloadCts.Token);

            StatusMessage =
                $"Scaricati {report.Downloaded.Count}, saltati {report.Skipped.Count}, falliti {report.Failed.Count}.";
            if (report.Failed.Count > 0)
                ErrorMessage = $"{report.Failed.Count} file falliti. Primo errore: {report.Failed[0].Reason}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download annullato.";
        }
        finally
        {
            IsDownloading = false;
            DownloadStatusText = null;
            DownloadProgressValue = 0;
            _downloadCts.Dispose();
            _downloadCts = null;
            RefreshLocalStatuses();
            // Persiste LastDestinationFolder aggiornata sul profilo.
            await ProfileStore.SaveAsync(ProfilesFilePath, Profiles.ToList());
        }
    }

    private DownloadFilter BuildFilter() => new()
    {
        NamePattern = FilterPattern,
        MinSize = ParseKb(FilterMinSizeKb),
        MaxSize = ParseKb(FilterMaxSizeKb),
        ModifiedAfter = FilterModifiedAfter?.DateTime,
        ModifiedBefore = FilterModifiedBefore?.DateTime,
        OnlyMissing = OnlyMissing,
        Recursive = IncludeSubfolders
    };

    private static long? ParseKb(string? text) =>
        long.TryParse(text, out long kb) && kb >= 0 ? kb * 1024 : null;

    private void RebuildVisibleItems()
    {
        var filter = BuildFilter();
        VisibleItems.Clear();
        foreach (var entry in Items)
        {
            if (filter.Matches(entry.Item))
                VisibleItems.Add(entry);
        }
    }
```

Nel Task 9 `RefreshLocalStatuses` era un hook vuoto: sostituiscilo (stessa classe, non più `virtual` se preferisci) con:

```csharp
    /// <summary>Ricalcola la colonna "Su disco" per i file di primo livello.</summary>
    protected void RefreshLocalStatuses()
    {
        foreach (var entry in Items)
        {
            if (entry.IsDirectory || string.IsNullOrWhiteSpace(DestinationFolder))
            {
                entry.LocalStatus = null;
                continue;
            }
            entry.LocalStatus = DownloadService.GetLocalStatus(
                entry.Item, Path.Combine(DestinationFolder, entry.Name));
        }
    }
```

e in fondo a `LoadListingAsync`, dopo `RefreshLocalStatuses()`, aggiungi `RebuildVisibleItems();`.

Inoltre in `ConnectAsync` (Task 9), subito dopo `CurrentPath = "/";`, aggiungi il ripristino
della destinazione salvata sul profilo:

```csharp
            DestinationFolder = SelectedProfile.LastDestinationFolder;
```

- [ ] **Step 4: Verifica che passino (tutti, anche i precedenti)**

Run: `dotnet test FileExplorer.sln`
Expected: tutti PASS (nessuna regressione sui test del Task 9).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/RemoteBrowserViewModel.cs FileExplorer.Tests/RemoteBrowserDownloadTests.cs
git commit -m "feat(remote): filters, disk-presence check and download orchestration in viewmodel"
```

---

### Task 11: ProfileEditorViewModel e finestra profili

**Model:** `sonnet`

**Files:**
- Create: `FileExplorer/ViewModels/ProfileEditorViewModel.cs`
- Create: `FileExplorer/Views/ProfileEditorWindow.axaml`
- Create: `FileExplorer/Views/ProfileEditorWindow.axaml.cs`
- Create: `FileExplorer.Tests/ProfileEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `ConnectionProfile`, `RemoteProtocol`, `ICredentialStore`.
- Produces:

```csharp
public class ProfileEditorViewModel : ViewModelBase
{
    public ProfileEditorViewModel(ConnectionProfile profile, ICredentialStore credentialStore); // edita una copia
    public string Name { get; set; }
    public string Host { get; set; }
    public string PortText { get; set; }
    public string Username { get; set; }
    public RemoteProtocol Protocol { get; set; }   // il set aggiorna PortText al default se era il default dell'altro protocollo
    public string? Password { get; set; }          // null/vuoto = non toccare la password salvata
    public bool CanSavePassword { get; }
    public bool ShowFtpWarning { get; }            // Protocol == Ftp
    public string? ValidationError { get; }
    public bool Validate();                        // false + ValidationError se invalido
    public Task<ConnectionProfile> SaveAsync();    // applica i campi al profilo e salva l'eventuale password nel keyring
}
```

  Regole di validazione: `Name` e `Host` non vuoti; porta intera 1-65535. Default porte: Ftp/Ftps 21, Sftp 22.

- [ ] **Step 1: Scrivi i test**

`FileExplorer.Tests/ProfileEditorViewModelTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class ProfileEditorViewModelTests
{
    private static ProfileEditorViewModel Create(ConnectionProfile? profile = null) =>
        new(profile ?? new ConnectionProfile(), new NullCredentialStore());

    [Fact]
    public void Validate_EmptyNameOrHost_Fails()
    {
        var vm = Create();
        vm.Name = "";
        vm.Host = "host";
        Assert.False(vm.Validate());
        Assert.NotNull(vm.ValidationError);

        vm.Name = "nome";
        vm.Host = "";
        Assert.False(vm.Validate());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("")]
    public void Validate_InvalidPort_Fails(string port)
    {
        var vm = Create();
        vm.Name = "nome";
        vm.Host = "host";
        vm.PortText = port;
        Assert.False(vm.Validate());
    }

    [Fact]
    public void Validate_ValidInput_Passes()
    {
        var vm = Create();
        vm.Name = "nome";
        vm.Host = "host";
        vm.PortText = "2222";
        Assert.True(vm.Validate());
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void Protocol_Switch_UpdatesDefaultPort()
    {
        var vm = Create(new ConnectionProfile { Protocol = RemoteProtocol.Sftp, Port = 22 });
        Assert.Equal("22", vm.PortText);

        vm.Protocol = RemoteProtocol.Ftp;
        Assert.Equal("21", vm.PortText);

        vm.Protocol = RemoteProtocol.Sftp;
        Assert.Equal("22", vm.PortText);
    }

    [Fact]
    public void Protocol_Switch_KeepsCustomPort()
    {
        var vm = Create(new ConnectionProfile { Protocol = RemoteProtocol.Sftp, Port = 2222 });
        vm.Protocol = RemoteProtocol.Ftp;
        Assert.Equal("2222", vm.PortText); // porta personalizzata: non toccata
    }

    [Fact]
    public void ShowFtpWarning_OnlyForPlainFtp()
    {
        var vm = Create();
        vm.Protocol = RemoteProtocol.Ftp;
        Assert.True(vm.ShowFtpWarning);
        vm.Protocol = RemoteProtocol.Ftps;
        Assert.False(vm.ShowFtpWarning);
        vm.Protocol = RemoteProtocol.Sftp;
        Assert.False(vm.ShowFtpWarning);
    }

    [Fact]
    public async Task SaveAsync_AppliesFieldsToProfile()
    {
        var profile = new ConnectionProfile();
        var vm = Create(profile);
        vm.Name = "NAS";
        vm.Host = "nas.local";
        vm.PortText = "2222";
        vm.Username = "utente";
        vm.Protocol = RemoteProtocol.Sftp;

        var saved = await vm.SaveAsync();

        Assert.Same(profile, saved);
        Assert.Equal("NAS", saved.Name);
        Assert.Equal("nas.local", saved.Host);
        Assert.Equal(2222, saved.Port);
        Assert.Equal("utente", saved.Username);
        Assert.Equal(RemoteProtocol.Sftp, saved.Protocol);
    }
}
```

- [ ] **Step 2: Verifica che falliscano**

Run: `dotnet test FileExplorer.sln --filter ProfileEditorViewModelTests`
Expected: errore di compilazione.

- [ ] **Step 3: Implementa il ViewModel**

`FileExplorer/ViewModels/ProfileEditorViewModel.cs`:

```csharp
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Editor di un profilo di connessione. Applica i campi solo al SaveAsync.</summary>
public class ProfileEditorViewModel : ViewModelBase
{
    private readonly ConnectionProfile _profile;
    private readonly ICredentialStore _credentialStore;

    private string _name;
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private string _host;
    public string Host
    {
        get => _host;
        set => this.RaiseAndSetIfChanged(ref _host, value);
    }

    private string _portText;
    public string PortText
    {
        get => _portText;
        set => this.RaiseAndSetIfChanged(ref _portText, value);
    }

    private string _username;
    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private RemoteProtocol _protocol;
    public RemoteProtocol Protocol
    {
        get => _protocol;
        set
        {
            var previous = _protocol;
            this.RaiseAndSetIfChanged(ref _protocol, value);
            if (previous != value && PortText == DefaultPort(previous).ToString())
                PortText = DefaultPort(value).ToString();
            this.RaisePropertyChanged(nameof(ShowFtpWarning));
        }
    }

    private string? _password;

    /// <summary>Vuoto o null = lascia invariata la password salvata.</summary>
    public string? Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public bool CanSavePassword => _credentialStore.IsAvailable;

    /// <summary>FTP semplice trasmette le credenziali in chiaro.</summary>
    public bool ShowFtpWarning => Protocol == RemoteProtocol.Ftp;

    private string? _validationError;
    public string? ValidationError
    {
        get => _validationError;
        private set => this.RaiseAndSetIfChanged(ref _validationError, value);
    }

    public ProfileEditorViewModel(ConnectionProfile profile, ICredentialStore credentialStore)
    {
        _profile = profile;
        _credentialStore = credentialStore;
        _name = profile.Name;
        _host = profile.Host;
        _portText = profile.Port.ToString();
        _username = profile.Username;
        _protocol = profile.Protocol;
    }

    private static int DefaultPort(RemoteProtocol protocol) =>
        protocol == RemoteProtocol.Sftp ? 22 : 21;

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Il nome del profilo è obbligatorio.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Host))
        {
            ValidationError = "L'host è obbligatorio.";
            return false;
        }
        if (!int.TryParse(PortText, out int port) || port is < 1 or > 65535)
        {
            ValidationError = "La porta deve essere un numero tra 1 e 65535.";
            return false;
        }
        ValidationError = null;
        return true;
    }

    /// <summary>Applica i campi al profilo e salva l'eventuale nuova password nel keyring.</summary>
    public async Task<ConnectionProfile> SaveAsync()
    {
        _profile.Name = Name.Trim();
        _profile.Host = Host.Trim();
        _profile.Port = int.Parse(PortText);
        _profile.Username = Username.Trim();
        _profile.Protocol = Protocol;

        if (!string.IsNullOrEmpty(Password) && _credentialStore.IsAvailable)
            await _credentialStore.SetPasswordAsync(_profile.Id, Password);

        return _profile;
    }
}
```

- [ ] **Step 4: Verifica che passino**

Run: `dotnet test FileExplorer.sln --filter ProfileEditorViewModelTests`
Expected: tutti PASS.

- [ ] **Step 5: Crea la finestra**

`FileExplorer/Views/ProfileEditorWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        xmlns:models="clr-namespace:FileExplorer.Models"
        x:Class="FileExplorer.Views.ProfileEditorWindow"
        Title="Profilo server"
        Width="420" SizeToContent="Height"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface}">

  <StackPanel Margin="20" Spacing="10">

    <TextBlock Text="Nome profilo" Foreground="{DynamicResource Brush.TextMuted}" />
    <TextBox Text="{Binding Name}" Watermark="Es. NAS di casa" />

    <TextBlock Text="Protocollo" Foreground="{DynamicResource Brush.TextMuted}" />
    <ComboBox SelectedItem="{Binding Protocol}" HorizontalAlignment="Stretch">
      <ComboBox.Items>
        <models:RemoteProtocol>Sftp</models:RemoteProtocol>
        <models:RemoteProtocol>Ftps</models:RemoteProtocol>
        <models:RemoteProtocol>Ftp</models:RemoteProtocol>
      </ComboBox.Items>
    </ComboBox>

    <Border Classes="badge warning" IsVisible="{Binding ShowFtpWarning}" HorizontalAlignment="Stretch">
      <TextBlock Text="FTP trasmette la password in chiaro: preferire FTPS se il server lo supporta."
                 TextWrapping="Wrap" />
    </Border>

    <Grid ColumnDefinitions="*,120" ColumnSpacing="10">
      <StackPanel Grid.Column="0" Spacing="10">
        <TextBlock Text="Host" Foreground="{DynamicResource Brush.TextMuted}" />
        <TextBox Text="{Binding Host}" Watermark="es. nas.local" />
      </StackPanel>
      <StackPanel Grid.Column="1" Spacing="10">
        <TextBlock Text="Porta" Foreground="{DynamicResource Brush.TextMuted}" />
        <TextBox Text="{Binding PortText}" />
      </StackPanel>
    </Grid>

    <TextBlock Text="Utente" Foreground="{DynamicResource Brush.TextMuted}" />
    <TextBox Text="{Binding Username}" />

    <TextBlock Text="Password" Foreground="{DynamicResource Brush.TextMuted}" />
    <TextBox Text="{Binding Password}" PasswordChar="●"
             Watermark="Lascia vuoto per non modificarla" />
    <TextBlock Text="Il keyring di sistema non è disponibile: la password verrà chiesta a ogni connessione."
               IsVisible="{Binding !CanSavePassword}"
               Foreground="{DynamicResource Brush.TextMuted}"
               TextWrapping="Wrap" FontSize="12" />

    <Border Classes="badge error" IsVisible="{Binding ValidationError, Converter={x:Static ObjectConverters.IsNotNull}}"
            HorizontalAlignment="Stretch">
      <TextBlock Text="{Binding ValidationError}" TextWrapping="Wrap" />
    </Border>

    <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Margin="0,10,0,0">
      <Button Classes="secondary" Content="Annulla" Click="OnCancelClick" />
      <Button Classes="primary" Content="Salva" Click="OnSaveClick" />
    </StackPanel>

  </StackPanel>
</Window>
```

`FileExplorer/Views/ProfileEditorWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Finestra di modifica profilo. Chiude con true se il profilo è stato salvato.
/// </summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _viewModel;

    // Costruttore senza parametri richiesto dal designer Avalonia.
    public ProfileEditorWindow()
        : this(new ProfileEditorViewModel(new Models.ConnectionProfile(), new Services.NullCredentialStore()))
    {
    }

    public ProfileEditorWindow(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate())
            return;

        await _viewModel.SaveAsync();
        Close(true);
    }
}
```

- [ ] **Step 6: Build e commit**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln`
Expected: build pulita, tutti PASS.

```bash
git add FileExplorer/ViewModels/ProfileEditorViewModel.cs FileExplorer/Views/ProfileEditorWindow.axaml FileExplorer/Views/ProfileEditorWindow.axaml.cs FileExplorer.Tests/ProfileEditorViewModelTests.cs
git commit -m "feat(remote): connection profile editor dialog"
```

---

### Task 12: RemoteBrowserView e shell a tab

**Model:** `sonnet`

**Files:**
- Create: `FileExplorer/Views/RemoteBrowserView.axaml`
- Create: `FileExplorer/Views/RemoteBrowserView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml`
- Modify: `FileExplorer/Styles/Controls.axaml` (stile TabControl/TabItem se il default Fluent non basta: verifica prima visivamente)

**Interfaces:**
- Consumes: `RemoteBrowserViewModel` completo (Task 9-10), `ProfileEditorWindow` (Task 11), `SelectPathDialog` esistente (costruita con `SelectPathDialogViewModel(directoriesOnly: true, startPath)` e chiusa con il percorso scelto — leggi `FileExplorer/Views/SelectPathDialog.axaml.cs` per il contratto esatto del risultato prima di usarla).
- Produces: UI finale. Nessun nuovo contratto per altri task.

- [ ] **Step 1: Crea RemoteBrowserView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:models="clr-namespace:FileExplorer.Models"
             xmlns:conv="clr-namespace:FileExplorer.Converters"
             x:Class="FileExplorer.Views.RemoteBrowserView">

  <UserControl.Resources>
    <conv:EnumEqualsConverter x:Key="EnumEquals" />
  </UserControl.Resources>

  <DockPanel>

    <!-- Barra connessione -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <Grid ColumnDefinitions="Auto,*,Auto,Auto,Auto,Auto" ColumnSpacing="10">
        <i:Icon Grid.Column="0" Value="fa-solid fa-server" FontSize="20"
                Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
        <ComboBox Grid.Column="1" ItemsSource="{Binding Profiles}"
                  SelectedItem="{Binding SelectedProfile}"
                  MaxWidth="280" HorizontalAlignment="Left" VerticalAlignment="Center">
          <ComboBox.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding Name}" />
            </DataTemplate>
          </ComboBox.ItemTemplate>
        </ComboBox>
        <Button Grid.Column="2" Classes="onaccent" Click="OnManageProfilesClick"
                i:Attached.Icon="fa-solid fa-gear" ToolTip.Tip="Gestisci profili" />
        <Button Grid.Column="3" Classes="onaccent" Click="OnConnectClick"
                IsVisible="{Binding !IsConnected}">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plug" />
            <TextBlock Text="Connetti" />
          </StackPanel>
        </Button>
        <Button Grid.Column="4" Classes="onaccent" Click="OnDisconnectClick"
                IsVisible="{Binding IsConnected}">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plug-circle-xmark" />
            <TextBlock Text="Disconnetti" />
          </StackPanel>
        </Button>
        <Border Grid.Column="5" Classes="badge"
                Classes.success="{Binding IsConnected}"
                VerticalAlignment="Center">
          <TextBlock Text="{Binding IsConnected, Converter={x:Static BoolConverters.Not}, ConverterParameter=x, StringFormat={}{0}}"
                     IsVisible="False" />
        </Border>
      </Grid>
    </Border>

    <!-- Prompt password -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.SurfaceAlt}" Padding="20,10"
            IsVisible="{Binding IsPasswordPromptVisible}">
      <Grid ColumnDefinitions="Auto,*,Auto,Auto" ColumnSpacing="10">
        <i:Icon Grid.Column="0" Value="fa-solid fa-key" VerticalAlignment="Center"
                Foreground="{DynamicResource Brush.TextMuted}" />
        <TextBox Grid.Column="1" Text="{Binding PasswordInput}" PasswordChar="●"
                 Watermark="Password" />
        <CheckBox Grid.Column="2" Content="Salva nel keyring" IsChecked="{Binding SavePassword}"
                  IsVisible="{Binding CanSavePassword}" VerticalAlignment="Center" />
        <Button Grid.Column="3" Classes="primary" Content="Accedi" Click="OnConnectClick" />
      </Grid>
    </Border>

    <!-- Banner host key -->
    <Border DockPanel.Dock="Top" Classes="badge warning" Margin="20,8" Padding="12,8"
            HorizontalAlignment="Stretch"
            IsVisible="{Binding PendingFingerprint, Converter={x:Static ObjectConverters.IsNotNull}}">
      <StackPanel Spacing="6">
        <TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap" FontWeight="Bold" />
        <SelectableTextBlock Text="{Binding PendingFingerprint}" FontFamily="monospace" />
        <StackPanel Orientation="Horizontal" Spacing="8">
          <Button Classes="primary" Content="Accetta e connetti" Click="OnAcceptFingerprintClick" />
          <Button Classes="secondary" Content="Rifiuta" Click="OnRejectFingerprintClick" />
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Banner errore (non host key) -->
    <Border DockPanel.Dock="Top" Classes="badge error" Margin="20,8" Padding="12,8"
            HorizontalAlignment="Stretch">
      <Border.IsVisible>
        <MultiBinding Converter="{x:Static BoolConverters.And}">
          <Binding Path="ErrorMessage" Converter="{x:Static ObjectConverters.IsNotNull}" />
          <Binding Path="PendingFingerprint" Converter="{x:Static ObjectConverters.IsNull}" />
        </MultiBinding>
      </Border.IsVisible>
      <TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap" />
    </Border>

    <!-- Barra percorso -->
    <Border DockPanel.Dock="Top" Padding="20,8" IsVisible="{Binding IsConnected}">
      <Grid ColumnDefinitions="Auto,Auto,*" ColumnSpacing="8">
        <Button Grid.Column="0" Classes="iconbtn" i:Attached.Icon="fa-solid fa-arrow-up"
                Click="OnNavigateUpClick" ToolTip.Tip="Cartella superiore" />
        <Button Grid.Column="1" Classes="iconbtn" i:Attached.Icon="fa-solid fa-rotate"
                Click="OnRefreshClick" ToolTip.Tip="Aggiorna" />
        <TextBox Grid.Column="2" Text="{Binding CurrentPath}" IsReadOnly="True" />
      </Grid>
    </Border>

    <!-- Pannello filtri -->
    <Expander DockPanel.Dock="Top" Header="Filtri download" Margin="20,0"
              IsVisible="{Binding IsConnected}">
      <StackPanel Spacing="8" Margin="0,8">
        <Grid ColumnDefinitions="*,110,110" ColumnSpacing="10">
          <TextBox Grid.Column="0" Text="{Binding FilterPattern}"
                   Watermark="Pattern nome (es. *.jpg;report*)" />
          <TextBox Grid.Column="1" Text="{Binding FilterMinSizeKb}" Watermark="Min KB" />
          <TextBox Grid.Column="2" Text="{Binding FilterMaxSizeKb}" Watermark="Max KB" />
        </Grid>
        <Grid ColumnDefinitions="Auto,*,Auto,*" ColumnSpacing="10">
          <TextBlock Grid.Column="0" Text="Modificato dopo" VerticalAlignment="Center"
                     Foreground="{DynamicResource Brush.TextMuted}" />
          <DatePicker Grid.Column="1" SelectedDate="{Binding FilterModifiedAfter}" />
          <TextBlock Grid.Column="2" Text="prima di" VerticalAlignment="Center"
                     Foreground="{DynamicResource Brush.TextMuted}" />
          <DatePicker Grid.Column="3" SelectedDate="{Binding FilterModifiedBefore}" />
        </Grid>
        <StackPanel Orientation="Horizontal" Spacing="16">
          <CheckBox Content="Solo file mancanti su disco" IsChecked="{Binding OnlyMissing}" />
          <CheckBox Content="Includi sottocartelle" IsChecked="{Binding IncludeSubfolders}" />
          <CheckBox Content="Sovrascrivi sempre" IsChecked="{Binding OverwriteAlways}" />
        </StackPanel>
      </StackPanel>
    </Expander>

    <!-- Barra download (in basso) -->
    <Border DockPanel.Dock="Bottom" Background="{DynamicResource Brush.SurfaceAlt}" Padding="20,10"
            IsVisible="{Binding IsConnected}">
      <StackPanel Spacing="8">
        <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">
          <i:Icon Grid.Column="0" Value="fa-solid fa-folder" VerticalAlignment="Center"
                  Foreground="{DynamicResource Brush.TextMuted}" />
          <TextBox Grid.Column="1" Text="{Binding DestinationFolder}" IsReadOnly="True"
                   Watermark="Cartella di destinazione…" />
          <Button Grid.Column="2" Classes="iconbtn" i:Attached.Icon="fa-solid fa-magnifying-glass"
                  Click="OnBrowseDestinationClick" ToolTip.Tip="Scegli destinazione" />
        </Grid>
        <Grid ColumnDefinitions="*,Auto,Auto,Auto" ColumnSpacing="8">
          <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
            <ProgressBar Width="220" Minimum="0" Maximum="1" Value="{Binding DownloadProgressValue}"
                         IsVisible="{Binding IsDownloading}" />
            <TextBlock Text="{Binding DownloadStatusText}" VerticalAlignment="Center"
                       Foreground="{DynamicResource Brush.TextMuted}" />
            <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center"
                       Foreground="{DynamicResource Brush.TextMuted}"
                       IsVisible="{Binding !IsDownloading}" />
          </StackPanel>
          <Button Grid.Column="1" Classes="primary" Click="OnDownloadSelectedClick"
                  IsEnabled="{Binding !IsDownloading}">
            <StackPanel Orientation="Horizontal" Spacing="8">
              <i:Icon Value="fa-solid fa-download" />
              <TextBlock Text="Scarica selezionati" />
            </StackPanel>
          </Button>
          <Button Grid.Column="2" Classes="primary" Click="OnDownloadDirectoryClick"
                  IsEnabled="{Binding !IsDownloading}">
            <StackPanel Orientation="Horizontal" Spacing="8">
              <i:Icon Value="fa-solid fa-folder-tree" />
              <TextBlock Text="Scarica directory" />
            </StackPanel>
          </Button>
          <Button Grid.Column="3" Classes="secondary" Content="Annulla"
                  Click="OnCancelDownloadClick" IsEnabled="{Binding IsDownloading}" />
        </Grid>
      </StackPanel>
    </Border>

    <!-- Lista file -->
    <Panel Background="{DynamicResource Brush.Surface}">
      <StackPanel IsVisible="{Binding !IsConnected}" VerticalAlignment="Center"
                  HorizontalAlignment="Center" Spacing="12">
        <i:Icon Value="fa-solid fa-server" FontSize="52"
                Foreground="{DynamicResource Brush.TextMuted}" HorizontalAlignment="Center" />
        <TextBlock Text="Nessuna connessione attiva"
                   FontSize="16" Foreground="{DynamicResource Brush.TextMuted}"
                   HorizontalAlignment="Center" />
      </StackPanel>

      <ProgressBar IsIndeterminate="True" VerticalAlignment="Top"
                   IsVisible="{Binding IsBusy}" />

      <DataGrid x:Name="RemoteGrid"
                ItemsSource="{Binding VisibleItems}"
                SelectionMode="Extended"
                AutoGenerateColumns="False"
                IsReadOnly="True"
                IsVisible="{Binding IsConnected}"
                DoubleTapped="OnGridDoubleTapped"
                Margin="20,4">
        <DataGrid.Columns>
          <DataGridTemplateColumn Width="44">
            <DataGridTemplateColumn.CellTemplate>
              <DataTemplate>
                <Panel HorizontalAlignment="Center" VerticalAlignment="Center">
                  <i:Icon Value="fa-solid fa-folder" IsVisible="{Binding IsDirectory}"
                          Foreground="{DynamicResource Brush.WarningFg}" />
                  <i:Icon Value="fa-regular fa-file" IsVisible="{Binding !IsDirectory}"
                          Foreground="{DynamicResource Brush.TextMuted}" />
                </Panel>
              </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
          </DataGridTemplateColumn>
          <DataGridTextColumn Header="Nome" Binding="{Binding Name}" Width="*" />
          <DataGridTextColumn Header="Dimensione" Binding="{Binding SizeDisplay}" Width="110" />
          <DataGridTextColumn Header="Modificato" Binding="{Binding ModifiedDisplay}" Width="150" />
          <DataGridTemplateColumn Header="Su disco" Width="120">
            <DataGridTemplateColumn.CellTemplate>
              <DataTemplate>
                <Border Classes="badge" HorizontalAlignment="Left" VerticalAlignment="Center"
                        IsVisible="{Binding LocalStatus, Converter={x:Static ObjectConverters.IsNotNull}}"
                        Classes.success="{Binding LocalStatus, Converter={StaticResource EnumEquals}, ConverterParameter=Present}"
                        Classes.warning="{Binding LocalStatus, Converter={StaticResource EnumEquals}, ConverterParameter=Different}"
                        Classes.error="{Binding LocalStatus, Converter={StaticResource EnumEquals}, ConverterParameter=Missing}">
                  <TextBlock>
                    <TextBlock.Text>
                      <Binding Path="LocalStatus" />
                    </TextBlock.Text>
                  </TextBlock>
                </Border>
              </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
          </DataGridTemplateColumn>
        </DataGrid.Columns>
      </DataGrid>
    </Panel>

  </DockPanel>
</UserControl>
```

Nota: per la colonna "Su disco" valuta un piccolo converter enum→testo italiano
(Missing→"Mancante", Present→"Presente", Different→"Diverso") in `Converters/`
se il nome enum inglese in UI stona; scelta lasciata all'implementatore, coerenza col resto dell'app.

- [ ] **Step 2: Crea il code-behind**

`FileExplorer/Views/RemoteBrowserView.axaml.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class RemoteBrowserView : UserControl
{
    private readonly RemoteBrowserViewModel _viewModel;

    public RemoteBrowserView()
    {
        InitializeComponent();
        _viewModel = new RemoteBrowserViewModel();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadProfilesAsync();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.ConnectAsync();

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DisconnectAsync();

    private async void OnNavigateUpClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.NavigateUpAsync();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void OnAcceptFingerprintClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AcceptFingerprintAsync();

    private void OnRejectFingerprintClick(object? sender, RoutedEventArgs e) =>
        _viewModel.RejectFingerprint();

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RemoteGrid.SelectedItem is RemoteEntryViewModel entry && entry.IsDirectory)
            await _viewModel.OpenDirectoryAsync(entry);
    }

    private async void OnDownloadSelectedClick(object? sender, RoutedEventArgs e)
    {
        var selected = RemoteGrid.SelectedItems.Cast<RemoteEntryViewModel>().ToList();
        if (selected.Count > 0)
            await _viewModel.DownloadSelectedAsync(selected);
    }

    private async void OnDownloadDirectoryClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.DownloadCurrentDirectoryAsync();

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelDownload();

    private async void OnBrowseDestinationClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        // Contratto verificato di SelectPathDialog: costruttore senza parametri,
        // DataContext assegnato dal chiamante, ShowDialog<string?> ritorna il percorso o null.
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly: true,
                startPath: _viewModel.DestinationFolder
                           ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
        };
        var result = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(result))
            _viewModel.DestinationFolder = result;
    }

    private async void OnManageProfilesClick(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null)
            return;

        var profile = _viewModel.SelectedProfile ?? new Models.ConnectionProfile();
        bool isNew = _viewModel.SelectedProfile is null;

        var editor = new ProfileEditorWindow(
            new ProfileEditorViewModel(profile, Services.CredentialStoreFactory.Create()));
        bool saved = await editor.ShowDialog<bool>(owner);

        if (saved)
        {
            if (isNew)
                _viewModel.Profiles.Add(profile);
            await Services.ProfileStore.SaveAsync(Services.ProfileStore.DefaultPath,
                _viewModel.Profiles.ToList());
            if (isNew)
                _viewModel.SelectedProfile = profile;
        }
    }
}
```

Nota per l'implementatore:
Aggiungi anche un pulsante "Nuovo profilo" se `OnManageProfilesClick` con profilo esistente
non basta: minimo indispensabile = creare il primo profilo e modificarne uno esistente.

- [ ] **Step 3: Shell a tab in MainWindow**

Sostituisci il contenuto di `FileExplorer/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        xmlns:views="clr-namespace:FileExplorer.Views"
        x:Class="FileExplorer.Views.MainWindow"
        Title="File Explorer"
        Width="900" Height="640"
        MinWidth="640" MinHeight="480"
        Background="{DynamicResource Brush.Surface}">

  <TabControl Padding="0">
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-copy" />
          <TextBlock Text="Copia" />
        </StackPanel>
      </TabItem.Header>
      <views:CopyPairsView />
    </TabItem>
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-server" />
          <TextBlock Text="Server remoto" />
        </StackPanel>
      </TabItem.Header>
      <views:RemoteBrowserView />
    </TabItem>
  </TabControl>

</Window>
```

- [ ] **Step 4: Build, test e avvio manuale**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln`
Expected: build pulita, tutti PASS.

Run: `dotnet run --project FileExplorer.Desktop` (chiudi dopo la verifica)
Verifica visiva minima:
- Le due tab appaiono e si cambia tab senza errori.
- La tab "Server remoto" mostra lo stato disconnesso.
- "Gestisci profili" apre l'editor, la validazione mostra errori, un profilo salvato appare nella ComboBox e sopravvive al riavvio dell'app.
- I colori seguono il tema (nessun colore hardcoded).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Views/RemoteBrowserView.axaml FileExplorer/Views/RemoteBrowserView.axaml.cs FileExplorer/Views/MainWindow.axaml FileExplorer/Styles/Controls.axaml
git commit -m "feat(remote): remote browser tab UI and tabbed main window shell"
```

---

### Task 13: Verifica finale, smoke test e pull request

**Model:** `sonnet`

**Files:**
- Modify: `docs/superpowers/plans/2026-08-15-remote-browser.md` (spunta finale)

**Interfaces:**
- Consumes: tutto.
- Produces: PR aperta.

- [ ] **Step 1: Suite completa e build**

Run: `dotnet build FileExplorer.sln && dotnet test FileExplorer.sln`
Expected: 0 warning, 0 errori, tutti i test PASS.

- [ ] **Step 2: Smoke test manuale contro un server reale (locale)**

Avvia un server SFTP di test in container (una porta libera qualsiasi):

```bash
podman run --rm -d --name fe-sftp -p 2222:22 docker.io/atmoz/sftp:latest testuser:testpass:::upload
# oppure 'docker run' con gli stessi argomenti
```

Poi `dotnet run --project FileExplorer.Desktop` e verifica:
1. Nuovo profilo SFTP `localhost:2222`, utente `testuser` → alla prima connessione appare il banner fingerprint → Accetta → connesso, si vede `upload/`.
2. Copiare 2-3 file in `upload/` nel container (`podman exec fe-sftp sh -c 'echo ciao > /home/testuser/upload/a.txt'`), Refresh, scaricarli in una cartella di destinazione → appaiono su disco, badge "Presente" dopo il download.
3. Secondo download identico → tutto "saltato".
4. Filtro `*.txt` esclude/include correttamente.
5. Password salvata nel keyring (se disponibile): riavvio app + riconnessione senza prompt.
6. Server spento (`podman stop fe-sftp`) → connessione fallisce con errore chiaro, UI reattiva.

Se un passo fallisce: correggi (bug fix con test dove possibile) prima di procedere.

- [ ] **Step 3: Spunta il piano e committa**

Tutte le checkbox di questo file spuntate; commit:

```bash
git add docs/superpowers/plans/2026-08-15-remote-browser.md
git commit -m "docs: mark remote browser plan complete"
```

- [ ] **Step 4: Push e PR**

```bash
git push -u origin feature/remote-browser
gh pr create --title "feat(remote): FTP/SFTP remote browser tab with filtered downloads" --body "$(cat <<'EOF'
## Summary
- Nuova tab "Server remoto": connessione FTP/FTPS/SFTP con profili salvati (password nel keyring OS, mai su file)
- Navigazione directory remote, download di file selezionati o della directory aperta (ricorsione opzionale)
- Filtri download: pattern nome, dimensione, data, solo mancanti; check "già presente su disco" (nome+dimensione+data) con skip automatico e report scaricati/saltati/falliti
- Verifica host key SFTP con accettazione esplicita della fingerprint (protezione MITM)
- Spec: docs/superpowers/specs/2026-08-15-remote-browser-design.md

## Test plan
- [x] Suite xunit completa (`dotnet test`)
- [x] Smoke test manuale contro server SFTP in container (atmoz/sftp)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR aperta verso `main`.

---

## Note di manutenzione del piano

- Dopo OGNI task completato e revisionato: spuntare le sue checkbox in questo file e committare insieme al codice del task (o subito dopo).
- Se un'API di FluentFTP/SSH.NET differisce da quella scritta qui, il contratto di `IRemoteFileClient` e la mappatura errori restano invariati: adattare solo l'interno del client.
- FTP/FTPS ed elenco keyring reali non hanno test automatici: coperti dallo smoke test del Task 13.
