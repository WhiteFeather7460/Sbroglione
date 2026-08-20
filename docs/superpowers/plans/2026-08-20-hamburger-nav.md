# Menu Hamburger Laterale (IDEA 22) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sostituire la tab bar orizzontale di `MainWindow` con una navigazione laterale a sinistra: pannello collassabile (solo icone ↔ icone + etichette) con bottone hamburger, stato espanso/collassato persistito nelle impostazioni.

**Architecture:** Si mantiene il `TabControl` esistente (nessun cambio al ciclo di vita delle 7 view figlie) passando a `TabStripPlacement="Left"`. Un bottone hamburger sopra la finestra alterna `IsNavExpanded` su `MainWindowViewModel` (oggi vuoto); le `TextBlock` delle etichette nei `TabItem.Header` legano `IsVisible` a quella proprietà. Lo stato è persistito in `AppSettings.NavExpanded` via `AppSettingsStore` (pattern già esistente). Nessuna nuova chiave colore: si riusano i brush `Brush.*` esistenti.

**Tech Stack:** Avalonia 11 (Fluent), ReactiveUI, Projektanker.Icons.Avalonia (FontAwesome), xunit.

**Spec:** `IDEE.md` voce 22 (nessun design doc separato: task bounded, design approvato in chat).

## Global Constraints

- Mai colori hardcoded nelle view: solo `{DynamicResource Brush.*}` (CLAUDE.md).
- Icone via Projektanker (`<i:Icon Value="fa-..."/>`).
- Nessun commit su `main`: branch `feature/hamburger-nav`, PR a fine lavoro.
- Non aggiungere Claude come co-author nei commit.
- `dotnet build FileExplorer.sln` e `dotnet test` devono passare a fine di ogni task.
- Testi UI in italiano (convenzione app: "Copia", "Impostazioni", ...).

---

### Task 1: `AppSettings.NavExpanded` + persistenza

**Modello subagent:** `haiku` (campo + test meccanico)

**Files:**
- Modify: `FileExplorer/Models/AppSettings.cs`
- Test: `FileExplorer.Tests/AppSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `AppSettingsStore` esistenti.
- Produces: `bool AppSettings.NavExpanded { get; set; }` (default `true`) — usato da Task 2.

- [x] **Step 1: Test fallente** — in `AppSettingsStoreTests.cs` aggiungere:

```csharp
[Fact]
public async Task SaveAsync_ThenLoad_RoundTripsNavExpanded()
{
    var settings = new AppSettings { NavExpanded = false };

    await AppSettingsStore.SaveAsync(StorePath, settings);
    var loaded = await AppSettingsStore.LoadAsync(StorePath);

    Assert.False(loaded.NavExpanded);
}

[Fact]
public async Task LoadAsync_MissingFile_NavExpandedDefaultsTrue()
{
    var settings = await AppSettingsStore.LoadAsync(StorePath);
    Assert.True(settings.NavExpanded);
}
```

- [x] **Step 2: Verifica FAIL** — `dotnet test --filter NavExpanded` → errore di compilazione (proprietà inesistente), atteso.

- [x] **Step 3: Implementazione** — in `AppSettings.cs`, dopo `CustomThemeId`:

```csharp
    /// <summary>Pannello di navigazione laterale espanso (icone + etichette) o collassato (solo icone).</summary>
    public bool NavExpanded { get; set; } = true;
```

- [x] **Step 4: Verifica PASS** — `dotnet test --filter NavExpanded` → 2 PASS.

- [x] **Step 5: Commit** — `git add` dei due file, `git commit -m "feat(nav): persist NavExpanded in AppSettings"`.

---

### Task 2: `MainWindowViewModel` — toggle + persistenza

**Modello subagent:** `sonnet`

**Files:**
- Modify: `FileExplorer/ViewModels/MainWindowViewModel.cs`
- Create: `FileExplorer.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings.NavExpanded` (Task 1), `AppSettingsStore.Current`/`SaveCurrentAsync`, `ViewModelBase` (ReactiveObject).
- Produces: `bool IsNavExpanded { get; }` (notifica via ReactiveUI), `ReactiveCommand<Unit, Unit> ToggleNavCommand`, `Task ToggleNavAsync()` — usati da Task 3 (binding XAML).

- [x] **Step 1: Test fallente** — creare `MainWindowViewModelTests.cs`:

```csharp
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public MainWindowViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-mainvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Constructor_ReadsNavExpandedFromSettings()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = false };
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsNavExpanded);
    }

    [Fact]
    public async Task ToggleNavAsync_FlipsStateAndPersists()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = true };
        var vm = new MainWindowViewModel();

        await vm.ToggleNavAsync();

        Assert.False(vm.IsNavExpanded);
        Assert.False(AppSettingsStore.Current.NavExpanded);
        var reloaded = await AppSettingsStore.LoadAsync(AppSettingsStore.CurrentPath);
        Assert.False(reloaded.NavExpanded);
    }

    [Fact]
    public async Task ToggleNavAsync_Twice_ReturnsToExpanded()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = true };
        var vm = new MainWindowViewModel();

        await vm.ToggleNavAsync();
        await vm.ToggleNavAsync();

        Assert.True(vm.IsNavExpanded);
        Assert.True(AppSettingsStore.Current.NavExpanded);
    }
}
```

- [x] **Step 2: Verifica FAIL** — `dotnet test --filter MainWindowViewModel` → errore di compilazione (membri inesistenti), atteso.

- [x] **Step 3: Implementazione** — sostituire `MainWindowViewModel.cs`:

```csharp
using System.Reactive;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Stato della shell: pannello di navigazione laterale espanso/collassato (persistito).</summary>
public class MainWindowViewModel : ViewModelBase
{
    private bool _isNavExpanded;

    public MainWindowViewModel()
    {
        _isNavExpanded = AppSettingsStore.Current.NavExpanded;
        ToggleNavCommand = ReactiveCommand.CreateFromTask(ToggleNavAsync);
    }

    public bool IsNavExpanded
    {
        get => _isNavExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isNavExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleNavCommand { get; }

    public async Task ToggleNavAsync()
    {
        IsNavExpanded = !IsNavExpanded;
        AppSettingsStore.Current.NavExpanded = IsNavExpanded;
        await AppSettingsStore.SaveCurrentAsync().ConfigureAwait(false);
    }
}
```

- [x] **Step 4: Verifica PASS** — `dotnet test --filter MainWindowViewModel` → 3 PASS; poi `dotnet test` completo → PASS.

- [x] **Step 5: Commit** — `git commit -m "feat(nav): toggle e persistenza pannello laterale in MainWindowViewModel"`.

---

### Task 3: `MainWindow.axaml` — rail sinistro + hamburger + stili

**Modello subagent:** `sonnet`

**Files:**
- Modify: `FileExplorer/Views/MainWindow.axaml`
- Modify: `FileExplorer/Styles/Controls.axaml`
- Modify: `IDEE.md` (voce 22 → `[x]`)

**Interfaces:**
- Consumes: `IsNavExpanded`, `ToggleNavCommand` (Task 2). DataContext della finestra è `MainWindowViewModel` (già impostato in `App.OnFrameworkInitializationCompleted`); i `TabItem.Header` ereditano quel DataContext (le view figlie sovrascrivono il proprio solo nel loro sottoalbero).
- Produces: layout finale; nessuna API per task successivi.

- [x] **Step 1: Ristrutturare `MainWindow.axaml`** — griglia a due righe: hamburger in alto a sinistra, `TabControl` con `TabStripPlacement="Left"` sotto. Ogni etichetta lega `IsVisible` a `IsNavExpanded`; tooltip fisso su ogni `TabItem` (utile da collassato). Contenuto completo:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        xmlns:views="clr-namespace:FileExplorer.Views"
        xmlns:vm="clr-namespace:FileExplorer.ViewModels"
        x:Class="FileExplorer.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="File Explorer"
        Width="900" Height="640"
        MinWidth="640" MinHeight="480"
        Background="{DynamicResource Brush.Surface}">

  <Grid RowDefinitions="Auto,*">
    <Button Grid.Row="0"
            Classes="iconbtn"
            Margin="8 8 0 0"
            HorizontalAlignment="Left"
            ToolTip.Tip="Espandi/comprimi menu"
            Command="{Binding ToggleNavCommand}">
      <i:Icon Value="fa-solid fa-bars" />
    </Button>

    <TabControl Grid.Row="1" Classes="nav" Padding="0" TabStripPlacement="Left">
      <TabItem ToolTip.Tip="Copia">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-copy" />
            <TextBlock Text="Copia" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:CopyPairsView />
      </TabItem>
      <TabItem ToolTip.Tip="Server remoto">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-server" />
            <TextBlock Text="Server remoto" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:RemoteBrowserView />
      </TabItem>
      <TabItem ToolTip.Tip="Confronto">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-code-compare" />
            <TextBlock Text="Confronto" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:ComparisonView />
      </TabItem>
      <TabItem ToolTip.Tip="Sync auto">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-rotate" />
            <TextBlock Text="Sync auto" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:WatchFoldersView />
      </TabItem>
      <TabItem ToolTip.Tip="Duplicati">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-clone" />
            <TextBlock Text="Duplicati" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:DuplicatesView />
      </TabItem>
      <TabItem ToolTip.Tip="Spazio disco">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-chart-pie" />
            <TextBlock Text="Spazio disco" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:DiskUsageView />
      </TabItem>
      <TabItem ToolTip.Tip="Impostazioni">
        <TabItem.Header>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-gear" />
            <TextBlock Text="Impostazioni" IsVisible="{Binding IsNavExpanded}" />
          </StackPanel>
        </TabItem.Header>
        <views:SettingsView />
      </TabItem>
    </TabControl>
  </Grid>

</Window>
```

Nota: se `x:DataType` causa errori di binding in compilazione (il progetto potrebbe non usare compiled bindings altrove), rimuovere `x:DataType` e `xmlns:vm` e usare binding classici — verificare com'è fatto nelle altre view e restare coerenti.

- [x] **Step 2: Stili nav in `Controls.axaml`** — in fondo al file, dimensioni/allineamento voci verticali (nessun colore hardcoded; i colori restano quelli del tema Fluent + brush esistenti):

```xml
  <!-- Navigazione laterale (MainWindow) -->
  <Style Selector="TabControl.nav TabItem">
    <Setter Property="FontSize" Value="14" />
    <Setter Property="FontWeight" Value="Normal" />
    <Setter Property="MinHeight" Value="40" />
    <Setter Property="Padding" Value="14 8" />
    <Setter Property="HorizontalContentAlignment" Value="Left" />
  </Style>
  <Style Selector="TabControl.nav TabItem:selected">
    <Setter Property="Foreground" Value="{DynamicResource Brush.Accent}" />
  </Style>
```

- [x] **Step 3: Build + test** — `dotnet build FileExplorer.sln` → 0 errori; `dotnet test` → PASS.

- [x] **Step 4: Smoke run (best effort)** — `dotnet run --project FileExplorer.Desktop` se c'è display disponibile: verificare rail a sinistra, toggle hamburger, tooltip da collassato, persistenza dopo riavvio. In ambiente headless saltare e annotarlo nel report.

- [x] **Step 5: Aggiornare `IDEE.md`** — voce 22: `[ ]` → `[x]`, e aggiungere in coda alla voce: *(implementata: TabControl verticale con etichette collassabili, stato in Impostazioni/settings.json)*.

- [x] **Step 6: Commit** — `git commit -m "feat(nav): menu hamburger laterale al posto della tab bar (IDEA 22)"`.

---

## Verifica finale

- [ ] `dotnet build FileExplorer.sln` e `dotnet test` verdi sul branch.
- [ ] Review whole-branch (superpowers:requesting-code-review) e PR verso `main` via superpowers:finishing-a-development-branch.
