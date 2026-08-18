# Conferma eliminazione Duplicati + follow-up review finale — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (A) Dialog di conferma per le eliminazioni nella tab Duplicati (chiude il gap "azioni sicure" di IDEE #13); (B) chiusura dei follow-up parcheggiati dalla review finale del piano quick-win: F1-residuo (LastSaveTask), sync visiva throttle tra tab, F3 (enumerazione copia tollerante), F4 (path catturati all'export), F5 (clamp ThrottleMBps al load), F6 (ETA con giorni), F7 (velocità negativa transitoria), comparer platform-aware per il confronto.

**Architecture:** Il dialog di conferma segue il pattern `SelectPathDialog` (Window + ViewModel parametrizzato + helper statico in `ViewModels/`), con un seam di override statico per i test headless. I follow-up sono fix puntuali nei file esistenti, ciascuno con test dedicato.

**Tech Stack:** .NET 8, Avalonia 11, ReactiveUI, xunit.

**Spec:** IDEE.md punto 13 ("azioni sicure") per la Fase A; report della review finale del piano `2026-08-18-quattro-quick-win-idee.md` (findings F1-F7 e ruling Task 9) per la Fase B.

## Global Constraints

- .NET 8, `dotnet build FileExplorer.sln`, test con `dotnet test`.
- Layering Views → ViewModels → Services → Models; nessun DI; servizi statici.
- Mai colori hardcoded: solo `{DynamicResource Brush.*}` con chiavi ESISTENTI in `Styles/Palette.axaml` (verificare prima di usare una chiave). Icone `fa-*`.
- Stringhe UI e commenti in italiano. Niente co-author nei commit. Mai commit su `main`.
- Test: xunit, `sealed` + `IDisposable`, tempdir `fe-<nome>-<guid>`, stato statico (incluso ogni Override/event introdotto da questo piano) salvato nel costruttore e ripristinato in `Dispose()`.
- Nessun warning nuovo (baseline 2: CA2263 DiskTypeServiceTests, CA1859 RemoteBrowserViewModel).
- Ogni task dichiara il modello del subagente (`haiku` = trascrizione/meccanico, `sonnet` = standard).
- Al termine di ogni task: spuntare i checkbox del task in questo file.

**Branch per fase:**
- Fase A: `feature/duplicates-confirm` (Task 1–2) — da `main` aggiornato
- Fase B: `feature/quickwin-followups` (Task 3–6) — da `main` aggiornato, indipendente dalla Fase A (nessun file in comune: la Fase A tocca solo ConfirmDialog*/DuplicatesViewModel/DuplicatesView; la Fase B non li tocca)

Ogni fase termina con push e `gh pr create` verso `main`.

---

## Fase A — Conferma eliminazione nella tab Duplicati

Stato attuale: `DuplicatesViewModel.DeleteFileCommand` e `KeepFirstCommand` eliminano immediatamente con `File.Delete`, senza conferma. Avalonia non ha MessageBox built-in: serve un componente dedicato, pattern `SelectPathDialog`/`SelectPathDialogHelper`.

### Task 1: ConfirmDialog (finestra + ViewModel + helper)

**Modello:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/ConfirmDialogViewModel.cs`
- Create: `FileExplorer/Views/ConfirmDialog.axaml`
- Create: `FileExplorer/Views/ConfirmDialog.axaml.cs`
- Create: `FileExplorer/ViewModels/ConfirmDialogHelper.cs`

**Interfaces:**
- Produces: `ConfirmDialogHelper.ShowAsync(string title, string message, string confirmLabel)` → `Task<bool>`; seam di test `internal static Func<string, string, string, Task<bool>>? Override`.

- [ ] **Step 1: ViewModel**

```csharp
namespace FileExplorer.ViewModels;

/// <summary>Contenuto di un dialog di conferma: titolo, messaggio e label del bottone di conferma.</summary>
public class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(string title, string message, string confirmLabel)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
}
```

- [ ] **Step 2: Finestra**

`ConfirmDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        x:Class="FileExplorer.Views.ConfirmDialog"
        Title="{Binding Title}"
        Width="440" SizeToContent="Height"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface}">

  <StackPanel Margin="20" Spacing="16">

    <StackPanel Orientation="Horizontal" Spacing="12">
      <i:Icon Value="fa-solid fa-triangle-exclamation" FontSize="24"
              Foreground="{DynamicResource Brush.WarningFg}" VerticalAlignment="Top" />
      <TextBlock Text="{Binding Message}" TextWrapping="Wrap" MaxWidth="360"
                 Foreground="{DynamicResource Brush.TextPrimary}" VerticalAlignment="Center" />
    </StackPanel>

    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button Classes="secondary" Content="Annulla" Click="OnCancelClick" />
      <Button Classes="primary" Content="{Binding ConfirmLabel}" Click="OnConfirmClick" />
    </StackPanel>

  </StackPanel>
</Window>
```

(verificare che `Brush.WarningFg` esista in Palette.axaml — è usato da SelectPathDialog per l'icona cartella; se assente usare `Brush.ErrorFg`.)

`ConfirmDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileExplorer.Views;

/// <summary>Dialog modale di conferma: restituisce true su conferma, false su annulla/chiusura.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
```

- [ ] **Step 3: Helper con seam di test**

```csharp
using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using FileExplorer.Views;

namespace FileExplorer.ViewModels;

/// <summary>
/// Apertura del dialog di conferma, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la risposta dell'utente.
/// </summary>
internal static class ConfirmDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<string, string, string, Task<bool>>? Override { get; set; }

    public static async Task<bool> ShowAsync(string title, string message, string confirmLabel)
    {
        if (Override is not null)
            return await Override(title, message, confirmLabel);

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return false; // senza finestra non c'è conferma: default sicuro, non si elimina nulla.

        var dialog = new ConfirmDialog
        {
            DataContext = new ConfirmDialogViewModel(title, message, confirmLabel)
        };

        return await dialog.ShowDialog<bool?>(owner) ?? false;
    }
}
```

- [ ] **Step 4: Build clean and commit**

Run: `dotnet build FileExplorer.sln` — 0 errori, nessun warning nuovo.

```bash
git add FileExplorer/ViewModels/ConfirmDialogViewModel.cs FileExplorer/Views/ConfirmDialog.axaml FileExplorer/Views/ConfirmDialog.axaml.cs FileExplorer/ViewModels/ConfirmDialogHelper.cs
git commit -m "feat(dialog): componente ConfirmDialog riusabile con seam per i test"
```

### Task 2: Conferma nelle eliminazioni della tab Duplicati

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/DuplicatesViewModel.cs`
- Modify: `IDEE.md` (punto 13: nessun cambio di stato, resta `[x]` — nessuna modifica se non serve)
- Test: `FileExplorer.Tests/DuplicatesViewModelTests.cs` (aggiunta)

**Interfaces:**
- Consumes: `ConfirmDialogHelper.ShowAsync` e `Override` (Task 1), `SizeFormatter.Format` (esistente).
- Produces: `DuplicatesViewModel.ConfirmAndDeleteFileAsync(DuplicateFileViewModel)` e `ConfirmAndKeepFirstAsync(DuplicateGroupViewModel)` (pubblici, usati dai comandi); `DeleteFileAsync`/`KeepFirstAsync` restano invariati (core senza conferma, già testati).

- [ ] **Step 1: Write the failing tests**

Aggiungere a `DuplicatesViewModelTests` (rispettando i pattern esistenti della classe; la classe DEVE ripristinare `ConfirmDialogHelper.Override = null` in `Dispose` — aggiungerlo se si imposta nei test):

```csharp
    [Fact]
    public async Task ConfirmAndDeleteFile_WhenDeclined_DoesNotDelete()
    {
        string file1 = Path.Combine(_tempDir, "a.bin");
        string file2 = Path.Combine(_tempDir, "b.bin");
        await File.WriteAllBytesAsync(file1, new byte[4]);
        await File.WriteAllBytesAsync(file2, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, new[] { file1, file2 }.ToList()));
        viewModel.Groups.Add(group);

        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);

        await viewModel.ConfirmAndDeleteFileAsync(group.Files[0]);

        Assert.True(File.Exists(file1));
        Assert.Equal(2, group.Files.Count);
    }

    [Fact]
    public async Task ConfirmAndDeleteFile_WhenConfirmed_DeletesAndPassesFilePathInMessage()
    {
        string file1 = Path.Combine(_tempDir, "c.bin");
        string file2 = Path.Combine(_tempDir, "d.bin");
        await File.WriteAllBytesAsync(file1, new byte[4]);
        await File.WriteAllBytesAsync(file2, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, new[] { file1, file2 }.ToList()));
        viewModel.Groups.Add(group);

        string? receivedMessage = null;
        ConfirmDialogHelper.Override = (_, message, _) =>
        {
            receivedMessage = message;
            return Task.FromResult(true);
        };

        await viewModel.ConfirmAndDeleteFileAsync(group.Files[0]);

        Assert.False(File.Exists(file1));
        Assert.NotNull(receivedMessage);
        Assert.Contains(file1, receivedMessage!);
    }

    [Fact]
    public async Task ConfirmAndKeepFirst_AsksOnceWithCount_AndDeletesRestOnConfirm()
    {
        string file1 = Path.Combine(_tempDir, "k1.bin");
        string file2 = Path.Combine(_tempDir, "k2.bin");
        string file3 = Path.Combine(_tempDir, "k3.bin");
        foreach (var f in new[] { file1, file2, file3 })
            await File.WriteAllBytesAsync(f, new byte[4]);

        using var viewModel = new DuplicatesViewModel();
        var group = new DuplicateGroupViewModel(new DuplicateGroup(4, new[] { file1, file2, file3 }.ToList()));
        viewModel.Groups.Add(group);

        int calls = 0;
        ConfirmDialogHelper.Override = (_, message, _) =>
        {
            calls++;
            Assert.Contains("2 file", message);
            return Task.FromResult(true);
        };

        await viewModel.ConfirmAndKeepFirstAsync(group);

        Assert.Equal(1, calls);
        Assert.True(File.Exists(file1));
        Assert.False(File.Exists(file2));
        Assert.False(File.Exists(file3));
    }
```

Nota: verificare la firma reale di `DuplicateGroup` (in `DuplicateFinderService.cs`) e adattare la costruzione nel test (positional record o proprietà). Aggiungere `using FileExplorer.Services;` se serve.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfirmAnd"`
Expected: FAIL (compile error: metodi inesistenti)

- [ ] **Step 3: Implement**

In `DuplicatesViewModel`:

1. Comandi nel costruttore:

```csharp
        DeleteFileCommand = ReactiveCommand.CreateFromTask<DuplicateFileViewModel>(ConfirmAndDeleteFileAsync);
        KeepFirstCommand = ReactiveCommand.CreateFromTask<DuplicateGroupViewModel>(ConfirmAndKeepFirstAsync);
```

2. Nuovi metodi:

```csharp
    /// <summary>Chiede conferma e poi elimina il singolo file. Pubblico per i test.</summary>
    public async Task ConfirmAndDeleteFileAsync(DuplicateFileViewModel file)
    {
        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Elimina file",
            $"Eliminare definitivamente \"{file.FilePath}\"?\nL'operazione non è reversibile.",
            "Elimina");

        if (confirmed)
            await DeleteFileAsync(file);
    }

    /// <summary>Chiede conferma una sola volta per il gruppo e poi elimina tutte le copie tranne la prima. Pubblico per i test.</summary>
    public async Task ConfirmAndKeepFirstAsync(DuplicateGroupViewModel group)
    {
        var toDelete = group.Files.Skip(1).ToList();
        if (toDelete.Count == 0)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Tieni solo il primo",
            $"Eliminare definitivamente {toDelete.Count} file ({SizeFormatter.Format(group.FileSize * toDelete.Count)})?\n" +
            $"Resta solo \"{group.Files[0].FilePath}\". L'operazione non è reversibile.",
            "Elimina");

        if (confirmed)
            await KeepFirstAsync(group);
    }
```

`DeleteFileAsync` e `KeepFirstAsync` restano invariati.

- [ ] **Step 4: Run the suite**

Run: `dotnet test`
Expected: PASS (i test esistenti che chiamano direttamente `DeleteFileAsync`/`KeepFirstAsync` non passano dal dialog e restano verdi).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/DuplicatesViewModel.cs FileExplorer.Tests/DuplicatesViewModelTests.cs
git commit -m "feat(dedup): dialog di conferma prima di eliminare i duplicati"
```

Fine fase: push e `gh pr create` (base `main`, titolo "Conferma eliminazione nella tab Duplicati").

---

## Fase B — Follow-up della review finale quick-win

### Task 3: Micro-fix servizi (F5, F6, F7)

**Modello:** haiku

**Files:**
- Modify: `FileExplorer/Services/SpeedTracker.cs` (F7)
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs` (F6, solo `FormatEta`)
- Modify: `FileExplorer/Services/AppSettingsStore.cs` (F5)
- Test: `FileExplorer.Tests/SpeedTrackerTests.cs`, `FileExplorer.Tests/AppSettingsStoreTests.cs`, `FileExplorer.Tests/CopyPairsViewModelTests.cs` (aggiunte)

- [ ] **Step 1: Write the failing tests**

`SpeedTrackerTests`:

```csharp
    [Fact]
    public void Report_OutOfOrderCumulative_NeverNegativeCurrent()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0; tracker.Report(500);
        now = 2.0; tracker.Report(400); // cumulativo out-of-order da callback paralleli

        Assert.True(tracker.CurrentBytesPerSecond >= 0);
        Assert.All(tracker.Samples, sample => Assert.True(sample >= 0));
    }
```

`AppSettingsStoreTests` (seguire il pattern esistente della classe per path temporaneo):

```csharp
    [Fact]
    public async Task Load_ClampsThrottleMBps()
    {
        string path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path, "{\"ThrottleMBps\": 99999}");

        var settings = await AppSettingsStore.LoadAsync(path);

        Assert.Equal(1000, settings.ThrottleMBps);
    }
```

`CopyPairsViewModelTests` — `FormatEta` è privato: testarlo dal comportamento osservabile è sproporzionato; renderlo `internal static` (il progetto ha InternalsVisibleTo):

```csharp
    [Fact]
    public void FormatEta_OverOneDay_ShowsDays()
    {
        double twoDays = 2 * 24 * 3600 + 3 * 3600 + 4 * 60 + 5; // 2g 3:04:05
        Assert.Equal("2g 3:04:05", CopyPairsViewModel.FormatEta(twoDays));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Report_OutOfOrder|FullyQualifiedName~Load_ClampsThrottle|FullyQualifiedName~FormatEta_Over"`
Expected: FAIL (i primi due per assert, il terzo per compile error su metodo privato)

- [ ] **Step 3: Implement**

F7 — in `SpeedTracker.CurrentLocked`, clamp a zero:

```csharp
        var oldest = _points[0];
        double window = now - oldest.Time;
        // Cumulativi out-of-order dai callback paralleli possono dare delta negativi: clamp a 0.
        return window > 0 ? Math.Max(0, (_lastBytes - oldest.Bytes) / window) : 0;
```

F6 — in `CopyPairsViewModel`, `FormatEta` diventa `internal static` e gestisce i giorni:

```csharp
    internal static string FormatEta(double? etaSeconds)
    {
        if (etaSeconds is null || !double.IsFinite(etaSeconds.Value))
            return "—";
        var time = TimeSpan.FromSeconds(Math.Min(etaSeconds.Value, TimeSpan.MaxValue.TotalSeconds - 1));
        if (time.TotalDays >= 1)
            return $"{(int)time.TotalDays}g {time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)}";
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
```

F5 — in `AppSettingsStore`: costanti `private const int MinThrottleMBps = 1;` e `private const int MaxThrottleMBps = 1000;` (commento: mirror dei limiti in SettingsViewModel/CopyPairsViewModel) e in `Clamp(...)`:

```csharp
        settings.ThrottleMBps = Math.Clamp(settings.ThrottleMBps, MinThrottleMBps, MaxThrottleMBps);
```

- [ ] **Step 4: Run the suite and commit**

Run: `dotnet test` — PASS.

```bash
git add FileExplorer/Services/SpeedTracker.cs FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/Services/AppSettingsStore.cs FileExplorer.Tests/SpeedTrackerTests.cs FileExplorer.Tests/AppSettingsStoreTests.cs FileExplorer.Tests/CopyPairsViewModelTests.cs
git commit -m "fix(followup): clamp velocità negativa, ETA con giorni, clamp throttle al load"
```

### Task 4: Throttle — LastSaveTask + sync visiva tra le tab (F1-residuo + ruling Task 3)

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/AppSettingsStore.cs` (evento)
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Modify: `FileExplorer/ViewModels/SettingsViewModel.cs`
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs`, `FileExplorer.Tests/SettingsViewModelTests.cs` (aggiunte/modifiche)

**Interfaces:**
- Produces: `AppSettingsStore.ThrottleChanged` (`event Action?`) + `RaiseThrottleChanged()`; `CopyPairsViewModel.LastSaveTask` (`internal Task?`).

- [ ] **Step 1: Write the failing tests**

`CopyPairsViewModelTests`:

```csharp
    [Fact]
    public async Task ThrottleSetters_ExposeAwaitableSaveTask()
    {
        var viewModel = new CopyPairsViewModel();

        viewModel.ThrottleEnabled = !viewModel.ThrottleEnabled;

        Assert.NotNull(viewModel.LastSaveTask);
        await viewModel.LastSaveTask!;
    }

    [Fact]
    public void ThrottleChangedFromSettings_RaisesPropertyChangedOnCopyPairs()
    {
        var copyViewModel = new CopyPairsViewModel();
        var settingsViewModel = new SettingsViewModel();

        var raised = new List<string?>();
        copyViewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        settingsViewModel.ThrottleEnabled = !settingsViewModel.ThrottleEnabled;

        Assert.Contains(nameof(CopyPairsViewModel.ThrottleEnabled), raised);
        Assert.Contains(nameof(CopyPairsViewModel.ThrottleMBps), raised);
    }
```

Inoltre: i due test throttle esistenti della classe (`ThrottleEnabled_RoundTripsThroughSettings`, `ThrottleMBps_ClampsToRange`) vanno aggiornati per attendere `viewModel.LastSaveTask` prima di uscire (chiusura definitiva del residuo F1: nessuna scrittura fire-and-forget sopravvive al test).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ThrottleSetters_Expose|FullyQualifiedName~ThrottleChangedFromSettings"`
Expected: FAIL (compile error: LastSaveTask inesistente; PropertyChanged non sollevato)

- [ ] **Step 3: Implement**

1. `AppSettingsStore`:

```csharp
    /// <summary>
    /// Sollevato quando le impostazioni del limite di banda cambiano da una vista,
    /// così le altre viste (Impostazioni ↔ Copia) possono rinfrescare i propri binding.
    /// </summary>
    public static event Action? ThrottleChanged;

    public static void RaiseThrottleChanged() => ThrottleChanged?.Invoke();
```

2. `CopyPairsViewModel`: sostituire `_ = SaveSettingsBestEffortAsync();` in entrambi i setter con `LastSaveTask = SaveSettingsBestEffortAsync();` preceduto da `AppSettingsStore.RaiseThrottleChanged();`, e aggiungere:

```csharp
    /// <summary>Task dell'ultimo salvataggio impostazioni avviato dai setter del throttle. Solo per i test.</summary>
    internal Task? LastSaveTask { get; private set; }
```

Nel costruttore, sottoscrizione (le VM delle tab vivono quanto l'app, l'handler statico non è un leak; nei test gli handler orfani si limitano a un RaisePropertyChanged innocuo):

```csharp
        AppSettingsStore.ThrottleChanged += () =>
        {
            this.RaisePropertyChanged(nameof(ThrottleEnabled));
            this.RaisePropertyChanged(nameof(ThrottleMBps));
        };
```

3. `SettingsViewModel`: nei setter `ThrottleEnabled`/`ThrottleMBps`, dopo `SaveCurrent();` aggiungere `AppSettingsStore.RaiseThrottleChanged();`; stessa sottoscrizione nel costruttore (creare il costruttore se assente).

Nota re-entrancy: i setter hanno guard di uguaglianza in testa → l'evento non provoca loop.

- [ ] **Step 4: Run the suite and commit**

Run: `dotnet test` — PASS (aggiornati anche i due test esistenti con l'await di LastSaveTask).

```bash
git add FileExplorer/Services/AppSettingsStore.cs FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer.Tests/CopyPairsViewModelTests.cs FileExplorer.Tests/SettingsViewModelTests.cs
git commit -m "fix(throttle): salvataggio attendibile nei test e sync visiva tra le tab"
```

### Task 5: Confronto — path catturati all'export + comparer platform-aware (F4 + ruling Task 9)

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/DirectoryComparisonService.cs`
- Modify: `FileExplorer/ViewModels/ComparisonViewModel.cs`
- Test: `FileExplorer.Tests/DirectoryComparisonServiceTests.cs`, `FileExplorer.Tests/ComparisonViewModelTests.cs` (aggiunte)

**Interfaces:**
- Produces: overload `DirectoryComparisonService.CompareAsync(..., StringComparer pathComparer, CancellationToken ct)`; l'overload esistente sceglie `DefaultPathComparer` (OrdinalIgnoreCase su Windows/macOS, Ordinal altrove), esposto `internal static StringComparer DefaultPathComparer`.

- [ ] **Step 1: Write the failing tests**

`DirectoryComparisonServiceTests`:

```csharp
    [Fact]
    public async Task CompareAsync_CaseInsensitiveComparer_MatchesDifferentCase()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "Same.TXT"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_right, "same.txt"), "uguale");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, StringComparer.OrdinalIgnoreCase, CancellationToken.None);

        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
        Assert.Single(result.Identical);
    }

    [Fact]
    public void DefaultPathComparer_MatchesPlatform()
    {
        bool caseInsensitiveFs = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var comparer = DirectoryComparisonService.DefaultPathComparer;

        Assert.Equal(caseInsensitiveFs, comparer.Equals("A.TXT", "a.txt"));
    }
```

`ComparisonViewModelTests`:

```csharp
    [Fact]
    public async Task ExportAsync_UsesPathsCapturedAtCompareTime()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.CompareAsync();

        // L'utente cambia i path dopo il confronto: l'export deve usare quelli confrontati.
        viewModel.LeftPath = "/altro/path";
        viewModel.RightPath = null;

        string exportDir = Path.Combine(_tempDir, "export2");
        Directory.CreateDirectory(exportDir);
        string? written = await viewModel.ExportAsync(ComparisonReportFormat.Json, exportDir);

        Assert.NotNull(written);
        string json = await File.ReadAllTextAsync(written!);
        Assert.Contains(_left.Replace("\\", "\\\\"), json);
        Assert.DoesNotContain("/altro/path", json);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CaseInsensitiveComparer|FullyQualifiedName~DefaultPathComparer|FullyQualifiedName~UsesPathsCapturedAtCompare"`
Expected: FAIL (compile error sull'overload; export usa i path correnti)

- [ ] **Step 3: Implement**

`DirectoryComparisonService`:

```csharp
    /// <summary>
    /// Comparer di default per i path relativi: case-insensitive sui filesystem
    /// tipicamente case-insensitive (Windows, macOS), byte-exact altrove.
    /// </summary>
    internal static StringComparer DefaultPathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot, string rightRoot, int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress, CancellationToken ct)
        => CompareAsync(leftRoot, rightRoot, maxDegreeOfParallelism, onProgress, DefaultPathComparer, ct);
```

L'overload nuovo prende `StringComparer pathComparer` e lo usa: `new Dictionary<string, long>(pathComparer)` in `RelativeFileSet` (passargli il comparer), e `OrderBy(p => p, pathComparer)` al posto di `StringComparer.Ordinal` nei quattro ordinamenti. I path nei risultati per i file comuni restano quelli del lato sinistro (chiavi del dizionario left).

`ComparisonViewModel`: aggiungere campi `private string? _comparedLeftRoot; private string? _comparedRightRoot;`, valorizzati al successo del confronto (dove viene assegnato `Result`); `ExportAsync` usa quelli al posto di `LeftPath!`/`RightPath!` e ritorna `null` se sono assenti (niente null-forgiving).

- [ ] **Step 4: Run the suite and commit**

Run: `dotnet test` — PASS.

```bash
git add FileExplorer/Services/DirectoryComparisonService.cs FileExplorer/ViewModels/ComparisonViewModel.cs FileExplorer.Tests/DirectoryComparisonServiceTests.cs FileExplorer.Tests/ComparisonViewModelTests.cs
git commit -m "fix(confronto): comparer platform-aware e path catturati al momento del confronto"
```

### Task 6: Copia — enumerazione tollerante come la simulazione (F3)

**Modello:** sonnet

**Files:**
- Modify: `FileExplorer/Services/FileCopyService.cs`
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs` (aggiunta)

- [ ] **Step 1: Write the failing test**

```csharp
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
```

(adattare `_root` al nome reale del campo tempdir della classe.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SymlinkLoop"`
Expected: FAIL (eccezione o timeout per il loop di symlink — se l'enumerazione lancia IOException il test fallisce comunque)

- [ ] **Step 3: Implement**

In `FileCopyService` aggiungere il campo (stesso pattern di `CopySimulationService`):

```csharp
    /// <summary>
    /// Enumerazione tollerante, identica a quella della simulazione: ignora i file
    /// inaccessibili e non segue i reparse point (symlink), evitando i loop.
    /// </summary>
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };
```

e sostituire in `CopyDirectoryAsync` e `CopyDirectoryToManyAsync`:

```csharp
        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration).ToList();
```

(al posto di `SearchOption.AllDirectories`; il parametro pattern `"*"` resta.)

- [ ] **Step 4: Run the suite and commit**

Run: `dotnet test` — PASS (i test esistenti di copia directory non usano symlink/hidden e restano verdi; `AttributesToSkip = ReparsePoint` esplicito non salta Hidden/System, quindi il set di file copiati per gli alberi normali è identico).

```bash
git add FileExplorer/Services/FileCopyService.cs FileExplorer.Tests/FileCopyServiceTests.cs
git commit -m "fix(copy): enumerazione tollerante allineata alla simulazione (skip symlink, ignore inaccessibili)"
```

Fine fase: push e `gh pr create` (base `main`, titolo "Follow-up review finale quick-win (F1, F3-F7, sync throttle, comparer)").
