# Impostazioni copia + parallelismo adattivo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adattare il parallelismo di copia cartelle al tipo di disco (SSD/HDD) di sorgente e destinazione, ed esporre una tab "Impostazioni" per configurare parallelismo, buffer size, verifica checksum e tema.

**Architecture:** Nessun DI container (coerente con il resto dell'app): un servizio statico `AppSettingsStore` persiste `AppSettings` in JSON su AppData (stesso pattern di `ProfileStore`). `DiskTypeService` rileva SSD/HDD per OS con cache 5 minuti; `CopyParallelismResolver` è una funzione pura che decide il grado di parallelismo. `CopyPairsViewModel` consuma questi servizi solo all'avvio della copia. `SettingsViewModel` (nuova tab) espone le proprietà di `AppSettingsStore.Current` con auto-save.

**Tech Stack:** .NET 8, Avalonia 11.2.8, ReactiveUI, System.Text.Json, System.Management (Windows WMI), xunit.

**Spec:** `docs/superpowers/specs/2026-08-16-copy-settings-design.md`

## Global Constraints

- .NET 8, MVVM/ReactiveUI, Avalonia UI — segue `FileExplorer.sln` a livello repo.
- Layering: `Views` → `ViewModels` → `Services` (statici) → `Models` (dati semplici). Nessun DI container.
- Colori mai hardcoded nelle view: solo `{DynamicResource Brush.*}`. Icone via Projektanker (`fa-*`).
- Classi di stile esistenti: `Button.primary/.secondary/.iconbtn/.onaccent`, `Border.card`, `Border.badge.*`.
- Test in `FileExplorer.Tests` (xunit), eseguiti con `dotnet test`. Nessuna CI.
- `dotnet format whitespace` gira automaticamente su file `.cs`/`.axaml` modificati (hook), non serve lanciarlo a mano.
- Git: mai commit diretti su `main`; lavorare su branch feature. Niente Claude come co-author.

---

### Task 1: Modello AppSettings + persistenza AppSettingsStore + wiring avvio

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Models/AppSettings.cs`
- Create: `FileExplorer/Services/AppSettingsStore.cs`
- Modify: `FileExplorer/App.axaml.cs`
- Test: `FileExplorer.Tests/AppSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  - `FileExplorer.Models.AppSettings` — classe con `AutoParallelism` (bool, default true), `ManualParallelism` (int, default `Math.Max(2, Environment.ProcessorCount - 1)`), `BufferSizeBytes` (int, default `1024*1024`), `VerifyChecksumAfterCopy` (bool, default true), `ThemeVariant` (string, default `"Default"`).
  - `FileExplorer.Services.AppSettingsStore`:
    - `static string DefaultPath { get; }`
    - `static string CurrentPath { get; set; }` (default = `DefaultPath`, sovrascrivibile nei test)
    - `static AppSettings Current { get; set; }`
    - `static Task LoadCurrentAsync()`
    - `static Task SaveCurrentAsync()`
    - `static Task<AppSettings> LoadAsync(string path)`
    - `static Task SaveAsync(string path, AppSettings settings)`

- [ ] **Step 1: Scrivi i test per AppSettingsStore**

```csharp
// FileExplorer.Tests/AppSettingsStoreTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public AppSettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string StorePath => Path.Combine(_root, "sub", "settings.json");

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaults()
    {
        var settings = await AppSettingsStore.LoadAsync(StorePath);
        Assert.True(settings.AutoParallelism);
        Assert.True(settings.VerifyChecksumAfterCopy);
        Assert.Equal("Default", settings.ThemeVariant);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsAllFields()
    {
        var settings = new AppSettings
        {
            AutoParallelism = false,
            ManualParallelism = 12,
            BufferSizeBytes = 4 * 1024 * 1024,
            VerifyChecksumAfterCopy = false,
            ThemeVariant = "Dark"
        };

        await AppSettingsStore.SaveAsync(StorePath, settings);
        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.False(loaded.AutoParallelism);
        Assert.Equal(12, loaded.ManualParallelism);
        Assert.Equal(4 * 1024 * 1024, loaded.BufferSizeBytes);
        Assert.False(loaded.VerifyChecksumAfterCopy);
        Assert.Equal("Dark", loaded.ThemeVariant);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, "{ non-json !!!");

        var settings = await AppSettingsStore.LoadAsync(StorePath);
        Assert.True(settings.AutoParallelism);
    }

    [Fact]
    public async Task LoadCurrentAsync_UsesCurrentPath()
    {
        AppSettingsStore.CurrentPath = StorePath;
        await AppSettingsStore.SaveAsync(StorePath, new AppSettings { ManualParallelism = 9 });

        await AppSettingsStore.LoadCurrentAsync();

        Assert.Equal(9, AppSettingsStore.Current.ManualParallelism);
    }

    [Fact]
    public async Task SaveCurrentAsync_WritesCurrentToCurrentPath()
    {
        AppSettingsStore.CurrentPath = StorePath;
        AppSettingsStore.Current = new AppSettings { ManualParallelism = 15 };

        await AppSettingsStore.SaveCurrentAsync();
        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.Equal(15, loaded.ManualParallelism);
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano (i tipi non esistono ancora)**

Run: `dotnet test --filter AppSettingsStoreTests`
Expected: FAIL (build error, `AppSettings`/`AppSettingsStore` non trovati)

- [ ] **Step 3: Crea il modello AppSettings**

```csharp
// FileExplorer/Models/AppSettings.cs
using System;

namespace FileExplorer.Models;

/// <summary>Impostazioni utente persistite su disco: parallelismo copia, buffer, checksum, tema.</summary>
public class AppSettings
{
    public bool AutoParallelism { get; set; } = true;
    public int ManualParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);
    public int BufferSizeBytes { get; set; } = 1024 * 1024;
    public bool VerifyChecksumAfterCopy { get; set; } = true;

    /// <summary>"Default" (segue il sistema), "Light" o "Dark".</summary>
    public string ThemeVariant { get; set; } = "Default";
}
```

- [ ] **Step 4: Crea AppSettingsStore**

```csharp
// FileExplorer/Services/AppSettingsStore.cs
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza delle impostazioni applicative in JSON (AppData), stesso pattern di
/// <see cref="ProfileStore"/>. Espone anche <see cref="Current"/>, l'istanza in memoria
/// caricata all'avvio e usata da tutto il resto dell'app.
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Percorso predefinito del file impostazioni.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "settings.json");

    /// <summary>
    /// Percorso usato da <see cref="LoadCurrentAsync"/> e <see cref="SaveCurrentAsync"/>.
    /// Sovrascrivibile nei test per non toccare l'AppData reale.
    /// </summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>Istanza in memoria delle impostazioni correnti.</summary>
    public static AppSettings Current { get; set; } = new();

    /// <summary>Carica le impostazioni da <see cref="CurrentPath"/> in <see cref="Current"/>.</summary>
    public static async Task LoadCurrentAsync()
    {
        Current = await LoadAsync(CurrentPath);
    }

    /// <summary>Salva <see cref="Current"/> su <see cref="CurrentPath"/>.</summary>
    public static Task SaveCurrentAsync() => SaveAsync(CurrentPath, Current);

    /// <summary>Carica le impostazioni; default se il file manca o è illeggibile.</summary>
    public static async Task<AppSettings> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options)
                   ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Salva le impostazioni creando la cartella se assente.</summary>
    public static async Task SaveAsync(string path, AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, Options);
    }
}
```

- [ ] **Step 5: Esegui i test e verifica che passino**

Run: `dotnet test --filter AppSettingsStoreTests`
Expected: PASS (5 test)

- [ ] **Step 6: Carica le impostazioni e applica il tema all'avvio in App.axaml.cs**

```csharp
// FileExplorer/App.axaml.cs
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FileExplorer.Services;
using FileExplorer.ViewModels;
using FileExplorer.Views;

namespace FileExplorer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettingsStore.LoadCurrentAsync().GetAwaiter().GetResult();
            RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ThemeVariant ParseThemeVariant(string value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
```

- [ ] **Step 7: Build completa per verificare che l'app compili ancora**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori

- [ ] **Step 8: Commit**

```bash
git add FileExplorer/Models/AppSettings.cs FileExplorer/Services/AppSettingsStore.cs FileExplorer/App.axaml.cs FileExplorer.Tests/AppSettingsStoreTests.cs
git commit -m "feat(settings): aggiungi modello AppSettings e persistenza AppSettingsStore"
```

---

### Task 2: Buffer size configurabile in FileCopyService

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs`
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs`

**Interfaces:**
- Consumes: nessuna dipendenza da altri task.
- Produces:
  - `FileCopyService.CopyFileAsync(string sourcePath, string destinationPath, Action<long>? onBytesCopied, CancellationToken ct, int bufferSize = 1024*1024)`
  - `FileCopyService.CopyDirectoryAsync(string sourceRoot, string destinationRoot, int maxDegreeOfParallelism, Action<CopyProgress>? onProgress, CancellationToken ct, int bufferSize = 1024*1024)`
  - Entrambi i nuovi parametri sono opzionali e in coda: le chiamate esistenti restano invariate.

- [ ] **Step 1: Scrivi il test per il buffer size personalizzato**

```csharp
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
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: FAIL (l'overload con `bufferSize` non esiste)

- [ ] **Step 3: Aggiungi il parametro bufferSize a FileCopyService**

In `FileExplorer/Services/FileCopyService.cs`, sostituisci il campo costante e le firme dei due metodi:

```csharp
    private const int DefaultBufferSize = 1024 * 1024; // 1 MB

    /// <summary>
    /// Copia un singolo file a blocchi, segnalando i byte copiati a ogni blocco.
    /// </summary>
    public static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[bufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytesCopied?.Invoke(read);
        }

        await output.FlushAsync(ct);
    }
```

E aggiorna `CopyDirectoryAsync` per accettare e propagare lo stesso parametro:

```csharp
    public static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
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
                string destinationFile = Path.Combine(destinationRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                await CopyFileAsync(sourceFile, destinationFile, deltaBytes =>
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

- [ ] **Step 4: Esegui i test e verifica che passino**

Run: `dotnet test --filter FileCopyServiceTests`
Expected: PASS (2 test)

- [ ] **Step 5: Esegui l'intera suite per verificare che nessuna chiamata esistente si sia rotta**

Run: `dotnet test`
Expected: PASS, tutti i test verdi (nessuna regressione sulle chiamate esistenti a `CopyFileAsync`/`CopyDirectoryAsync`)

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "feat(copy): rendi configurabile la dimensione del buffer di copia"
```

---

### Task 3: DiskType + CopyParallelismResolver (logica pura)

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Models/DiskType.cs`
- Create: `FileExplorer/Services/CopyParallelismResolver.cs`
- Test: `FileExplorer.Tests/CopyParallelismResolverTests.cs`

**Interfaces:**
- Consumes: `FileExplorer.Models.AppSettings` (Task 1).
- Produces:
  - `FileExplorer.Models.DiskType` — enum `{ Unknown, Ssd, Hdd }`.
  - `FileExplorer.Services.CopyParallelismResolver.Resolve(AppSettings settings, DiskType sourceType, DiskType destinationType) → int`.

- [ ] **Step 1: Scrivi i test per CopyParallelismResolver**

```csharp
// FileExplorer.Tests/CopyParallelismResolverTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CopyParallelismResolverTests
{
    [Theory]
    [InlineData(DiskType.Hdd, DiskType.Ssd)]
    [InlineData(DiskType.Ssd, DiskType.Hdd)]
    [InlineData(DiskType.Hdd, DiskType.Hdd)]
    public void Resolve_Auto_EitherDiskIsHdd_ReturnsOne(DiskType source, DiskType destination)
    {
        var settings = new AppSettings { AutoParallelism = true };
        Assert.Equal(1, CopyParallelismResolver.Resolve(settings, source, destination));
    }

    [Theory]
    [InlineData(DiskType.Ssd, DiskType.Ssd)]
    [InlineData(DiskType.Ssd, DiskType.Unknown)]
    [InlineData(DiskType.Unknown, DiskType.Unknown)]
    public void Resolve_Auto_NeitherDiskIsHdd_ReturnsProcessorBasedValue(DiskType source, DiskType destination)
    {
        var settings = new AppSettings { AutoParallelism = true };
        int expected = Math.Max(2, Environment.ProcessorCount - 1);
        Assert.Equal(expected, CopyParallelismResolver.Resolve(settings, source, destination));
    }

    [Fact]
    public void Resolve_Manual_ReturnsConfiguredValue()
    {
        var settings = new AppSettings { AutoParallelism = false, ManualParallelism = 6 };
        Assert.Equal(6, CopyParallelismResolver.Resolve(settings, DiskType.Hdd, DiskType.Hdd));
    }

    [Fact]
    public void Resolve_Manual_ClampsBelowOneToOne()
    {
        var settings = new AppSettings { AutoParallelism = false, ManualParallelism = 0 };
        Assert.Equal(1, CopyParallelismResolver.Resolve(settings, DiskType.Ssd, DiskType.Ssd));
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test --filter CopyParallelismResolverTests`
Expected: FAIL (`DiskType`/`CopyParallelismResolver` non esistono)

- [ ] **Step 3: Crea l'enum DiskType**

```csharp
// FileExplorer/Models/DiskType.cs
namespace FileExplorer.Models;

/// <summary>Tipo di disco fisico su cui risiede un percorso, per adattare il parallelismo di copia.</summary>
public enum DiskType
{
    Unknown,
    Ssd,
    Hdd
}
```

- [ ] **Step 4: Crea CopyParallelismResolver**

```csharp
// FileExplorer/Services/CopyParallelismResolver.cs
using System;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Decide il grado di parallelismo per la copia di una cartella, in base alle
/// impostazioni utente e al tipo di disco di sorgente/destinazione.
/// </summary>
public static class CopyParallelismResolver
{
    /// <summary>
    /// In automatico: 1 (sequenziale) se sorgente o destinazione sono su HDD, altrimenti
    /// ProcessorCount-1. In manuale: il valore impostato dall'utente (clampato a >= 1).
    /// </summary>
    public static int Resolve(AppSettings settings, DiskType sourceType, DiskType destinationType)
    {
        if (!settings.AutoParallelism)
            return Math.Max(1, settings.ManualParallelism);

        bool eitherHdd = sourceType == DiskType.Hdd || destinationType == DiskType.Hdd;
        return eitherHdd ? 1 : Math.Max(2, Environment.ProcessorCount - 1);
    }
}
```

- [ ] **Step 5: Esegui i test e verifica che passino**

Run: `dotnet test --filter CopyParallelismResolverTests`
Expected: PASS (7 test)

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Models/DiskType.cs FileExplorer/Services/CopyParallelismResolver.cs FileExplorer.Tests/CopyParallelismResolverTests.cs
git commit -m "feat(copy): aggiungi DiskType e CopyParallelismResolver"
```

---

### Task 4: DiskTypeService — rilevamento SSD/HDD cross-platform

**Model:** opus

**Files:**
- Create: `FileExplorer/Services/DiskTypeService.cs`
- Modify: `FileExplorer/FileExplorer.csproj`
- Test: `FileExplorer.Tests/DiskTypeServiceTests.cs`

**Interfaces:**
- Consumes: `FileExplorer.Models.DiskType` (Task 3).
- Produces:
  - `DiskTypeService.GetDiskTypeAsync(string? path, CancellationToken ct) → Task<DiskType>` — non lancia mai eccezioni, cache 5 minuti per drive root.
  - Helper `internal static` testabili senza chiamate OS reali: `ResolveLinuxBlockDevice(string mountsContent, string absolutePath)`, `ExtractLinuxDiskName(string device)`, `ParseRotationalFlag(string content)`, `ParseWindowsMediaType(int mediaType)`, `ParseDiskutilSolidState(string diskutilOutput)`.

**Nota:** il rilevamento reale Windows/macOS non è eseguibile in questo ambiente di sviluppo (Linux). I test coprono la logica di parsing pura con stringhe iniettate; i metodi che fanno I/O reale (lettura `/proc/mounts`, WMI, `diskutil`) sono avvolti in try/catch e ritornano `Unknown` su qualunque fallimento, per design.

- [ ] **Step 1: Aggiungi la dipendenza System.Management (necessaria solo su Windows a runtime)**

In `FileExplorer/FileExplorer.csproj`, nel gruppo `<ItemGroup>` dei `PackageReference` esistenti, aggiungi:

```xml
    <PackageReference Include="System.Management" Version="8.0.0" />
```

- [ ] **Step 2: Scrivi i test per la logica di parsing pura**

```csharp
// FileExplorer.Tests/DiskTypeServiceTests.cs
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
}
```

- [ ] **Step 3: Esegui i test e verifica che falliscano**

Run: `dotnet test --filter DiskTypeServiceTests`
Expected: FAIL (`DiskTypeService` non esiste)

- [ ] **Step 4: Crea DiskTypeService**

```csharp
// FileExplorer/Services/DiskTypeService.cs
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Rileva se un percorso risiede su SSD o HDD, per adattare il parallelismo di copia.
/// Non lancia mai eccezioni: qualunque fallimento di rilevamento ritorna <see cref="DiskType.Unknown"/>.
/// </summary>
public static class DiskTypeService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (DiskType Type, DateTime CachedAt)> Cache = new();

    public static async Task<DiskType> GetDiskTypeAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DiskType.Unknown;

        string cacheKey = Path.GetPathRoot(path) is { Length: > 0 } root ? root : path;

        if (Cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached.Type;

        DiskType type;
        try
        {
            type = await DetectAsync(path, ct);
        }
        catch
        {
            type = DiskType.Unknown;
        }

        Cache[cacheKey] = (type, DateTime.UtcNow);
        return type;
    }

    private static Task<DiskType> DetectAsync(string path, CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectLinuxAsync(path, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(DetectWindows(path));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectMacAsync(path, ct);

        return Task.FromResult(DiskType.Unknown);
    }

    // ===== Linux =====

    private static async Task<DiskType> DetectLinuxAsync(string path, CancellationToken ct)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists("/proc/mounts"))
            return DiskType.Unknown;

        string mountsContent = await File.ReadAllTextAsync("/proc/mounts", ct);
        string? device = ResolveLinuxBlockDevice(mountsContent, fullPath);
        if (device is null)
            return DiskType.Unknown;

        string? diskName = ExtractLinuxDiskName(device);
        if (diskName is null)
            return DiskType.Unknown;

        string rotationalPath = $"/sys/block/{diskName}/queue/rotational";
        if (!File.Exists(rotationalPath))
            return DiskType.Unknown;

        string content = await File.ReadAllTextAsync(rotationalPath, ct);
        return ParseRotationalFlag(content);
    }

    /// <summary>
    /// Trova il device montato con il prefisso più lungo che contiene <paramref name="absolutePath"/>,
    /// leggendo il contenuto di /proc/mounts. Ritorna null se nessun device corrisponde (es. FS di rete).
    /// </summary>
    internal static string? ResolveLinuxBlockDevice(string mountsContent, string absolutePath)
    {
        string? bestDevice = null;
        int bestLength = -1;

        foreach (string line in mountsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
                continue;

            string device = fields[0];
            string mountPoint = fields[1];

            if (!device.StartsWith("/dev/", StringComparison.Ordinal))
                continue;

            bool matches = absolutePath == mountPoint
                || absolutePath.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal)
                || mountPoint == "/";

            if (!matches)
                continue;

            if (mountPoint.Length > bestLength)
            {
                bestLength = mountPoint.Length;
                bestDevice = device;
            }
        }

        return bestDevice;
    }

    /// <summary>
    /// Estrae il nome del disco (per /sys/block) da un device di partizione,
    /// es. "/dev/sda1" -> "sda", "/dev/nvme0n1p1" -> "nvme0n1". Ritorna null per
    /// device non riconosciuti (mapper/LVM, di rete, ecc.).
    /// </summary>
    internal static string? ExtractLinuxDiskName(string device)
    {
        string name = device.StartsWith("/dev/", StringComparison.Ordinal) ? device[5..] : device;

        var nvmeMatch = Regex.Match(name, @"^(nvme\d+n\d+)(p\d+)?$");
        if (nvmeMatch.Success)
            return nvmeMatch.Groups[1].Value;

        var diskMatch = Regex.Match(name, @"^([a-z]+)\d*$");
        if (diskMatch.Success)
            return diskMatch.Groups[1].Value;

        return null;
    }

    /// <summary>Interpreta il contenuto di /sys/block/&lt;disco&gt;/queue/rotational.</summary>
    internal static DiskType ParseRotationalFlag(string content)
    {
        return content.Trim() switch
        {
            "0" => DiskType.Ssd,
            "1" => DiskType.Hdd,
            _ => DiskType.Unknown
        };
    }

    // ===== Windows =====

    private static DiskType DetectWindows(string path)
    {
        string? driveLetter = Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(driveLetter))
            return DiskType.Unknown;

        try
        {
            using var logicalDiskSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");

            foreach (ManagementBaseObject partition in logicalDiskSearcher.Get())
            {
                using var driveSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");

                foreach (ManagementBaseObject drive in driveSearcher.Get())
                {
                    string? index = drive["Index"]?.ToString();
                    if (index is null)
                        continue;

                    using var physicalDiskSearcher = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage",
                        $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{index}'");

                    foreach (ManagementBaseObject physicalDisk in physicalDiskSearcher.Get())
                    {
                        if (physicalDisk["MediaType"] is ushort mediaType)
                            return ParseWindowsMediaType(mediaType);
                    }
                }
            }
        }
        catch
        {
            return DiskType.Unknown;
        }

        return DiskType.Unknown;
    }

    /// <summary>Interpreta MSFT_PhysicalDisk.MediaType (3 = HDD, 4 = SSD).</summary>
    internal static DiskType ParseWindowsMediaType(int mediaType) => mediaType switch
    {
        3 => DiskType.Hdd,
        4 => DiskType.Ssd,
        _ => DiskType.Unknown
    };

    // ===== macOS =====

    private static async Task<DiskType> DetectMacAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("diskutil", $"info \"{Path.GetFullPath(path)}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
                return DiskType.Unknown;

            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return ParseDiskutilSolidState(output);
        }
        catch
        {
            return DiskType.Unknown;
        }
    }

    /// <summary>Interpreta l'output di "diskutil info" cercando il campo "Solid State".</summary>
    internal static DiskType ParseDiskutilSolidState(string diskutilOutput)
    {
        var match = Regex.Match(diskutilOutput, @"Solid State:\s*(Yes|No)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return DiskType.Unknown;

        return string.Equals(match.Groups[1].Value, "Yes", StringComparison.OrdinalIgnoreCase)
            ? DiskType.Ssd
            : DiskType.Hdd;
    }
}
```

- [ ] **Step 5: Esegui i test e verifica che passino**

Run: `dotnet test --filter DiskTypeServiceTests`
Expected: PASS (tutti i test)

- [ ] **Step 6: Build completa per verificare che il nuovo pacchetto NuGet si ripristini correttamente**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori

- [ ] **Step 7: Commit**

```bash
git add FileExplorer/Services/DiskTypeService.cs FileExplorer/FileExplorer.csproj FileExplorer.Tests/DiskTypeServiceTests.cs
git commit -m "feat(copy): aggiungi rilevamento SSD/HDD cross-platform (DiskTypeService)"
```

---

### Task 5: Integra buffer/checksum/parallelismo in CopyPairsViewModel

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs`

**Interfaces:**
- Consumes:
  - `AppSettingsStore.Current` (Task 1)
  - `FileCopyService.CopyFileAsync(..., int bufferSize = ...)` / `CopyDirectoryAsync(..., int bufferSize = ...)` (Task 2)
  - `CopyParallelismResolver.Resolve(AppSettings, DiskType, DiskType)` (Task 3)
  - `DiskTypeService.GetDiskTypeAsync(string?, CancellationToken)` (Task 4)
- Produces: `CopyPairsViewModel.StartCopyAsync(FolderFilePairViewModel pair)` diventa `public` (era `private`), per essere invocabile direttamente dai test — stesso pattern già usato da `RemoteBrowserViewModel.ConnectAsync`/`UploadFilesAsync`.

- [ ] **Step 1: Scrivi i test per checksum toggle e parallelismo**

```csharp
// FileExplorer.Tests/CopyPairsViewModelTests.cs
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class CopyPairsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;

    public CopyPairsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copypairs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task StartCopy_SingleFile_ChecksumEnabled_VerifiesAndMarksSuccess()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = true;

        string sourceFile = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "contenuto di prova");
        string destinationFile = Path.Combine(_root, "dest.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destinationFile };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(pair.IsVerified);
        Assert.Equal("contenuto di prova", await File.ReadAllTextAsync(destinationFile));
    }

    [Fact]
    public async Task StartCopy_SingleFile_ChecksumDisabled_SkipsVerificationButCopiesFile()
    {
        AppSettingsStore.Current.VerifyChecksumAfterCopy = false;

        string sourceFile = Path.Combine(_root, "source2.txt");
        await File.WriteAllTextAsync(sourceFile, "altro contenuto");
        string destinationFile = Path.Combine(_root, "dest2.txt");

        var pair = new FolderFilePairViewModel { SourcePath = sourceFile, DestinationPath = destinationFile };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.Null(pair.IsVerified);
        Assert.Equal("Completato", pair.Status);
        Assert.Equal("altro contenuto", await File.ReadAllTextAsync(destinationFile));
    }

    [Fact]
    public async Task StartCopy_Directory_CopiesAllFilesWithResolvedParallelism()
    {
        string sourceDir = Path.Combine(_root, "srcdir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "b");
        string destinationDir = Path.Combine(_root, "dstdir");

        var pair = new FolderFilePairViewModel { SourcePath = sourceDir, DestinationPath = destinationDir };
        await pair.SourceStateRefresh;

        var vm = new CopyPairsViewModel();
        await vm.StartCopyAsync(pair);

        Assert.Equal(CopyStateKind.Success, pair.StateKind);
        Assert.True(File.Exists(Path.Combine(destinationDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(destinationDir, "b.txt")));
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test --filter CopyPairsViewModelTests`
Expected: FAIL (`StartCopyAsync` non è accessibile dal test, checksum toggle ignorato)

- [ ] **Step 3: Rendi StartCopyAsync pubblico**

In `FileExplorer/ViewModels/CopyPairsViewModel.cs`, cambia la firma:

```csharp
    public async Task StartCopyAsync(FolderFilePairViewModel pair)
```

(era `private async Task StartCopyAsync(FolderFilePairViewModel pair)`)

- [ ] **Step 4: Applica checksum toggle e buffer size in CopySingleFileAsync**

Sostituisci il corpo del metodo con:

```csharp
    private static async Task CopySingleFileAsync(FolderFilePairViewModel pair, CancellationToken ct)
    {
        // Se la sorgente è un file e la destinazione una cartella, il file viene copiato dentro la cartella.
        bool isFileCopyToFolder = await FileSystemService.GetPathTypeAsync(pair.DestinationPath) == PathType.Directory;
        string destinationPath = isFileCopyToFolder
            ? Path.Combine(pair.DestinationPath!, Path.GetFileName(pair.SourcePath!))
            : pair.DestinationPath!;

        long totalBytes = new FileInfo(pair.SourcePath!).Length;
        long copiedBytes = 0;

        await FileCopyService.CopyFileAsync(pair.SourcePath!, destinationPath, deltaBytes =>
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

        // Verifica checksum dopo la copia.
        pair.Status = "Verifica checksum…";
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);
        pair.DestinationChecksum = await ChecksumService.ComputeSha256Async(destinationPath, ct);
        pair.IsVerified = string.Equals(pair.SourceChecksum, pair.DestinationChecksum, StringComparison.OrdinalIgnoreCase);

        pair.Progress = 1;
        pair.Status = pair.IsVerified == true ? "Completato" : "Completato (checksum non corrisponde)";
        pair.StateKind = pair.IsVerified == true ? CopyStateKind.Success : CopyStateKind.Warning;
    }
```

- [ ] **Step 5: Applica rilevamento disco + buffer size in CopyDirectoryAsync**

Sostituisci il corpo del metodo con:

```csharp
    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, CancellationToken ct)
    {
        int knownFileCount = -1;

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        var destinationType = await DiskTypeService.GetDiskTypeAsync(pair.DestinationPath, ct);
        int parallelism = CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType);

        await FileCopyService.CopyDirectoryAsync(
            pair.SourcePath!,
            pair.DestinationPath!,
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

        if (!ct.IsCancellationRequested && knownFileCount != 0)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
        }
        else if (knownFileCount == 0)
        {
            pair.StateKind = CopyStateKind.Ready;
        }
    }
```

- [ ] **Step 6: Esegui i test e verifica che passino**

Run: `dotnet test --filter CopyPairsViewModelTests`
Expected: PASS (3 test)

- [ ] **Step 7: Esegui l'intera suite per verificare che non ci siano regressioni**

Run: `dotnet test`
Expected: PASS, tutti i test verdi

- [ ] **Step 8: Commit**

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer.Tests/CopyPairsViewModelTests.cs
git commit -m "feat(copy): applica parallelismo adattivo, buffer e checksum opzionale alla copia"
```

---

### Task 6: SettingsViewModel (auto-save)

**Model:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/SettingsViewModel.cs`
- Test: `FileExplorer.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettingsStore` (Task 1).
- Produces: `SettingsViewModel` (ReactiveObject) con proprietà `AutoParallelism` (bool), `ManualParallelism` (int, clampato 1-32), `BufferSizeKb` (int, clampato 256-16384, mappa `AppSettings.BufferSizeBytes`), `VerifyChecksumAfterCopy` (bool), `ThemeVariant` (string), `IsThemeDefault`/`IsThemeLight`/`IsThemeDark` (bool, mutuamente esclusivi via `ThemeVariant`).

- [ ] **Step 1: Scrivi i test per SettingsViewModel**

```csharp
// FileExplorer.Tests/SettingsViewModelTests.cs
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public SettingsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-settingsvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void AutoParallelism_Set_UpdatesCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.AutoParallelism = false;
        Assert.False(AppSettingsStore.Current.AutoParallelism);
    }

    [Fact]
    public void ManualParallelism_SetOutOfRange_ClampsTo1To32()
    {
        var vm = new SettingsViewModel();
        vm.ManualParallelism = 100;
        Assert.Equal(32, AppSettingsStore.Current.ManualParallelism);

        vm.ManualParallelism = 0;
        Assert.Equal(1, AppSettingsStore.Current.ManualParallelism);
    }

    [Fact]
    public void BufferSizeKb_Set_UpdatesBufferSizeBytesOnCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.BufferSizeKb = 4096;
        Assert.Equal(4096 * 1024, AppSettingsStore.Current.BufferSizeBytes);
    }

    [Fact]
    public void VerifyChecksumAfterCopy_Toggle_UpdatesCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.VerifyChecksumAfterCopy = false;
        Assert.False(AppSettingsStore.Current.VerifyChecksumAfterCopy);
    }

    [Fact]
    public void IsThemeDark_SetTrue_UpdatesThemeVariantAndPeers()
    {
        var vm = new SettingsViewModel();
        vm.IsThemeDark = true;

        Assert.Equal("Dark", AppSettingsStore.Current.ThemeVariant);
        Assert.True(vm.IsThemeDark);
        Assert.False(vm.IsThemeLight);
        Assert.False(vm.IsThemeDefault);
    }

    [Fact]
    public async Task PropertyChange_PersistsToDiskAsynchronously()
    {
        var vm = new SettingsViewModel();
        vm.ManualParallelism = 8;

        for (int i = 0; i < 50 && !File.Exists(AppSettingsStore.CurrentPath); i++)
            await Task.Delay(20);

        var saved = await AppSettingsStore.LoadAsync(AppSettingsStore.CurrentPath);
        Assert.Equal(8, saved.ManualParallelism);
    }
}
```

- [ ] **Step 2: Esegui i test e verifica che falliscano**

Run: `dotnet test --filter SettingsViewModelTests`
Expected: FAIL (`SettingsViewModel` non esiste)

- [ ] **Step 3: Crea SettingsViewModel**

```csharp
// FileExplorer/ViewModels/SettingsViewModel.cs
using System;
using System.Threading.Tasks;
using Avalonia;
using FileExplorer.Services;
using ReactiveUI;
using AvaloniaThemeVariant = Avalonia.Styling.ThemeVariant;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Impostazioni": espone le proprietà di <see cref="AppSettingsStore.Current"/>
/// con auto-save ad ogni modifica (nessun bottone "Salva").
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    public bool AutoParallelism
    {
        get => AppSettingsStore.Current.AutoParallelism;
        set
        {
            if (AppSettingsStore.Current.AutoParallelism == value)
                return;

            AppSettingsStore.Current.AutoParallelism = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public int ManualParallelism
    {
        get => AppSettingsStore.Current.ManualParallelism;
        set
        {
            int clamped = Math.Clamp(value, 1, 32);
            if (AppSettingsStore.Current.ManualParallelism == clamped)
                return;

            AppSettingsStore.Current.ManualParallelism = clamped;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    /// <summary>Dimensione del buffer di copia in KB (leggibile in UI), 256-16384. Mappa BufferSizeBytes.</summary>
    public int BufferSizeKb
    {
        get => AppSettingsStore.Current.BufferSizeBytes / 1024;
        set
        {
            int clampedKb = Math.Clamp(value, 256, 16384);
            int bytes = clampedKb * 1024;
            if (AppSettingsStore.Current.BufferSizeBytes == bytes)
                return;

            AppSettingsStore.Current.BufferSizeBytes = bytes;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public bool VerifyChecksumAfterCopy
    {
        get => AppSettingsStore.Current.VerifyChecksumAfterCopy;
        set
        {
            if (AppSettingsStore.Current.VerifyChecksumAfterCopy == value)
                return;

            AppSettingsStore.Current.VerifyChecksumAfterCopy = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public string ThemeVariant
    {
        get => AppSettingsStore.Current.ThemeVariant;
        set
        {
            if (AppSettingsStore.Current.ThemeVariant == value)
                return;

            AppSettingsStore.Current.ThemeVariant = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsThemeDefault));
            this.RaisePropertyChanged(nameof(IsThemeLight));
            this.RaisePropertyChanged(nameof(IsThemeDark));
            ApplyThemeVariant(value);
            SaveCurrent();
        }
    }

    public bool IsThemeDefault
    {
        get => ThemeVariant == "Default";
        set { if (value) ThemeVariant = "Default"; }
    }

    public bool IsThemeLight
    {
        get => ThemeVariant == "Light";
        set { if (value) ThemeVariant = "Light"; }
    }

    public bool IsThemeDark
    {
        get => ThemeVariant == "Dark";
        set { if (value) ThemeVariant = "Dark"; }
    }

    private static void ApplyThemeVariant(string value)
    {
        try
        {
            if (Application.Current is null)
                return;

            Application.Current.RequestedThemeVariant = value switch
            {
                "Light" => AvaloniaThemeVariant.Light,
                "Dark" => AvaloniaThemeVariant.Dark,
                _ => AvaloniaThemeVariant.Default
            };
        }
        catch (Exception)
        {
            // applicazione del tema opzionale: un fallimento qui non deve rompere il salvataggio.
        }
    }

    private static void SaveCurrent()
    {
        _ = SaveCurrentAsync();
    }

    private static async Task SaveCurrentAsync()
    {
        try
        {
            await AppSettingsStore.SaveCurrentAsync();
        }
        catch (Exception)
        {
            // best effort: le impostazioni restano valide in memoria anche se il salvataggio su disco fallisce.
        }
    }
}
```

- [ ] **Step 4: Esegui i test e verifica che passino**

Run: `dotnet test --filter SettingsViewModelTests`
Expected: PASS (6 test)

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer.Tests/SettingsViewModelTests.cs
git commit -m "feat(settings): aggiungi SettingsViewModel con auto-save"
```

---

### Task 7: Tab "Impostazioni" (SettingsView + MainWindow)

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Views/SettingsView.axaml`
- Create: `FileExplorer/Views/SettingsView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `SettingsViewModel` (Task 6).
- Produces: nuova tab "Impostazioni" visibile in `MainWindow`.

- [ ] **Step 1: Crea SettingsView.axaml.cs**

```csharp
// FileExplorer/Views/SettingsView.axaml.cs
using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>Scheda "Impostazioni": parametri di copia e aspetto.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}
```

- [ ] **Step 2: Crea SettingsView.axaml**

```xml
<!-- FileExplorer/Views/SettingsView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             x:Class="FileExplorer.Views.SettingsView">

  <DockPanel>

    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <i:Icon Value="fa-solid fa-gear" FontSize="20" Foreground="{DynamicResource Brush.OnAccent}" />
        <TextBlock Text="Impostazioni" FontSize="18" FontWeight="Bold" Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
      </StackPanel>
    </Border>

    <ScrollViewer Background="{DynamicResource Brush.Surface}">
      <StackPanel Margin="20" MaxWidth="560" HorizontalAlignment="Left">

        <Border Classes="card">
          <StackPanel Spacing="14">
            <TextBlock Text="Copia" FontSize="15" FontWeight="SemiBold" Foreground="{DynamicResource Brush.TextPrimary}" />

            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="Parallelismo automatico (rileva SSD/HDD)"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <ToggleSwitch Grid.Column="1" IsChecked="{Binding AutoParallelism}" />
            </Grid>

            <Grid ColumnDefinitions="*,Auto" IsEnabled="{Binding !AutoParallelism}">
              <TextBlock Grid.Column="0" Text="Thread di copia (manuale, 1-32)"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <NumericUpDown Grid.Column="1" Width="120" Minimum="1" Maximum="32" Increment="1"
                             Value="{Binding ManualParallelism}" />
            </Grid>

            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="Dimensione buffer copia (KB)"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <NumericUpDown Grid.Column="1" Width="140" Minimum="256" Maximum="16384" Increment="256"
                             Value="{Binding BufferSizeKb}" />
            </Grid>

            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="Verifica checksum dopo la copia"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <ToggleSwitch Grid.Column="1" IsChecked="{Binding VerifyChecksumAfterCopy}" />
            </Grid>
          </StackPanel>
        </Border>

        <Border Classes="card">
          <StackPanel Spacing="14">
            <TextBlock Text="Aspetto" FontSize="15" FontWeight="SemiBold" Foreground="{DynamicResource Brush.TextPrimary}" />

            <StackPanel Orientation="Horizontal" Spacing="16">
              <RadioButton GroupName="Theme" Content="Sistema" IsChecked="{Binding IsThemeDefault}" />
              <RadioButton GroupName="Theme" Content="Chiaro" IsChecked="{Binding IsThemeLight}" />
              <RadioButton GroupName="Theme" Content="Scuro" IsChecked="{Binding IsThemeDark}" />
            </StackPanel>
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>

  </DockPanel>

</UserControl>
```

- [ ] **Step 3: Aggiungi la tab in MainWindow.axaml**

In `FileExplorer/Views/MainWindow.axaml`, aggiungi una terza `TabItem` prima della chiusura di `</TabControl>`:

```xml
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-gear" />
          <TextBlock Text="Impostazioni" />
        </StackPanel>
      </TabItem.Header>
      <views:SettingsView />
    </TabItem>
```

- [ ] **Step 4: Build per verificare che l'XAML compili**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Views/SettingsView.axaml FileExplorer/Views/SettingsView.axaml.cs FileExplorer/Views/MainWindow.axaml
git commit -m "feat(settings): aggiungi tab Impostazioni alla finestra principale"
```

---

### Task 8: Verifica finale (build, test suite completa, avvio app)

**Model:** sonnet

**Files:** nessuno (solo verifica)

**Interfaces:** nessuna — task di sola verifica end-to-end.

- [ ] **Step 1: Build completa della solution**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori, 0 warning nuovi

- [ ] **Step 2: Esegui l'intera suite di test**

Run: `dotnet test`
Expected: PASS, tutti i test verdi (inclusi i preesistenti — nessuna regressione)

- [ ] **Step 3: Avvia l'app e verifica che non crashi all'avvio con la tab Impostazioni**

Run:
```bash
timeout 8 dotnet run --project FileExplorer.Desktop > /tmp/fe-smoke.log 2>&1
echo "exit code: $?"
cat /tmp/fe-smoke.log
```

Expected: nessuna eccezione non gestita nell'output/log (l'app resta in esecuzione fino al timeout, che è il comportamento atteso per un processo GUI senza crash — `exit code` da `timeout` per un processo ancora vivo è 124).

Se l'ambiente ha un display disponibile (X11/Wayland), verifica visivamente aprendo l'app che:
- la tab "Impostazioni" è presente e mostra le due card "Copia" e "Aspetto"
- toggle "Parallelismo automatico" disabilita/abilita il campo "Thread di copia"
- cambiare il tema (Sistema/Chiaro/Scuro) aggiorna live i colori dell'interfaccia
- una copia file/cartella nella tab "Copia" completa ancora correttamente

- [ ] **Step 4: Nessun commit in questo task** (solo verifica; se emergono difetti, tornare al task pertinente, correggere lì e committare)
