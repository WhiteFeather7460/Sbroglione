# Quattro quick-win IDEE (4, 6, 9, 19) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementare i punti 4 (throttling I/O configurabile), 6 (dry-run/simulazione), 9 (report di confronto esportabile) e 19 (grafico velocità in tempo reale) di `IDEE.md`.

**Architecture:** Si estende il pattern esistente: servizi statici in `Services/`, ViewModel ReactiveUI in `ViewModels/`, viste axaml in `Views/`. Il throttling è un token-bucket globale (`IoThrottleService`) chiamato dai loop di copia di `FileCopyService`; il dry-run è un servizio puro di enumerazione (`CopySimulationService`); il confronto è una nuova tab con servizio a cascata dimensione→hash (`DirectoryComparisonService`) ed esportatore HTML/CSV/JSON; il grafico velocità è un tracker puro (`SpeedTracker`) alimentato dai callback di progresso già esistenti più un controllo sparkline custom (pattern `TreemapControl`).

**Tech Stack:** .NET 8, Avalonia 11, ReactiveUI, xunit, Projektanker.Icons.Avalonia (FontAwesome), System.Text.Json.

**Spec:** `IDEE.md` (punti 4, 6, 9, 19).

## Global Constraints

- .NET 8, `dotnet build FileExplorer.sln`, test con `dotnet test`.
- Layering: Views → ViewModels → Services → Models. Nessun DI container. Servizi statici (classi helper istanziabili solo se pure e testabili, es. `TokenBucket`, `SpeedTracker`).
- Mai colori hardcoded nelle viste: sempre `{DynamicResource Brush.*}` da `Styles/Palette.axaml` (ThemeDictionaries Light+Dark). Icone via `i:Icon` / `i:Attached.Icon` con `fa-*`.
- Stringhe UI e commenti al codice in italiano (convenzione del codebase).
- Mai commit su `main`: ogni fase lavora su un branch dedicato e termina con una PR. Niente co-author Claude nei commit.
- Test: xunit, classi `sealed` + `IDisposable`, directory temporanee `Path.Combine(Path.GetTempPath(), "fe-<nome>-" + Guid.NewGuid().ToString("N"))`, stato statico (`AppSettingsStore.Current`, `CurrentPath`) salvato nel costruttore e ripristinato in `Dispose()`. Niente array inline ripetuti nei test dove scatta CA1861: hoistare in `private static readonly`.
- Enumerazioni di filesystem sempre con `EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }` (evita crash su symlink-loop e non salta Hidden/System).
- ViewModel con `CancellationTokenSource` implementano `IDisposable` (Cancel + Dispose + `GC.SuppressFinalize`); nei blocchi `finally` il dispose del CTS è null-condizionale (`_cts?.Dispose()`).
- Ogni task dichiara il modello per il subagente esecutore (`haiku` = meccanico, `sonnet` = standard, `opus` = logica complessa); il dispatcher lo passa al tool Agent.
- Al termine di ogni task: spuntare i checkbox del task in questo file.

**Branch per fase:**
- Fase 1: `feature/io-throttling` (Task 1–3) — da `main` aggiornato (post-merge stack precedente)
- Fase 2: `feature/dry-run` (Task 4–5) — stacked su Fase 1
- Fase 3: `feature/speed-graph` (Task 6–8) — stacked su Fase 2
- Fase 4: `feature/compare-report` (Task 9–12) — da `main` (nessun file in comune con Fasi 1–3 tranne `MainWindow.axaml`, toccato solo qui)

Ogni fase termina con `gh pr create` (base = branch precedente per le fasi stacked, `main` per la Fase 4) e con l'aggiornamento del punto IDEE corrispondente a `[x]`.

---

## Fase 1 — Throttling I/O configurabile (IDEE punto 4)

Stato attuale: `FileCopyService.CopyFileAsync` e `CopyFileToManyAsync` copiano a blocchi da `bufferSize` byte senza alcun limite di banda. Le impostazioni (`AppSettings` + `SettingsViewModel` + `SettingsView`) seguono il pattern auto-save.

### Task 1: TokenBucket + IoThrottleService

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/IoThrottleService.cs`
- Test: `FileExplorer.Tests/TokenBucketTests.cs`

**Interfaces:**
- Produces: `TokenBucket(Func<double> nowSeconds)` con `double BytesPerSecond { get; set; }` (0 = illimitato) e `double ReserveOrWaitSeconds(long bytes)`; `IoThrottleService.WaitAsync(long bytes, CancellationToken ct)` statico che legge `AppSettingsStore.Current` a ogni chiamata.

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class TokenBucketTests
{
    [Fact]
    public void ReserveOrWaitSeconds_RateZero_AlwaysGrantsImmediately()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 0 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(long.MaxValue));
    }

    [Fact]
    public void ReserveOrWaitSeconds_WithinBudget_GrantsImmediately()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        // Il bucket parte pieno (burst di 1 secondo = 1000 byte).
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
    }

    [Fact]
    public void ReserveOrWaitSeconds_BudgetExhausted_ReturnsWaitTime()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        // Bucket vuoto: altri 500 byte richiedono 0.5 s di attesa.
        double wait = bucket.ReserveOrWaitSeconds(500);
        Assert.Equal(0.5, wait, precision: 3);
    }

    [Fact]
    public void ReserveOrWaitSeconds_RefillsWithTime()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        now = 1.0; // dopo 1 s il bucket è di nuovo pieno.
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
    }

    [Fact]
    public void ReserveOrWaitSeconds_RefillCappedAtOneSecondBurst()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        now = 10.0; // il refill non accumula oltre 1 s di burst.
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        double wait = bucket.ReserveOrWaitSeconds(1000);
        Assert.Equal(1.0, wait, precision: 3);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TokenBucketTests"`
Expected: FAIL (compile error: `TokenBucket` non esiste)

- [x] **Step 3: Write the implementation**

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Token bucket per il limite di banda: accumula "byte spendibili" al ritmo di
/// <see cref="BytesPerSecond"/>, con burst massimo di 1 secondo. Thread-safe.
/// Il clock è iniettabile per i test.
/// </summary>
public sealed class TokenBucket
{
    private readonly Func<double> _nowSeconds;
    private readonly object _gate = new();
    private double _available;
    private double _lastRefill;
    private double _bytesPerSecond;

    public TokenBucket(Func<double> nowSeconds)
    {
        _nowSeconds = nowSeconds;
        _lastRefill = nowSeconds();
    }

    /// <summary>Byte al secondo; 0 (o negativo) = nessun limite.</summary>
    public double BytesPerSecond
    {
        get { lock (_gate) return _bytesPerSecond; }
        set
        {
            lock (_gate)
            {
                _bytesPerSecond = value;
                // Cambio limite: il bucket riparte pieno per evitare attese spurie.
                _available = value;
                _lastRefill = _nowSeconds();
            }
        }
    }

    /// <summary>
    /// Prova a spendere <paramref name="bytes"/>: restituisce 0 se concessi subito,
    /// altrimenti i secondi da attendere prima di riprovare. La spesa avviene comunque
    /// (il saldo può andare negativo): un blocco già letto va scritto in ogni caso.
    /// </summary>
    public double ReserveOrWaitSeconds(long bytes)
    {
        lock (_gate)
        {
            if (_bytesPerSecond <= 0)
                return 0;

            double now = _nowSeconds();
            _available = Math.Min(_bytesPerSecond, _available + (now - _lastRefill) * _bytesPerSecond);
            _lastRefill = now;

            _available -= bytes;
            return _available >= 0 ? 0 : -_available / _bytesPerSecond;
        }
    }
}

/// <summary>
/// Limite di banda globale della copia: unico bucket condiviso da tutte le copie
/// in corso, pilotato dalle impostazioni (<see cref="Models.AppSettings.ThrottleEnabled"/>
/// e <see cref="Models.AppSettings.ThrottleMBps"/>) rilette a ogni chiamata,
/// così il toggle rapido ha effetto immediato sulle copie già avviate.
/// </summary>
public static class IoThrottleService
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly TokenBucket Bucket = new(() => Clock.Elapsed.TotalSeconds);

    public static async Task WaitAsync(long bytes, CancellationToken ct)
    {
        var settings = AppSettingsStore.Current;
        double rate = settings.ThrottleEnabled ? settings.ThrottleMBps * 1024.0 * 1024.0 : 0;
        if (Math.Abs(Bucket.BytesPerSecond - rate) > 0.5)
            Bucket.BytesPerSecond = rate;

        if (rate <= 0)
            return;

        double waitSeconds = Bucket.ReserveOrWaitSeconds(bytes);
        if (waitSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
    }
}
```

Nota: `AppSettings.ThrottleEnabled`/`ThrottleMBps` non esistono ancora — vengono aggiunti in questo stesso task (Step 4) perché `IoThrottleService` non compila senza.

- [x] **Step 4: Add the settings fields**

In `FileExplorer/Models/AppSettings.cs` aggiungere dopo `VerifyChecksumAfterCopy`:

```csharp
    /// <summary>Limite di banda della copia attivo (toggle rapido nella scheda Copia).</summary>
    public bool ThrottleEnabled { get; set; }

    /// <summary>Limite di banda in MB/s (usato solo se <see cref="ThrottleEnabled"/>).</summary>
    public int ThrottleMBps { get; set; } = 50;
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TokenBucketTests"`
Expected: PASS (5 test)

- [x] **Step 6: Build clean and commit**

Run: `dotnet build FileExplorer.sln` — 0 errori, nessun warning nuovo.

```bash
git add FileExplorer/Services/IoThrottleService.cs FileExplorer/Models/AppSettings.cs FileExplorer.Tests/TokenBucketTests.cs
git commit -m "feat(throttle): token bucket e servizio globale di limite banda"
```

### Task 2: Hook del throttle nei loop di copia

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs` (loop di `CopyFileAsync` e `CopyFileToManyAsync`)
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs` (aggiunta)

**Interfaces:**
- Consumes: `IoThrottleService.WaitAsync(long bytes, CancellationToken ct)` (Task 1).
- Produces: nessuna firma nuova — il throttle è trasparente per i chiamanti.

- [x] **Step 1: Write the failing test**

Aggiungere a `FileCopyServiceTests` (rispettando i pattern esistenti della classe: directory temporanea, salvataggio/ripristino di `AppSettingsStore.Current` se la classe non lo fa già — verificarlo e in caso aggiungere il salvataggio nel costruttore/`Dispose`):

```csharp
    [Fact]
    public async Task CopyFileAsync_WithThrottleEnabled_TakesAtLeastExpectedTime()
    {
        // 2 MB a 1 MB/s: il burst iniziale copre ~1 MB, il resto attende ~1 s.
        string source = Path.Combine(_tempDir, "big.bin");
        string destination = Path.Combine(_tempDir, "big-copy.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);

        AppSettingsStore.Current.ThrottleEnabled = true;
        AppSettingsStore.Current.ThrottleMBps = 1;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await FileCopyService.CopyFileAsync(source, destination, null, CancellationToken.None);
        stopwatch.Stop();

        AppSettingsStore.Current.ThrottleEnabled = false;

        Assert.True(stopwatch.Elapsed.TotalSeconds >= 0.5,
            $"Copia troppo veloce con throttle attivo: {stopwatch.Elapsed.TotalSeconds:F2}s");
        Assert.Equal(2 * 1024 * 1024, new FileInfo(destination).Length);
    }
```

Nota anti-flakiness: la soglia è 0.5 s su un'attesa teorica di ~1 s — il test verifica che il throttle rallenti, non il tempo esatto.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CopyFileAsync_WithThrottleEnabled"`
Expected: FAIL (la copia termina in millisecondi)

- [x] **Step 3: Hook the throttle**

In `FileCopyService.CopyFileAsync`, dentro il loop di lettura, come prima istruzione dopo la `ReadAsync`:

```csharp
        while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await IoThrottleService.WaitAsync(read, ct);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytesCopied?.Invoke(read);
        }
```

Stessa aggiunta nel loop di `CopyFileToManyAsync` (prima della `Task.WhenAll` delle scritture). I byte sono contati una sola volta per blocco letto (non per destinazione): il limite è sulla lettura della sorgente, coerente con `onBytesCopied`.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FileCopyServiceTests"`
Expected: PASS (tutti, inclusi i preesistenti — con throttle disattivo il comportamento è identico)

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "feat(throttle): limite di banda applicato ai loop di copia"
```

### Task 3: UI impostazioni + toggle rapido nella scheda Copia

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/SettingsViewModel.cs`
- Modify: `FileExplorer/Views/SettingsView.axaml` (card "Copia")
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Modify: `FileExplorer/Views/CopyPairsView.axaml` (header della scheda)
- Modify: `IDEE.md` (punto 4 → `[x]`)
- Test: `FileExplorer.Tests/SettingsViewModelTests.cs` (aggiunta), `FileExplorer.Tests/CopyPairsViewModelTests.cs` (aggiunta)

**Interfaces:**
- Consumes: `AppSettings.ThrottleEnabled` / `ThrottleMBps` (Task 1).
- Produces: `SettingsViewModel.ThrottleEnabled` (bool), `SettingsViewModel.ThrottleMBps` (int, clamp 1–1000); `CopyPairsViewModel.ThrottleEnabled` (bool, stesso storage, auto-save) e `CopyPairsViewModel.ThrottleMBps` (int, clamp 1–1000, auto-save).

- [x] **Step 1: Write the failing tests**

In `SettingsViewModelTests` (seguire il pattern esistente della classe per save/restore dello stato statico):

```csharp
    [Fact]
    public async Task ThrottleMBps_ClampsAndPersists()
    {
        var viewModel = new SettingsViewModel();

        viewModel.ThrottleMBps = 5000;
        Assert.Equal(1000, AppSettingsStore.Current.ThrottleMBps);

        viewModel.ThrottleMBps = 0;
        Assert.Equal(1, AppSettingsStore.Current.ThrottleMBps);

        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;
    }

    [Fact]
    public void ThrottleEnabled_WritesSetting()
    {
        var viewModel = new SettingsViewModel();

        viewModel.ThrottleEnabled = true;
        Assert.True(AppSettingsStore.Current.ThrottleEnabled);
    }
```

In `CopyPairsViewModelTests`:

```csharp
    [Fact]
    public void ThrottleEnabled_RoundTripsThroughSettings()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleEnabled = true;
        Assert.True(AppSettingsStore.Current.ThrottleEnabled);

        viewModel.ThrottleEnabled = false;
        Assert.False(AppSettingsStore.Current.ThrottleEnabled);
    }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Throttle"`
Expected: FAIL (compile error: proprietà inesistenti)

- [x] **Step 3: Implement the ViewModel properties**

In `SettingsViewModel`, dopo `VerifyChecksumAfterCopy`, stesso pattern auto-save:

```csharp
    public bool ThrottleEnabled
    {
        get => AppSettingsStore.Current.ThrottleEnabled;
        set
        {
            if (AppSettingsStore.Current.ThrottleEnabled == value)
                return;

            AppSettingsStore.Current.ThrottleEnabled = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public int ThrottleMBps
    {
        get => AppSettingsStore.Current.ThrottleMBps;
        set
        {
            int clamped = Math.Clamp(value, 1, 1000);
            if (AppSettingsStore.Current.ThrottleMBps == clamped)
                return;

            AppSettingsStore.Current.ThrottleMBps = clamped;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }
```

In `CopyPairsViewModel` (il toggle rapido scrive le stesse impostazioni; il salvataggio è best-effort fire-and-forget come da pattern):

```csharp
    /// <summary>Toggle rapido del limite di banda (scrive le impostazioni, effetto immediato sulle copie in corso).</summary>
    public bool ThrottleEnabled
    {
        get => AppSettingsStore.Current.ThrottleEnabled;
        set
        {
            if (AppSettingsStore.Current.ThrottleEnabled == value)
                return;

            AppSettingsStore.Current.ThrottleEnabled = value;
            this.RaisePropertyChanged();
            _ = SaveSettingsBestEffortAsync();
        }
    }

    /// <summary>Limite MB/s modificabile al volo dalla scheda Copia.</summary>
    public int ThrottleMBps
    {
        get => AppSettingsStore.Current.ThrottleMBps;
        set
        {
            int clamped = Math.Clamp(value, 1, 1000);
            if (AppSettingsStore.Current.ThrottleMBps == clamped)
                return;

            AppSettingsStore.Current.ThrottleMBps = clamped;
            this.RaisePropertyChanged();
            _ = SaveSettingsBestEffortAsync();
        }
    }

    private static async Task SaveSettingsBestEffortAsync()
    {
        try
        {
            await AppSettingsStore.SaveCurrentAsync();
        }
        catch (Exception)
        {
            // best effort: il limite resta attivo in memoria anche se il salvataggio fallisce.
        }
    }
```

- [x] **Step 4: Add the Settings UI**

In `SettingsView.axaml`, nella card "Copia", dopo la riga "Verifica checksum dopo la copia":

```xml
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="Limita velocità di copia"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <ToggleSwitch Grid.Column="1" IsChecked="{Binding ThrottleEnabled}" />
            </Grid>

            <Grid ColumnDefinitions="*,Auto" IsEnabled="{Binding ThrottleEnabled}">
              <TextBlock Grid.Column="0" Text="Limite di banda (MB/s, 1-1000)"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <NumericUpDown Grid.Column="1" Width="140" Minimum="1" Maximum="1000" Increment="5"
                             Value="{Binding ThrottleMBps}" />
            </Grid>
```

- [x] **Step 5: Add the quick toggle in CopyPairsView**

In `CopyPairsView.axaml`, l'header è `<Border DockPanel.Dock="Top" …><Grid ColumnDefinitions="*,Auto">` con il bottone "Aggiungi coppia" in colonna 1. Estendere a `ColumnDefinitions="*,Auto,Auto"`, spostare il bottone esistente in `Grid.Column="2"` e inserire in colonna 1 il toggle rapido (il DataContext dell'header è già `CopyPairsViewModel`):

```xml
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center" Margin="0,0,14,0">
          <i:Icon Value="fa-solid fa-gauge-high" Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
          <NumericUpDown Width="110" Minimum="1" Maximum="1000" Increment="5"
                         Value="{Binding ThrottleMBps}" IsEnabled="{Binding ThrottleEnabled}" />
          <TextBlock Text="MB/s" VerticalAlignment="Center" Foreground="{DynamicResource Brush.OnAccent}" />
          <ToggleSwitch IsChecked="{Binding ThrottleEnabled}" ToolTip.Tip="Limita velocità di copia" />
        </StackPanel>
```

(l'header ha sfondo `Brush.AccentGradient`: i testi/icone usano `Brush.OnAccent` come gli elementi già presenti.)

- [x] **Step 6: Run the full suite, mark IDEE, commit**

Run: `dotnet test`
Expected: PASS (nessuna regressione)

In `IDEE.md` cambiare il punto 4 da `[ ]` a `[x]`.

```bash
git add FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer/Views/SettingsView.axaml FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/Views/CopyPairsView.axaml FileExplorer.Tests/SettingsViewModelTests.cs FileExplorer.Tests/CopyPairsViewModelTests.cs IDEE.md
git commit -m "feat(throttle): slider impostazioni e toggle rapido nella scheda Copia"
```

Fine fase: push del branch e `gh pr create` (base `main`, titolo "Throttling I/O configurabile (IDEE #4)").

---

## Fase 2 — Dry-run / simulazione operazioni (IDEE punto 6)

Stacked su Fase 1. Stato attuale: `CopyPairsViewModel.StartCopyAsync` avvia direttamente la copia; nessuna anteprima. `FileCopyService.IsUnchanged` è privato.

### Task 4: CopySimulationService

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/CopySimulationService.cs`
- Modify: `FileExplorer/Services/FileCopyService.cs` (visibilità di `IsUnchanged`: `private` → `internal`)
- Test: `FileExplorer.Tests/CopySimulationServiceTests.cs`

**Interfaces:**
- Consumes: `FileCopyService.IsUnchanged(string sourceFile, string destinationFile)` (reso `internal`; il progetto ha già `InternalsVisibleTo` per i test).
- Produces:

```csharp
public sealed record DestinationSimulation(string Root, int OverwriteCount, long? FreeBytes, bool? Fits);
public sealed record CopySimulationResult(
    int TotalFiles, long TotalBytes, int SkippedFiles,
    IReadOnlyList<DestinationSimulation> Destinations);
public static class CopySimulationService
{
    public static Task<CopySimulationResult> SimulateAsync(
        string sourcePath, IReadOnlyList<string> destinationRoots,
        bool skipUnchanged, CancellationToken ct);
}
```

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class CopySimulationServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-simulate-" + Guid.NewGuid().ToString("N"));

    public CopySimulationServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SimulateAsync_Directory_CountsFilesBytesAndOverwrites()
    {
        string source = Path.Combine(_tempDir, "src");
        string destination = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(source, "sub", "b.bin"), new byte[20]);
        // "a.bin" esiste già in destinazione: è una sovrascrittura.
        await File.WriteAllBytesAsync(Path.Combine(destination, "a.bin"), new byte[99]);

        var result = await CopySimulationService.SimulateAsync(
            source, new[] { destination }, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(30, result.TotalBytes);
        Assert.Equal(0, result.SkippedFiles);
        var dest = Assert.Single(result.Destinations);
        Assert.Equal(1, dest.OverwriteCount);
        Assert.NotNull(dest.FreeBytes);
        Assert.True(dest.Fits);
    }

    [Fact]
    public async Task SimulateAsync_SkipUnchanged_CountsUnchangedAsSkipped()
    {
        string source = Path.Combine(_tempDir, "src2");
        string destination = Path.Combine(_tempDir, "dst2");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);

        string sourceFile = Path.Combine(source, "same.bin");
        string destinationFile = Path.Combine(destination, "same.bin");
        await File.WriteAllBytesAsync(sourceFile, new byte[10]);
        File.Copy(sourceFile, destinationFile);
        File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));

        var result = await CopySimulationService.SimulateAsync(
            source, new[] { destination }, skipUnchanged: true, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.SkippedFiles);
    }

    [Fact]
    public async Task SimulateAsync_SingleFile_Works()
    {
        string sourceFile = Path.Combine(_tempDir, "single.bin");
        string destination = Path.Combine(_tempDir, "dst3");
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(sourceFile, new byte[42]);

        var result = await CopySimulationService.SimulateAsync(
            sourceFile, new[] { destination }, skipUnchanged: false, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(42, result.TotalBytes);
        Assert.Equal(0, Assert.Single(result.Destinations).OverwriteCount);
    }
}
```

Attenzione CA1861: se l'analyzer segnala gli array inline `new[] { destination }`, hoistarli è impossibile (dipendono dal tempdir) — in quel caso usare variabili locali `string[] destinations = { destination };`.

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CopySimulationServiceTests"`
Expected: FAIL (compile error)

- [x] **Step 3: Write the implementation**

In `FileCopyService.cs` cambiare `private static bool IsUnchanged` in `internal static bool IsUnchanged` (nessun altro cambio).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Esito della simulazione per una singola destinazione.</summary>
/// <param name="Root">Radice della destinazione.</param>
/// <param name="OverwriteCount">File che verrebbero sovrascritti.</param>
/// <param name="FreeBytes">Spazio libero sul volume, null se non determinabile (es. percorsi di rete).</param>
/// <param name="Fits">True se lo spazio libero copre i byte da copiare; null se FreeBytes è null.</param>
public sealed record DestinationSimulation(string Root, int OverwriteCount, long? FreeBytes, bool? Fits);

/// <summary>Esito complessivo della simulazione (dry-run) di una copia.</summary>
public sealed record CopySimulationResult(
    int TotalFiles,
    long TotalBytes,
    int SkippedFiles,
    IReadOnlyList<DestinationSimulation> Destinations);

/// <summary>
/// Dry-run di una copia: enumera cosa verrebbe copiato/sovrascritto/saltato e
/// verifica lo spazio disponibile per destinazione, senza scrivere nulla.
/// </summary>
public static class CopySimulationService
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static Task<CopySimulationResult> SimulateAsync(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        CancellationToken ct)
    {
        return Task.Run(() => Simulate(sourcePath, destinationRoots, skipUnchanged, ct), ct);
    }

    private static CopySimulationResult Simulate(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        CancellationToken ct)
    {
        bool isDirectory = Directory.Exists(sourcePath);

        // Coppie (path sorgente, path relativo) da esaminare.
        List<(string Source, string Relative)> files = isDirectory
            ? Directory.EnumerateFiles(sourcePath, "*", SafeEnumeration)
                .Select(f => (f, Path.GetRelativePath(sourcePath, f)))
                .ToList()
            : new List<(string, string)> { (sourcePath, Path.GetFileName(sourcePath)) };

        long totalBytes = 0;
        foreach (var (source, _) in files)
        {
            ct.ThrowIfCancellationRequested();
            totalBytes += new FileInfo(source).Length;
        }

        int skipped = 0;
        if (skipUnchanged)
        {
            foreach (var (source, relative) in files)
            {
                ct.ThrowIfCancellationRequested();
                if (destinationRoots.All(root => FileCopyService.IsUnchanged(source, Path.Combine(root, relative))))
                    skipped++;
            }
        }

        var destinations = new List<DestinationSimulation>(destinationRoots.Count);
        foreach (var root in destinationRoots)
        {
            ct.ThrowIfCancellationRequested();

            int overwrites = files.Count(pair => File.Exists(Path.Combine(root, pair.Relative)));

            long? freeBytes = null;
            try
            {
                string? volumeRoot = Path.GetPathRoot(Path.GetFullPath(root));
                if (!string.IsNullOrEmpty(volumeRoot))
                    freeBytes = new DriveInfo(volumeRoot).AvailableFreeSpace;
            }
            catch (Exception)
            {
                // spazio non determinabile (percorso di rete, volume rimosso): resta null.
            }

            destinations.Add(new DestinationSimulation(
                root,
                overwrites,
                freeBytes,
                freeBytes is null ? null : freeBytes >= totalBytes));
        }

        return new CopySimulationResult(files.Count, totalBytes, skipped, destinations);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CopySimulationServiceTests"`
Expected: PASS (3 test); poi `dotnet test` completo senza regressioni.

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/CopySimulationService.cs FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/CopySimulationServiceTests.cs
git commit -m "feat(dry-run): servizio di simulazione copia senza scritture"
```

### Task 5: Comando "Simula" nella scheda Copia

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs`
- Modify: `FileExplorer/Views/CopyPairsView.axaml`
- Modify: `IDEE.md` (punto 6 → `[x]`)
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs` (aggiunta)

**Interfaces:**
- Consumes: `CopySimulationService.SimulateAsync` (Task 4), `SizeFormatter.Format(long)` (esistente).
- Produces: `CopyPairsViewModel.SimulateCommand` (`ReactiveCommand<FolderFilePairViewModel, Unit>`); `FolderFilePairViewModel.SimulationSummary` (`string?`, null = pannello nascosto) e `HasSimulation` (bool derivata).

- [x] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task SimulatePair_PopulatesSummary()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fe-simcmd-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(tempDir, "src");
        string destination = Path.Combine(tempDir, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[10]);

        try
        {
            var viewModel = new CopyPairsViewModel();
            var pair = new FolderFilePairViewModel { SourcePath = source, DestinationPath = destination };
            await pair.SourceStateRefresh;

            await viewModel.SimulatePairAsync(pair);

            Assert.NotNull(pair.SimulationSummary);
            Assert.Contains("1 file", pair.SimulationSummary!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SimulatePair_PopulatesSummary"`
Expected: FAIL (compile error: `SimulatePairAsync` / `SimulationSummary` non esistono)

- [x] **Step 3: Implement ViewModel changes**

In `FolderFilePairViewModel`:

```csharp
    private string? _simulationSummary;

    /// <summary>Esito testuale dell'ultima simulazione (dry-run); null = nessuna simulazione visibile.</summary>
    public string? SimulationSummary
    {
        get => _simulationSummary;
        set
        {
            this.RaiseAndSetIfChanged(ref _simulationSummary, value);
            this.RaisePropertyChanged(nameof(HasSimulation));
        }
    }

    public bool HasSimulation => !string.IsNullOrEmpty(SimulationSummary);
```

In `CopyPairsViewModel`: dichiarare `public ReactiveCommand<FolderFilePairViewModel, Unit> SimulateCommand { get; }`, crearlo nel costruttore con `ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(SimulatePairAsync)` e aggiungere:

```csharp
    /// <summary>Dry-run della coppia: cosa verrebbe copiato, sovrascritto, saltato, e se lo spazio basta.</summary>
    public async Task SimulatePairAsync(FolderFilePairViewModel pair)
    {
        if (!pair.CanStart)
        {
            pair.Status = "Percorsi non validi";
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        IReadOnlyList<string> destinations = pair.AllDestinations;
        pair.Status = "Simulazione…";

        try
        {
            var result = await CopySimulationService.SimulateAsync(
                pair.SourcePath!, destinations, pair.SkipUnchanged, CancellationToken.None);

            var lines = new List<string>
            {
                $"Da copiare: {result.TotalFiles} file, {SizeFormatter.Format(result.TotalBytes)}" +
                (result.SkippedFiles > 0 ? $" (di cui {result.SkippedFiles} invariati, saltati)" : string.Empty)
            };

            foreach (var destination in result.Destinations)
            {
                string space = destination.FreeBytes is null
                    ? "spazio libero sconosciuto"
                    : $"liberi {SizeFormatter.Format(destination.FreeBytes.Value)}" +
                      (destination.Fits == false ? " — SPAZIO INSUFFICIENTE" : string.Empty);
                lines.Add($"{destination.Root}: {destination.OverwriteCount} sovrascritture, {space}");
            }

            pair.SimulationSummary = string.Join(Environment.NewLine, lines);

            bool anyDoesNotFit = result.Destinations.Any(d => d.Fits == false);
            pair.Status = anyDoesNotFit ? "Simulazione: spazio insufficiente" : "Simulazione completata";
            pair.StateKind = anyDoesNotFit ? CopyStateKind.Warning : CopyStateKind.Ready;
        }
        catch (Exception ex)
        {
            pair.Status = $"Errore simulazione: {ex.Message}";
            pair.StateKind = CopyStateKind.Error;
        }
    }
```

- [x] **Step 4: Add the UI**

In `CopyPairsView.axaml`, nella riga della coppia: accanto al bottone Avvia aggiungere un bottone icona (stessa classe degli altri `iconbtn`):

```xml
              <Button Classes="iconbtn"
                      i:Attached.Icon="fa-solid fa-flask"
                      Command="{Binding DataContext.SimulateCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                      CommandParameter="{Binding}"
                      IsEnabled="{Binding CanStart}"
                      ToolTip.Tip="Simula (dry-run): niente viene scritto" />
```

(stesso pattern di binding degli altri bottoni-riga: `DataContext.XCommand` con `RelativeSource AncestorType=UserControl`.)

Sotto la riga di stato della coppia, il pannello risultato:

```xml
              <Border Classes="card" IsVisible="{Binding HasSimulation}" Margin="0,6,0,0">
                <TextBlock Text="{Binding SimulationSummary}"
                           TextWrapping="Wrap"
                           FontFamily="{StaticResource ResourceKey=ContentControlThemeFontFamily}"
                           Foreground="{DynamicResource Brush.TextSecondary}" />
              </Border>
```

(se `Border.card` risulta troppo pesante dentro la riga, usare un `Border` semplice con `Background="{DynamicResource Brush.SurfaceAlt}"` o il brush di sfondo secondario già in palette — non introdurre colori nuovi.)

Azzerare `SimulationSummary` all'avvio di una copia reale: in `StartCopyAsync`, nel blocco che resetta lo stato (`pair.IsVerified = null;`), aggiungere `pair.SimulationSummary = null;`.

- [x] **Step 5: Run the suite, mark IDEE, commit**

Run: `dotnet test`
Expected: PASS

In `IDEE.md` cambiare il punto 6 da `[ ]` a `[x]`.

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer/Views/CopyPairsView.axaml FileExplorer.Tests/CopyPairsViewModelTests.cs IDEE.md
git commit -m "feat(dry-run): comando Simula con report per destinazione"
```

Fine fase: push e `gh pr create` (base `feature/io-throttling`, titolo "Dry-run / simulazione copia (IDEE #6)").

---

## Fase 3 — Grafico velocità in tempo reale (IDEE punto 19)

Stacked su Fase 2. Stato attuale: i callback `onBytesCopied`/`onProgress` di `FileCopyService` già riportano i byte copiati; manca solo elaborazione e visualizzazione.

### Task 6: SpeedTracker

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/SpeedTracker.cs`
- Test: `FileExplorer.Tests/SpeedTrackerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class SpeedTracker
{
    public SpeedTracker(Func<double> nowSeconds);   // clock iniettabile
    public void Start(long totalBytes);
    public void Report(long copiedBytes);            // totale cumulativo copiato
    public double CurrentBytesPerSecond { get; }     // finestra mobile ~1 s
    public double AverageBytesPerSecond { get; }
    public double PeakBytesPerSecond { get; }
    public double? EtaSeconds { get; }               // null se velocità 0 o totale ignoto
    public IReadOnlyList<double> Samples { get; }    // ultimi 60 campioni MB/s per la sparkline
    public bool TryTakeSnapshot(out SpeedSnapshot snapshot); // true max ~4 volte/s (rate-limit UI)
}
public readonly record struct SpeedSnapshot(
    double CurrentBytesPerSecond, double AverageBytesPerSecond,
    double PeakBytesPerSecond, double? EtaSeconds, IReadOnlyList<double> Samples);
```

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class SpeedTrackerTests
{
    [Fact]
    public void Report_ComputesCurrentAndAverageSpeed()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0;
        tracker.Report(copiedBytes: 100);

        Assert.Equal(100, tracker.CurrentBytesPerSecond, precision: 1);
        Assert.Equal(100, tracker.AverageBytesPerSecond, precision: 1);
    }

    [Fact]
    public void Peak_TracksMaximum()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 10000);

        now = 1.0; tracker.Report(500);   // 500 B/s
        now = 2.0; tracker.Report(600);   // 100 B/s

        Assert.Equal(500, tracker.PeakBytesPerSecond, precision: 1);
    }

    [Fact]
    public void Eta_UsesAverageSpeed()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0; tracker.Report(100);
        // Restano 900 byte a 100 B/s medi → 9 s.
        Assert.NotNull(tracker.EtaSeconds);
        Assert.Equal(9.0, tracker.EtaSeconds!.Value, precision: 1);
    }

    [Fact]
    public void Eta_NullWhenNoProgress()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        Assert.Null(tracker.EtaSeconds);
    }

    [Fact]
    public void TryTakeSnapshot_RateLimited()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);
        now = 0.5; tracker.Report(100);

        Assert.True(tracker.TryTakeSnapshot(out _));
        Assert.False(tracker.TryTakeSnapshot(out _)); // stesso istante: rifiutato

        now = 1.0; tracker.Report(200);
        Assert.True(tracker.TryTakeSnapshot(out var snapshot));
        Assert.NotEmpty(snapshot.Samples);
    }

    [Fact]
    public void Samples_CappedAtSixty()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: long.MaxValue);

        for (int i = 1; i <= 100; i++)
        {
            now = i * 0.5;
            tracker.Report(i * 10);
        }

        Assert.True(tracker.Samples.Count <= 60);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SpeedTrackerTests"`
Expected: FAIL (compile error)

- [x] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorer.Services;

/// <summary>Fotografia della velocità di copia per l'aggiornamento della UI.</summary>
public readonly record struct SpeedSnapshot(
    double CurrentBytesPerSecond,
    double AverageBytesPerSecond,
    double PeakBytesPerSecond,
    double? EtaSeconds,
    IReadOnlyList<double> Samples);

/// <summary>
/// Traccia la velocità di una copia a partire dai byte cumulativi riportati dai
/// callback di avanzamento. Clock iniettabile per i test; thread-safe (i callback
/// di copia arrivano da thread di background).
/// </summary>
public sealed class SpeedTracker
{
    private const int MaxSamples = 60;
    private const double SnapshotIntervalSeconds = 0.25;

    private readonly Func<double> _nowSeconds;
    private readonly object _gate = new();
    private readonly List<(double Time, long Bytes)> _points = new();
    private readonly List<double> _samples = new();

    private long _totalBytes;
    private double _startTime;
    private long _lastBytes;
    private double _peak;
    private double _lastSnapshotTime = double.NegativeInfinity;

    public SpeedTracker(Func<double> nowSeconds)
    {
        _nowSeconds = nowSeconds;
    }

    public void Start(long totalBytes)
    {
        lock (_gate)
        {
            _totalBytes = totalBytes;
            _startTime = _nowSeconds();
            _lastBytes = 0;
            _peak = 0;
            _points.Clear();
            _samples.Clear();
            _lastSnapshotTime = double.NegativeInfinity;
            _points.Add((_startTime, 0));
        }
    }

    public void Report(long copiedBytes)
    {
        lock (_gate)
        {
            double now = _nowSeconds();
            _lastBytes = copiedBytes;
            _points.Add((now, copiedBytes));

            // Finestra mobile: tiene solo l'ultimo secondo (e almeno 2 punti).
            while (_points.Count > 2 && now - _points[0].Time > 1.0)
                _points.RemoveAt(0);

            double current = CurrentLocked(now);
            if (current > _peak)
                _peak = current;

            _samples.Add(current / (1024.0 * 1024.0));
            if (_samples.Count > MaxSamples)
                _samples.RemoveAt(0);
        }
    }

    private double CurrentLocked(double now)
    {
        var oldest = _points[0];
        double window = now - oldest.Time;
        return window > 0 ? (_lastBytes - oldest.Bytes) / window : 0;
    }

    public double CurrentBytesPerSecond
    {
        get { lock (_gate) return CurrentLocked(_nowSeconds()); }
    }

    public double AverageBytesPerSecond
    {
        get
        {
            lock (_gate)
            {
                double elapsed = _nowSeconds() - _startTime;
                return elapsed > 0 ? _lastBytes / elapsed : 0;
            }
        }
    }

    public double PeakBytesPerSecond
    {
        get { lock (_gate) return _peak; }
    }

    public double? EtaSeconds
    {
        get
        {
            lock (_gate)
            {
                double average = AverageLocked();
                if (average <= 0 || _totalBytes <= 0 || _lastBytes >= _totalBytes)
                    return null;
                return (_totalBytes - _lastBytes) / average;
            }
        }
    }

    private double AverageLocked()
    {
        double elapsed = _nowSeconds() - _startTime;
        return elapsed > 0 ? _lastBytes / elapsed : 0;
    }

    public IReadOnlyList<double> Samples
    {
        get { lock (_gate) return _samples.ToList(); }
    }

    /// <summary>
    /// True al massimo ~4 volte al secondo: limita la frequenza di aggiornamento
    /// della UI senza timer dedicati (chiamato dai callback di avanzamento).
    /// </summary>
    public bool TryTakeSnapshot(out SpeedSnapshot snapshot)
    {
        lock (_gate)
        {
            double now = _nowSeconds();
            if (now - _lastSnapshotTime < SnapshotIntervalSeconds)
            {
                snapshot = default;
                return false;
            }

            _lastSnapshotTime = now;
            double average = AverageLocked();
            double? eta = average > 0 && _totalBytes > 0 && _lastBytes < _totalBytes
                ? (_totalBytes - _lastBytes) / average
                : null;

            snapshot = new SpeedSnapshot(CurrentLocked(now), average, _peak, eta, _samples.ToList());
            return true;
        }
    }
}
```

Nota per l'implementatore: se un assert numerico dei test in Step 1 non torna con questa implementazione, la discrepanza va risolta a favore della semantica documentata nei commenti dei test (finestra mobile ~1 s, media dall'inizio, ETA sulla media) — correggere l'implementazione, non l'assert.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SpeedTrackerTests"`
Expected: PASS (6 test)

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/SpeedTracker.cs FileExplorer.Tests/SpeedTrackerTests.cs
git commit -m "feat(speed): tracker velocità con finestra mobile, picco ed ETA"
```

### Task 7: SparklineControl + brush in palette

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Views/SparklineControl.cs`
- Modify: `FileExplorer/Styles/Palette.axaml` (brush `Brush.Sparkline.Line` e `Brush.Sparkline.Fill` in Light e Dark)

**Interfaces:**
- Produces: `SparklineControl : Control` con `StyledProperty<IReadOnlyList<double>?> SamplesProperty` (`Samples`); ridisegna su cambio proprietà e su cambio tema (pattern `TreemapControl`: `FindResource(ActualThemeVariant, "Brush.Sparkline.Line")`).

- [x] **Step 1: Add the palette brushes**

In `Palette.axaml`, ThemeDictionary Light:

```xml
    <SolidColorBrush x:Key="Brush.Sparkline.Line" Color="#2563EB" />
    <SolidColorBrush x:Key="Brush.Sparkline.Fill" Color="#332563EB" />
```

ThemeDictionary Dark:

```xml
    <SolidColorBrush x:Key="Brush.Sparkline.Line" Color="#60A5FA" />
    <SolidColorBrush x:Key="Brush.Sparkline.Fill" Color="#3360A5FA" />
```

(se la palette usa già un blu accent con chiave dedicata, riusare quei valori esatti per coerenza cromatica.)

- [x] **Step 2: Write the control**

```csharp
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileExplorer.Views;

/// <summary>
/// Sparkline minimale: polilinea dei campioni (MB/s) normalizzata sull'altezza
/// disponibile, con riempimento sotto la curva. Nessun asse, nessuna label.
/// </summary>
public class SparklineControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<SparklineControl, IReadOnlyList<double>?>(nameof(Samples));

    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    static SparklineControl()
    {
        AffectsRender<SparklineControl>(SamplesProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var samples = Samples;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (samples is null || samples.Count < 2 || width <= 0 || height <= 0)
            return;

        double max = 0;
        foreach (var sample in samples)
            if (double.IsFinite(sample) && sample > max)
                max = sample;
        if (max <= 0)
            return;

        var lineBrush = this.FindResource(ActualThemeVariant, "Brush.Sparkline.Line") as IBrush;
        var fillBrush = this.FindResource(ActualThemeVariant, "Brush.Sparkline.Fill") as IBrush;
        if (lineBrush is null)
            return;

        double stepX = width / (samples.Count - 1);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(new Point(0, height), isFilled: true);
            for (int i = 0; i < samples.Count; i++)
            {
                double value = double.IsFinite(samples[i]) ? Math.Max(0, samples[i]) : 0;
                geometryContext.LineTo(new Point(i * stepX, height - value / max * height));
            }
            geometryContext.LineTo(new Point(width, height));
            geometryContext.EndFigure(isClosed: true);
        }

        if (fillBrush is not null)
            context.DrawGeometry(fillBrush, null, geometry);

        var pen = new Pen(lineBrush, 1.5);
        for (int i = 1; i < samples.Count; i++)
        {
            double value0 = double.IsFinite(samples[i - 1]) ? Math.Max(0, samples[i - 1]) : 0;
            double value1 = double.IsFinite(samples[i]) ? Math.Max(0, samples[i]) : 0;
            context.DrawLine(pen,
                new Point((i - 1) * stepX, height - value0 / max * height),
                new Point(i * stepX, height - value1 / max * height));
        }
    }
}
```

Nota: verificare la firma esatta di `FindResource(ThemeVariant, object)` usata da `TreemapControl` e replicarla (stessa API, stesso using). Se `ActualThemeVariantChanged` in `TreemapControl` è agganciato diversamente, copiare quel pattern.

- [x] **Step 3: Build clean and commit**

Run: `dotnet build FileExplorer.sln` — 0 errori, nessun warning nuovo.

```bash
git add FileExplorer/Views/SparklineControl.cs FileExplorer/Styles/Palette.axaml
git commit -m "feat(speed): controllo sparkline theme-aware"
```

### Task 8: Integrazione velocità nella scheda Copia

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs` (proprietà velocità)
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs` (tracker nei percorsi di copia)
- Modify: `FileExplorer/Views/CopyPairsView.axaml` (sparkline + testo velocità)
- Modify: `IDEE.md` (punto 19 → `[x]`)
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs` (aggiunta)

**Interfaces:**
- Consumes: `SpeedTracker` (Task 6), `SparklineControl` (Task 7), `SizeFormatter.Format` (esistente).
- Produces: `FolderFilePairViewModel.SpeedText` (`string?`), `FolderFilePairViewModel.SpeedSamples` (`IReadOnlyList<double>?`).

- [x] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task CopyDirectory_UpdatesSpeedText()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fe-speed-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(tempDir, "src");
        string destination = Path.Combine(tempDir, "dst");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, "a.bin"), new byte[512 * 1024]);

        try
        {
            var viewModel = new CopyPairsViewModel();
            var pair = new FolderFilePairViewModel { SourcePath = source, DestinationPath = destination };
            await pair.SourceStateRefresh;

            await viewModel.StartCopyAsync(pair);

            // A copia finita il testo velocità riporta la media (formato "media …/s").
            Assert.NotNull(pair.SpeedText);
            Assert.Contains("media", pair.SpeedText!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CopyDirectory_UpdatesSpeedText"`
Expected: FAIL (compile error: `SpeedText` non esiste)

- [x] **Step 3: Implement**

In `FolderFilePairViewModel`:

```csharp
    private string? _speedText;

    /// <summary>Riga velocità: "12.3 MB/s · media 10.1 MB/s · picco 15.2 MB/s · ETA 00:42".</summary>
    public string? SpeedText
    {
        get => _speedText;
        set => this.RaiseAndSetIfChanged(ref _speedText, value);
    }

    private IReadOnlyList<double>? _speedSamples;

    /// <summary>Campioni MB/s per la sparkline.</summary>
    public IReadOnlyList<double>? SpeedSamples
    {
        get => _speedSamples;
        set => this.RaiseAndSetIfChanged(ref _speedSamples, value);
    }
```

In `CopyPairsViewModel`:

1. Helper statici privati:

```csharp
    private static string FormatSpeed(double bytesPerSecond) =>
        $"{SizeFormatter.Format((long)bytesPerSecond)}/s";

    private static string FormatEta(double? etaSeconds)
    {
        if (etaSeconds is null || !double.IsFinite(etaSeconds.Value))
            return "—";
        var time = TimeSpan.FromSeconds(Math.Min(etaSeconds.Value, TimeSpan.MaxValue.TotalSeconds - 1));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private static void PublishSpeed(FolderFilePairViewModel pair, SpeedSnapshot snapshot)
    {
        pair.SpeedText =
            $"{FormatSpeed(snapshot.CurrentBytesPerSecond)} · media {FormatSpeed(snapshot.AverageBytesPerSecond)}" +
            $" · picco {FormatSpeed(snapshot.PeakBytesPerSecond)} · ETA {FormatEta(snapshot.EtaSeconds)}";
        pair.SpeedSamples = snapshot.Samples;
    }
```

2. In `CopySingleFileAsync`: creare `var tracker = new SpeedTracker(() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);` prima della copia, `tracker.Start(totalBytes);`, e dentro il callback `deltaBytes`:

```csharp
            copiedBytes += deltaBytes;
            pair.Progress = totalBytes > 0 ? (double)copiedBytes / totalBytes : 1;
            tracker.Report(copiedBytes);
            if (tracker.TryTakeSnapshot(out var snapshot))
                PublishSpeed(pair, snapshot);
```

A copia conclusa (prima della verifica checksum): pubblicare un ultimo snapshot con la media finale:

```csharp
            pair.SpeedText = $"media {FormatSpeed(tracker.AverageBytesPerSecond)} · picco {FormatSpeed(tracker.PeakBytesPerSecond)}";
```

3. In `CopyDirectoryAsync` (VM): stesso schema dentro `onProgress` — `tracker.Start(progress.TotalBytes)` alla prima notifica (quando `knownFileCount` cambia), `tracker.Report(progress.CopiedBytes)` a ogni notifica, `TryTakeSnapshot` → `PublishSpeed`. Ultimo snapshot con la media finale dopo la copia, come sopra.

4. Reset: in `StartCopyAsync`, nel blocco di reset dello stato, `pair.SpeedText = null; pair.SpeedSamples = null;` — e ripristino dello `SpeedText` finale lasciato visibile a fine copia (non azzerarlo nel `finally`).

- [x] **Step 4: Add the UI**

In `CopyPairsView.axaml`, nella riga della coppia sotto la ProgressBar:

```xml
              <StackPanel Orientation="Horizontal" Spacing="10" IsVisible="{Binding SpeedText, Converter={x:Static ObjectConverters.IsNotNull}}">
                <views:SparklineControl Width="120" Height="24" Samples="{Binding SpeedSamples}" VerticalAlignment="Center" />
                <TextBlock Text="{Binding SpeedText}" VerticalAlignment="Center"
                           Foreground="{DynamicResource Brush.TextSecondary}" FontSize="12" />
              </StackPanel>
```

Aggiungere `xmlns:views="clr-namespace:FileExplorer.Views"` se assente.

- [x] **Step 5: Run the suite, mark IDEE, commit**

Run: `dotnet test`
Expected: PASS

In `IDEE.md` cambiare il punto 19 da `[ ]` a `[x]`.

```bash
git add FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/Views/CopyPairsView.axaml FileExplorer.Tests/CopyPairsViewModelTests.cs IDEE.md
git commit -m "feat(speed): sparkline, velocità media/picco ed ETA nella scheda Copia"
```

Fine fase: push e `gh pr create` (base `feature/dry-run`, titolo "Grafico velocità in tempo reale (IDEE #19)").

---

## Fase 4 — Report di confronto esportabile (IDEE punto 9)

Branch da `main`. Nuova tab "Confronto": due percorsi, confronto directory a cascata (dimensione → SHA-256), quattro categorie (solo a sinistra, solo a destra, diversi, identici), export HTML/CSV/JSON.

### Task 9: DirectoryComparisonService

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/DirectoryComparisonService.cs`
- Test: `FileExplorer.Tests/DirectoryComparisonServiceTests.cs`

**Interfaces:**
- Consumes: `ChecksumService.ComputeSha256Async(string path, CancellationToken ct)` (esistente).
- Produces:

```csharp
public readonly record struct CompareProgress(int Processed, int Total);
public sealed record DirectoryComparisonResult(
    IReadOnlyList<string> LeftOnly, IReadOnlyList<string> RightOnly,
    IReadOnlyList<string> Different, IReadOnlyList<string> Identical);
public static class DirectoryComparisonService
{
    public static Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot, string rightRoot, int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress, CancellationToken ct);
}
```

Tutti i path nei risultati sono relativi alle radici, ordinati con `StringComparer.Ordinal`.

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class DirectoryComparisonServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-compare-" + Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public DirectoryComparisonServiceTests()
    {
        _left = Path.Combine(_tempDir, "left");
        _right = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task CompareAsync_ClassifiesAllFourCategories()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "only-left.txt"), "L");
        await File.WriteAllTextAsync(Path.Combine(_right, "only-right.txt"), "R");
        await File.WriteAllTextAsync(Path.Combine(_left, "same.txt"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_right, "same.txt"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_left, "diff.txt"), "AAAA");
        await File.WriteAllTextAsync(Path.Combine(_right, "diff.txt"), "BBBB");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, maxDegreeOfParallelism: 2, onProgress: null, CancellationToken.None);

        Assert.Equal(new[] { "only-left.txt" }, result.LeftOnly);
        Assert.Equal(new[] { "only-right.txt" }, result.RightOnly);
        Assert.Equal(new[] { "diff.txt" }, result.Different);
        Assert.Equal(new[] { "same.txt" }, result.Identical);
    }

    [Fact]
    public async Task CompareAsync_SameSizeDifferentContent_IsDifferent()
    {
        // Stessa dimensione: il confronto deve arrivare all'hash.
        await File.WriteAllTextAsync(Path.Combine(_left, "tricky.txt"), "AAAA");
        await File.WriteAllTextAsync(Path.Combine(_right, "tricky.txt"), "ZZZZ");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, CancellationToken.None);

        Assert.Equal(new[] { "tricky.txt" }, result.Different);
        Assert.Empty(result.Identical);
    }

    [Fact]
    public async Task CompareAsync_NestedPaths_UseRelativePaths()
    {
        Directory.CreateDirectory(Path.Combine(_left, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_left, "sub", "deep.txt"), "X");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, CancellationToken.None);

        Assert.Equal(new[] { Path.Combine("sub", "deep.txt") }, result.LeftOnly);
    }

    [Fact]
    public async Task CompareAsync_ReportsProgress()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_right, "a.txt"), "1");

        int lastProcessed = 0;
        var progressLock = new object();
        await DirectoryComparisonService.CompareAsync(_left, _right, 1,
            progress => { lock (progressLock) lastProcessed = progress.Processed; },
            CancellationToken.None);

        Assert.Equal(1, lastProcessed);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DirectoryComparisonServiceTests"`
Expected: FAIL (compile error)

- [x] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Avanzamento del confronto (file comuni processati sul totale dei comuni).</summary>
public readonly record struct CompareProgress(int Processed, int Total);

/// <summary>
/// Esito del confronto di due alberi: path relativi alle radici,
/// classificati in quattro categorie, ordinati.
/// </summary>
public sealed record DirectoryComparisonResult(
    IReadOnlyList<string> LeftOnly,
    IReadOnlyList<string> RightOnly,
    IReadOnlyList<string> Different,
    IReadOnlyList<string> Identical);

/// <summary>
/// Confronto directory a cascata: presenza → dimensione → SHA-256 (solo a parità
/// di dimensione), più file in parallelo. Enumerazione tollerante (symlink e
/// file inaccessibili saltati).
/// </summary>
public static class DirectoryComparisonService
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static async Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot,
        string rightRoot,
        int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress,
        CancellationToken ct)
    {
        var leftFiles = await Task.Run(() => RelativeFileSet(leftRoot, ct), ct);
        var rightFiles = await Task.Run(() => RelativeFileSet(rightRoot, ct), ct);

        var leftOnly = leftFiles.Keys.Where(k => !rightFiles.ContainsKey(k)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var rightOnly = rightFiles.Keys.Where(k => !leftFiles.ContainsKey(k)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var common = leftFiles.Keys.Where(rightFiles.ContainsKey).ToList();

        var different = new ConcurrentBag<string>();
        var identical = new ConcurrentBag<string>();
        int processed = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = common.Select(async relative =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                string leftPath = Path.Combine(leftRoot, relative);
                string rightPath = Path.Combine(rightRoot, relative);

                if (leftFiles[relative] != rightFiles[relative])
                {
                    different.Add(relative);
                    return;
                }

                string leftHash = await ChecksumService.ComputeSha256Async(leftPath, ct);
                string rightHash = await ChecksumService.ComputeSha256Async(rightPath, ct);
                if (string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase))
                    identical.Add(relative);
                else
                    different.Add(relative);
            }
            finally
            {
                semaphore.Release();
                onProgress?.Invoke(new CompareProgress(Interlocked.Increment(ref processed), common.Count));
            }
        });

        await Task.WhenAll(tasks);

        return new DirectoryComparisonResult(
            leftOnly,
            rightOnly,
            different.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            identical.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }

    /// <summary>Mappa path relativo → dimensione file.</summary>
    private static Dictionary<string, long> RelativeFileSet(string root, CancellationToken ct)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            map[Path.GetRelativePath(root, file)] = new FileInfo(file).Length;
        }
        return map;
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DirectoryComparisonServiceTests"`
Expected: PASS (4 test)

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/DirectoryComparisonService.cs FileExplorer.Tests/DirectoryComparisonServiceTests.cs
git commit -m "feat(confronto): confronto directory a cascata dimensione-hash"
```

### Task 10: ComparisonReportExporter (HTML/CSV/JSON)

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/Services/ComparisonReportExporter.cs`
- Test: `FileExplorer.Tests/ComparisonReportExporterTests.cs`

**Interfaces:**
- Consumes: `DirectoryComparisonResult` (Task 9).
- Produces:

```csharp
public enum ComparisonReportFormat { Html, Csv, Json }
public static class ComparisonReportExporter
{
    public static string Render(DirectoryComparisonResult result, ComparisonReportFormat format,
        string leftRoot, string rightRoot, DateTime generatedUtc);
    public static Task ExportAsync(string filePath, DirectoryComparisonResult result,
        ComparisonReportFormat format, string leftRoot, string rightRoot,
        DateTime generatedUtc, CancellationToken ct);
    public static string SuggestFileName(ComparisonReportFormat format, DateTime generatedUtc);
        // "confronto-20260818-153000.html" ecc.
}
```

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using System.Text.Json;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class ComparisonReportExporterTests
{
    private static readonly string[] LeftOnlyPaths = { "solo-sx.txt" };
    private static readonly string[] RightOnlyPaths = { "solo-dx.txt" };
    private static readonly string[] DifferentPaths = { "diverso.txt" };
    private static readonly string[] IdenticalPaths = { "uguale.txt" };

    private static DirectoryComparisonResult SampleResult() =>
        new(LeftOnlyPaths, RightOnlyPaths, DifferentPaths, IdenticalPaths);

    private static readonly DateTime GeneratedUtc = new(2026, 8, 18, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Render_Csv_HasHeaderAndOneRowPerFile()
    {
        string csv = ComparisonReportExporter.Render(
            SampleResult(), ComparisonReportFormat.Csv, "/sx", "/dx", GeneratedUtc);

        string[] lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("categoria;percorso", lines[0]);
        Assert.Contains("solo-a-sinistra;solo-sx.txt", lines);
        Assert.Contains("solo-a-destra;solo-dx.txt", lines);
        Assert.Contains("diversi;diverso.txt", lines);
        Assert.Contains("identici;uguale.txt", lines);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void Render_Json_RoundTrips()
    {
        string json = ComparisonReportExporter.Render(
            SampleResult(), ComparisonReportFormat.Json, "/sx", "/dx", GeneratedUtc);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("/sx", root.GetProperty("left").GetString());
        Assert.Equal("solo-sx.txt", root.GetProperty("leftOnly")[0].GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("different").GetInt32());
    }

    [Fact]
    public void Render_Html_ContainsSummaryAndEscapesPaths()
    {
        var result = new DirectoryComparisonResult(
            new[] { "cattivo<script>.txt" }, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>());

        string html = ComparisonReportExporter.Render(
            result, ComparisonReportFormat.Html, "/sx", "/dx", GeneratedUtc);

        Assert.Contains("Solo a sinistra", html);
        Assert.Contains("cattivo&lt;script&gt;.txt", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void SuggestFileName_UsesTimestampAndExtension()
    {
        Assert.Equal("confronto-20260818-153000.csv",
            ComparisonReportExporter.SuggestFileName(ComparisonReportFormat.Csv, GeneratedUtc));
    }
}
```

(aggiungere `using System.Linq;` in testa.)

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonReportExporterTests"`
Expected: FAIL (compile error)

- [x] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Formato di esportazione del report di confronto.</summary>
public enum ComparisonReportFormat
{
    Html,
    Csv,
    Json
}

/// <summary>
/// Rendering ed esportazione del report di confronto directory in HTML
/// (autonomo, CSS inline), CSV (separatore ';') e JSON.
/// </summary>
public static class ComparisonReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Render(
        DirectoryComparisonResult result,
        ComparisonReportFormat format,
        string leftRoot,
        string rightRoot,
        DateTime generatedUtc)
    {
        return format switch
        {
            ComparisonReportFormat.Csv => RenderCsv(result),
            ComparisonReportFormat.Json => RenderJson(result, leftRoot, rightRoot, generatedUtc),
            _ => RenderHtml(result, leftRoot, rightRoot, generatedUtc)
        };
    }

    public static async Task ExportAsync(
        string filePath,
        DirectoryComparisonResult result,
        ComparisonReportFormat format,
        string leftRoot,
        string rightRoot,
        DateTime generatedUtc,
        CancellationToken ct)
    {
        string content = Render(result, format, leftRoot, rightRoot, generatedUtc);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, ct);
    }

    public static string SuggestFileName(ComparisonReportFormat format, DateTime generatedUtc)
    {
        string extension = format switch
        {
            ComparisonReportFormat.Csv => "csv",
            ComparisonReportFormat.Json => "json",
            _ => "html"
        };
        return $"confronto-{generatedUtc:yyyyMMdd-HHmmss}.{extension}";
    }

    private static string RenderCsv(DirectoryComparisonResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("categoria;percorso");
        AppendCsvRows(builder, "solo-a-sinistra", result.LeftOnly);
        AppendCsvRows(builder, "solo-a-destra", result.RightOnly);
        AppendCsvRows(builder, "diversi", result.Different);
        AppendCsvRows(builder, "identici", result.Identical);
        return builder.ToString();
    }

    private static void AppendCsvRows(StringBuilder builder, string category, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            // Il ';' è il separatore: i path che lo contengono vanno quotati.
            string cell = path.Contains(';') || path.Contains('"')
                ? "\"" + path.Replace("\"", "\"\"") + "\""
                : path;
            builder.Append(category).Append(';').AppendLine(cell);
        }
    }

    private static string RenderJson(
        DirectoryComparisonResult result, string leftRoot, string rightRoot, DateTime generatedUtc)
    {
        var payload = new
        {
            Left = leftRoot,
            Right = rightRoot,
            GeneratedUtc = generatedUtc,
            Summary = new
            {
                LeftOnly = result.LeftOnly.Count,
                RightOnly = result.RightOnly.Count,
                Different = result.Different.Count,
                Identical = result.Identical.Count
            },
            LeftOnly = result.LeftOnly,
            RightOnly = result.RightOnly,
            Different = result.Different,
            Identical = result.Identical
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string RenderHtml(
        DirectoryComparisonResult result, string leftRoot, string rightRoot, DateTime generatedUtc)
    {
        string Escape(string value) => WebUtility.HtmlEncode(value);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>Report confronto directory</title>");
        builder.AppendLine("<style>body{font-family:sans-serif;margin:2rem;color:#1f2937}h1{font-size:1.3rem}h2{font-size:1.05rem;margin-top:1.5rem}table{border-collapse:collapse;margin-top:.5rem}td,th{border:1px solid #d1d5db;padding:.3rem .6rem;text-align:left;font-size:.9rem}.empty{color:#6b7280;font-style:italic}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine($"<h1>Report confronto directory</h1>");
        builder.AppendLine($"<p><strong>Sinistra:</strong> {Escape(leftRoot)}<br><strong>Destra:</strong> {Escape(rightRoot)}<br><strong>Generato (UTC):</strong> {generatedUtc:yyyy-MM-dd HH:mm:ss}</p>");
        builder.AppendLine("<table><tr><th>Categoria</th><th>File</th></tr>");
        builder.AppendLine($"<tr><td>Solo a sinistra</td><td>{result.LeftOnly.Count}</td></tr>");
        builder.AppendLine($"<tr><td>Solo a destra</td><td>{result.RightOnly.Count}</td></tr>");
        builder.AppendLine($"<tr><td>Diversi</td><td>{result.Different.Count}</td></tr>");
        builder.AppendLine($"<tr><td>Identici</td><td>{result.Identical.Count}</td></tr>");
        builder.AppendLine("</table>");

        AppendHtmlSection(builder, "Solo a sinistra", result.LeftOnly, Escape);
        AppendHtmlSection(builder, "Solo a destra", result.RightOnly, Escape);
        AppendHtmlSection(builder, "Diversi", result.Different, Escape);
        AppendHtmlSection(builder, "Identici", result.Identical, Escape);

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void AppendHtmlSection(
        StringBuilder builder, string title, IReadOnlyList<string> paths, Func<string, string> escape)
    {
        builder.AppendLine($"<h2>{escape(title)} ({paths.Count})</h2>");
        if (paths.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">Nessun file.</p>");
            return;
        }

        builder.AppendLine("<ul>");
        foreach (var path in paths)
            builder.AppendLine($"<li>{escape(path)}</li>");
        builder.AppendLine("</ul>");
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonReportExporterTests"`
Expected: PASS (4 test)

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Services/ComparisonReportExporter.cs FileExplorer.Tests/ComparisonReportExporterTests.cs
git commit -m "feat(confronto): esportazione report HTML/CSV/JSON"
```

### Task 11: ComparisonViewModel

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/ComparisonViewModel.cs`
- Test: `FileExplorer.Tests/ComparisonViewModelTests.cs`

**Interfaces:**
- Consumes: `DirectoryComparisonService.CompareAsync` (Task 9), `ComparisonReportExporter` (Task 10), `SelectPathDialogHelper.ShowAsync(bool directoriesOnly, string? initialPath)` (esistente).
- Produces: proprietà `LeftPath`/`RightPath` (string?), `IsComparing` (bool), `StatusText` (string), `Result` (`DirectoryComparisonResult?`), `HasResult` (bool), contatori `LeftOnlyCount/RightOnlyCount/DifferentCount/IdenticalCount`, comandi `BrowseLeftCommand`, `BrowseRightCommand`, `CompareCommand`, `CancelCommand`, `ExportHtmlCommand`, `ExportCsvCommand`, `ExportJsonCommand`; metodi testabili `CompareAsync()` e `ExportAsync(ComparisonReportFormat format, string targetDirectory)`; `IDisposable`.

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using FileExplorer.Services;
using FileExplorer.ViewModels;
using Xunit;

namespace FileExplorer.Tests;

public sealed class ComparisonViewModelTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-comparevm-" + Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public ComparisonViewModelTests()
    {
        _left = Path.Combine(_tempDir, "left");
        _right = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task CompareAsync_PopulatesCountsAndStatus()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_right, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_left, "b.txt"), "solo sx");

        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };

        await viewModel.CompareAsync();

        Assert.True(viewModel.HasResult);
        Assert.Equal(1, viewModel.IdenticalCount);
        Assert.Equal(1, viewModel.LeftOnlyCount);
        Assert.Equal(0, viewModel.DifferentCount);
        Assert.False(viewModel.IsComparing);
        Assert.Contains("1 identici", viewModel.StatusText);
    }

    [Fact]
    public async Task CompareAsync_InvalidPaths_SetsErrorStatus()
    {
        using var viewModel = new ComparisonViewModel { LeftPath = null, RightPath = _right };

        await viewModel.CompareAsync();

        Assert.False(viewModel.HasResult);
        Assert.Contains("Selezionare", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportAsync_WritesFileInTargetDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.CompareAsync();

        string exportDir = Path.Combine(_tempDir, "export");
        Directory.CreateDirectory(exportDir);

        string? written = await viewModel.ExportAsync(ComparisonReportFormat.Csv, exportDir);

        Assert.NotNull(written);
        Assert.True(File.Exists(written));
        Assert.Contains("solo-a-sinistra;a.txt", await File.ReadAllTextAsync(written!));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonViewModelTests"`
Expected: FAIL (compile error)

- [x] **Step 3: Write the implementation**

```csharp
using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Confronto": confronta due directory (cascata dimensione → SHA-256)
/// ed esporta il report in HTML/CSV/JSON.
/// </summary>
public class ComparisonViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _compareCts;

    public ComparisonViewModel()
    {
        BrowseLeftCommand = ReactiveCommand.CreateFromTask(BrowseLeftAsync);
        BrowseRightCommand = ReactiveCommand.CreateFromTask(BrowseRightAsync);
        CompareCommand = ReactiveCommand.CreateFromTask(CompareAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExportHtmlCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Html));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Csv));
        ExportJsonCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Json));
    }

    public ReactiveCommand<Unit, Unit> BrowseLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportHtmlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }

    private string? _leftPath;
    public string? LeftPath
    {
        get => _leftPath;
        set => this.RaiseAndSetIfChanged(ref _leftPath, value);
    }

    private string? _rightPath;
    public string? RightPath
    {
        get => _rightPath;
        set => this.RaiseAndSetIfChanged(ref _rightPath, value);
    }

    private bool _isComparing;
    public bool IsComparing
    {
        get => _isComparing;
        private set => this.RaiseAndSetIfChanged(ref _isComparing, value);
    }

    private string _statusText = "Selezionare due cartelle da confrontare";
    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private DirectoryComparisonResult? _result;
    public DirectoryComparisonResult? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(HasResult));
            this.RaisePropertyChanged(nameof(LeftOnlyCount));
            this.RaisePropertyChanged(nameof(RightOnlyCount));
            this.RaisePropertyChanged(nameof(DifferentCount));
            this.RaisePropertyChanged(nameof(IdenticalCount));
        }
    }

    public bool HasResult => Result is not null;
    public int LeftOnlyCount => Result?.LeftOnly.Count ?? 0;
    public int RightOnlyCount => Result?.RightOnly.Count ?? 0;
    public int DifferentCount => Result?.Different.Count ?? 0;
    public int IdenticalCount => Result?.Identical.Count ?? 0;

    private async Task BrowseLeftAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, LeftPath);
        if (!string.IsNullOrEmpty(selected))
            LeftPath = selected;
    }

    private async Task BrowseRightAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, RightPath);
        if (!string.IsNullOrEmpty(selected))
            RightPath = selected;
    }

    /// <summary>Confronta le due directory selezionate. Pubblico per i test.</summary>
    public async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath)
            || !Directory.Exists(LeftPath) || !Directory.Exists(RightPath))
        {
            StatusText = "Selezionare due cartelle esistenti";
            return;
        }

        _compareCts?.Cancel();
        _compareCts?.Dispose();
        _compareCts = new CancellationTokenSource();
        var ct = _compareCts.Token;

        IsComparing = true;
        Result = null;
        StatusText = "Confronto in corso…";

        try
        {
            int parallelism = Math.Max(2, Environment.ProcessorCount - 1);
            var result = await DirectoryComparisonService.CompareAsync(
                LeftPath, RightPath, parallelism,
                progress => StatusText = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            Result = result;
            StatusText = $"{result.Identical.Count} identici, {result.Different.Count} diversi, " +
                         $"{result.LeftOnly.Count} solo a sinistra, {result.RightOnly.Count} solo a destra";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Confronto annullato";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsComparing = false;
        }
    }

    private void Cancel() => _compareCts?.Cancel();

    private async Task BrowseAndExportAsync(ComparisonReportFormat format)
    {
        if (Result is null)
            return;

        var targetDirectory = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, null);
        if (string.IsNullOrEmpty(targetDirectory))
            return;

        await ExportAsync(format, targetDirectory);
    }

    /// <summary>Esporta l'ultimo risultato nella cartella indicata; ritorna il path scritto o null. Pubblico per i test.</summary>
    public async Task<string?> ExportAsync(ComparisonReportFormat format, string targetDirectory)
    {
        if (Result is null)
            return null;

        try
        {
            DateTime generatedUtc = DateTime.UtcNow;
            string filePath = Path.Combine(
                targetDirectory, ComparisonReportExporter.SuggestFileName(format, generatedUtc));

            await ComparisonReportExporter.ExportAsync(
                filePath, Result, format, LeftPath!, RightPath!, generatedUtc, CancellationToken.None);

            StatusText = $"Report esportato: {filePath}";
            return filePath;
        }
        catch (Exception ex)
        {
            StatusText = $"Errore esportazione: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        _compareCts?.Cancel();
        _compareCts?.Dispose();
        _compareCts = null;
        GC.SuppressFinalize(this);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonViewModelTests"`
Expected: PASS (3 test); poi `dotnet build` senza warning nuovi (CA1001 coperto da IDisposable).

- [x] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/ComparisonViewModel.cs FileExplorer.Tests/ComparisonViewModelTests.cs
git commit -m "feat(confronto): ViewModel della scheda Confronto con export"
```

### Task 12: ComparisonView + tab in MainWindow

**Modello:** haiku

**Files:**
- Create: `FileExplorer/Views/ComparisonView.axaml`
- Create: `FileExplorer/Views/ComparisonView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml` (nuova TabItem "Confronto" dopo "Server remoto")
- Modify: `IDEE.md` (punto 9 → `[x]`)

**Interfaces:**
- Consumes: `ComparisonViewModel` (Task 11) — la vista crea il proprio ViewModel nel costruttore (pattern `CopyPairsView`).

- [x] **Step 1: Create the code-behind**

```csharp
using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class ComparisonView : UserControl
{
    public ComparisonView()
    {
        InitializeComponent();
        DataContext = new ComparisonViewModel();
    }
}
```

- [x] **Step 2: Create the view**

```xml
<!-- FileExplorer/Views/ComparisonView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             x:Class="FileExplorer.Views.ComparisonView">

  <DockPanel>

    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
        <i:Icon Value="fa-solid fa-code-compare" FontSize="20" Foreground="{DynamicResource Brush.OnAccent}" />
        <TextBlock Text="Confronto directory" FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
      </StackPanel>
    </Border>

    <ScrollViewer Background="{DynamicResource Brush.Surface}">
      <StackPanel Margin="20" Spacing="14" MaxWidth="720" HorizontalAlignment="Left">

        <Border Classes="card">
          <StackPanel Spacing="10">
            <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,8,Auto">
              <TextBlock Grid.Row="0" Grid.Column="0" Text="Sinistra:" Width="70" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding LeftPath}" Watermark="Cartella sinistra" />
              <Button Grid.Row="0" Grid.Column="2" Classes="iconbtn" Command="{Binding BrowseLeftCommand}"
                      ToolTip.Tip="Sfoglia">
                <i:Icon Value="fa-solid fa-folder-open" />
              </Button>

              <TextBlock Grid.Row="2" Grid.Column="0" Text="Destra:" Width="70" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding RightPath}" Watermark="Cartella destra" />
              <Button Grid.Row="2" Grid.Column="2" Classes="iconbtn" Command="{Binding BrowseRightCommand}"
                      ToolTip.Tip="Sfoglia">
                <i:Icon Value="fa-solid fa-folder-open" />
              </Button>
            </Grid>

            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button Classes="primary" Command="{Binding CompareCommand}" IsEnabled="{Binding !IsComparing}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <i:Icon Value="fa-solid fa-code-compare" />
                  <TextBlock Text="Confronta" />
                </StackPanel>
              </Button>
              <Button Classes="secondary" Command="{Binding CancelCommand}" IsEnabled="{Binding IsComparing}">
                <TextBlock Text="Annulla" />
              </Button>
            </StackPanel>

            <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextSecondary}" />
          </StackPanel>
        </Border>

        <Border Classes="card" IsVisible="{Binding HasResult}">
          <StackPanel Spacing="10">
            <TextBlock Text="Risultato" FontSize="15" FontWeight="SemiBold"
                       Foreground="{DynamicResource Brush.TextPrimary}" />

            <UniformGrid Columns="4">
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding IdenticalCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Identici" Foreground="{DynamicResource Brush.TextSecondary}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding DifferentCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Diversi" Foreground="{DynamicResource Brush.TextSecondary}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding LeftOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a sinistra" Foreground="{DynamicResource Brush.TextSecondary}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding RightOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a destra" Foreground="{DynamicResource Brush.TextSecondary}" FontSize="12" />
              </StackPanel>
            </UniformGrid>

            <StackPanel Orientation="Horizontal" Spacing="8">
              <TextBlock Text="Esporta:" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.TextPrimary}" />
              <Button Classes="secondary" Command="{Binding ExportHtmlCommand}"><TextBlock Text="HTML" /></Button>
              <Button Classes="secondary" Command="{Binding ExportCsvCommand}"><TextBlock Text="CSV" /></Button>
              <Button Classes="secondary" Command="{Binding ExportJsonCommand}"><TextBlock Text="JSON" /></Button>
            </StackPanel>
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>

  </DockPanel>

</UserControl>
```

- [x] **Step 3: Add the tab**

In `MainWindow.axaml`, dopo la TabItem "Server remoto":

```xml
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-code-compare" />
          <TextBlock Text="Confronto" />
        </StackPanel>
      </TabItem.Header>
      <views:ComparisonView />
    </TabItem>
```

- [x] **Step 4: Build, run the suite, mark IDEE, commit**

Run: `dotnet build FileExplorer.sln` (0 errori, nessun warning nuovo) e `dotnet test` (PASS).

In `IDEE.md` cambiare il punto 9 da `[ ]` a `[x]`.

```bash
git add FileExplorer/Views/ComparisonView.axaml FileExplorer/Views/ComparisonView.axaml.cs FileExplorer/Views/MainWindow.axaml IDEE.md
git commit -m "feat(confronto): scheda Confronto con export report"
```

Fine fase: push e `gh pr create` (base `main`, titolo "Report di confronto esportabile (IDEE #9)").
