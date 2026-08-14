# UI Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle completo dell'app FileExplorer (corallo→arancio, card, tema chiaro/scuro) senza cambiare alcuna funzionalità.

**Architecture:** FluentTheme resta la base; sopra si aggiungono due file di stile custom (`Styles/Palette.axaml` risorse tema-aware, `Styles/Controls.axaml` stili a classi) e si ristilizzano le tre superfici: MainWindow (shell), CopyPairsView (header gradiente + card), SelectPathDialog (barra rifinita + footer). Un solo dato nuovo di presentazione (`StateKind`) pilota i badge colorati.

**Tech Stack:** Avalonia 11.1 (.NET 8), ReactiveUI, Projektanker.Icons.Avalonia + FontAwesome (unica dipendenza nuova).

**Spec:** `docs/superpowers/specs/2026-08-14-ui-restyle-design.md` — leggerla prima di iniziare.

## Global Constraints

- **Funzionalità invariate**: nessun cambiamento a servizi, logica copia/checksum, validazioni, flussi del dialogo.
- Build sempre a **0 errori / 0 warning** (`dotnet build FileExplorer.sln`): gli analyzer sono in modalità `latest-recommended` con `EnforceCodeStyleInBuild`.
- Nessun pacchetto NuGet oltre `Projektanker.Icons.Avalonia` (+ `.FontAwesome`).
- Palette: accent `#FF5E62` → `#FF9446`; chiaro: surface `#FAF9F7`, card `#FFFFFF`, testo `#2B2420`, muted `#8A7F78`; scuro: surface `#1E1B1A`, card `#2A2624`, testo `#F2ECE7`, muted `#A79A91`. Ritocchi ammessi solo per contrasto.
- Tema: `RequestedThemeVariant="Default"` resta; ogni colore passa da risorse con `ThemeDictionaries` (mai colori hardcoded nelle viste, tranne il bianco su gradiente accent).
- Stringhe UI in italiano; testi esistenti (`Status`, titoli) invariati salvo dove la spec dice altrimenti.
- Titlebar nativa: non toccare `ExtendClientArea*`.
- axaml indent 2 spazi (`.editorconfig`); un hook PostToolUse esegue già `dotnet format whitespace` sui file editati.
- Commit: Conventional Commits, **niente co-author Claude** (regola del repo).
- Niente test automatici (nessun test project): il ciclo di verifica di ogni task è `dotnet build` a 0 warning; smoke run dell'app nel task finale.

---

### Task 0: Commit del refactor pendente (baseline)

Il working tree contiene un refactor completo non committato (rinomina progetti in FileExplorer, split servizi, pulizia). Va committato prima di iniziare, altrimenti i commit del restyle mescolerebbero contenuti.

**Files:**
- Nessuna modifica: solo `git add -A` + commit di quanto già presente.

**Interfaces:**
- Consumes: —
- Produces: baseline pulita su `main`; tutti i task successivi committano solo i propri file.

- [ ] **Step 1: Verifica build della baseline**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 2: Verifica che il diff sia solo il refactor atteso**

Run: `git status --short`
Expected: rinomine `GetStartedApp* → FileExplorer*`, file nuovi in `Models/ Services/ ViewModels/ Views/`, modifiche a `CLAUDE.md`, `.claude/settings.json`, `.gitignore`. Nessun file inatteso (i mockup di `.superpowers/` sono ignorati).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: rename to FileExplorer, split services, remove dead code"
```

---

### Task 1: Pacchetti icone + registrazione provider

**Files:**
- Modify: `FileExplorer/FileExplorer.csproj`
- Modify: `FileExplorer.Desktop/Program.cs`

**Interfaces:**
- Consumes: —
- Produces: namespace xmlns `https://github.com/projektanker/icons.avalonia` utilizzabile in ogni axaml (`<i:Icon Value="fa-solid fa-…" />`, attached `i:Attached.Icon` sui Button). Provider registrato prima della costruzione dell'app.

- [ ] **Step 1: Aggiungi i pacchetti al progetto core**

```bash
dotnet add FileExplorer/FileExplorer.csproj package Projektanker.Icons.Avalonia
dotnet add FileExplorer/FileExplorer.csproj package Projektanker.Icons.Avalonia.FontAwesome
```

(Il progetto Desktop li riceve transitivamente.)

- [ ] **Step 2: Registra il provider in Program.cs**

Sostituisci `BuildAvaloniaApp` in `FileExplorer.Desktop/Program.cs`:

```csharp
using System;

using Avalonia;
using Avalonia.ReactiveUI;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

namespace FileExplorer.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/FileExplorer.csproj FileExplorer.Desktop/Program.cs
git commit -m "feat(ui): add Projektanker icon packs and register FontAwesome provider"
```

---

### Task 2: Palette.axaml (risorse tema chiaro/scuro)

**Files:**
- Create: `FileExplorer/Styles/Palette.axaml`
- Modify: `FileExplorer/App.axaml`

**Interfaces:**
- Consumes: —
- Produces: risorse `{DynamicResource ...}` usate da tutti i task UI: `Brush.AccentGradient`, `Brush.Accent`, `Brush.Surface`, `Brush.Card`, `Brush.CardBorder`, `Brush.Field`, `Brush.TextPrimary`, `Brush.TextMuted`, `Brush.SuccessBg/Fg`, `Brush.WarningBg/Fg`, `Brush.ErrorBg/Fg`, `Brush.ProgressBg/Fg`, `Brush.NeutralBg/Fg`.

- [ ] **Step 1: Crea `FileExplorer/Styles/Palette.axaml`**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Accent identico nelle due varianti -->
  <SolidColorBrush x:Key="Brush.Accent" Color="#FF5E62" />
  <LinearGradientBrush x:Key="Brush.AccentGradient" StartPoint="0%,0%" EndPoint="100%,0%">
    <GradientStop Color="#FF5E62" Offset="0" />
    <GradientStop Color="#FF9446" Offset="1" />
  </LinearGradientBrush>

  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light">
      <SolidColorBrush x:Key="Brush.Surface" Color="#FAF9F7" />
      <SolidColorBrush x:Key="Brush.Card" Color="#FFFFFF" />
      <SolidColorBrush x:Key="Brush.CardBorder" Color="#EEE2DA" />
      <SolidColorBrush x:Key="Brush.Field" Color="#F4EFE9" />
      <SolidColorBrush x:Key="Brush.TextPrimary" Color="#2B2420" />
      <SolidColorBrush x:Key="Brush.TextMuted" Color="#8A7F78" />
      <SolidColorBrush x:Key="Brush.SuccessBg" Color="#E6F6EC" />
      <SolidColorBrush x:Key="Brush.SuccessFg" Color="#1F8A4C" />
      <SolidColorBrush x:Key="Brush.WarningBg" Color="#FBF0D9" />
      <SolidColorBrush x:Key="Brush.WarningFg" Color="#9A6B00" />
      <SolidColorBrush x:Key="Brush.ErrorBg" Color="#FBE5E2" />
      <SolidColorBrush x:Key="Brush.ErrorFg" Color="#C43025" />
      <SolidColorBrush x:Key="Brush.ProgressBg" Color="#FFE9DF" />
      <SolidColorBrush x:Key="Brush.ProgressFg" Color="#D8481F" />
      <SolidColorBrush x:Key="Brush.NeutralBg" Color="#EFEAE5" />
      <SolidColorBrush x:Key="Brush.NeutralFg" Color="#6E635C" />
    </ResourceDictionary>
    <ResourceDictionary x:Key="Dark">
      <SolidColorBrush x:Key="Brush.Surface" Color="#1E1B1A" />
      <SolidColorBrush x:Key="Brush.Card" Color="#2A2624" />
      <SolidColorBrush x:Key="Brush.CardBorder" Color="#3A3430" />
      <SolidColorBrush x:Key="Brush.Field" Color="#35302D" />
      <SolidColorBrush x:Key="Brush.TextPrimary" Color="#F2ECE7" />
      <SolidColorBrush x:Key="Brush.TextMuted" Color="#A79A91" />
      <SolidColorBrush x:Key="Brush.SuccessBg" Color="#22402E" />
      <SolidColorBrush x:Key="Brush.SuccessFg" Color="#7FD8A2" />
      <SolidColorBrush x:Key="Brush.WarningBg" Color="#453A1C" />
      <SolidColorBrush x:Key="Brush.WarningFg" Color="#E8C36A" />
      <SolidColorBrush x:Key="Brush.ErrorBg" Color="#46231F" />
      <SolidColorBrush x:Key="Brush.ErrorFg" Color="#FF9C8F" />
      <SolidColorBrush x:Key="Brush.ProgressBg" Color="#44261C" />
      <SolidColorBrush x:Key="Brush.ProgressFg" Color="#FFA07C" />
      <SolidColorBrush x:Key="Brush.NeutralBg" Color="#35302D" />
      <SolidColorBrush x:Key="Brush.NeutralFg" Color="#B5A89F" />
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

- [ ] **Step 2: Includi la palette in `FileExplorer/App.axaml`**

Sostituisci l'intero file:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="FileExplorer.App"
             RequestedThemeVariant="Default">
  <!-- "Default" ThemeVariant follows system theme variant. "Dark" or "Light" are other available options. -->

  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://FileExplorer/Styles/Palette.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>

  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />
  </Application.Styles>
</Application>
```

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Styles/Palette.axaml FileExplorer/App.axaml
git commit -m "feat(ui): add coral/orange theme palette with light and dark variants"
```

---

### Task 3: Controls.axaml (stili a classi)

**Files:**
- Create: `FileExplorer/Styles/Controls.axaml`
- Modify: `FileExplorer/App.axaml`

**Interfaces:**
- Consumes: risorse `Brush.*` del Task 2.
- Produces: classi usate dalle viste: `Button.primary`, `Button.secondary`, `Button.iconbtn`, `Button.onaccent`, `TextBox.error`, `Border.card`, `Border.badge` (+ `success`/`warning`/`error`/`progress`; il neutro è lo stile base del badge), stile globale `TextBox` e `ProgressBar`.

- [ ] **Step 1: Crea `FileExplorer/Styles/Controls.axaml`**

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ===== Bottoni ===== -->
  <Style Selector="Button.primary">
    <Setter Property="Background" Value="{DynamicResource Brush.AccentGradient}" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16,7" />
  </Style>
  <Style Selector="Button.primary:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.AccentGradient}" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="Opacity" Value="0.85" />
  </Style>
  <Style Selector="Button.primary:disabled /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.NeutralBg}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.NeutralFg}" />
  </Style>

  <Style Selector="Button.secondary">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.TextPrimary}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.CardBorder}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16,7" />
  </Style>
  <Style Selector="Button.secondary:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.Field}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Accent}" />
  </Style>

  <Style Selector="Button.iconbtn">
    <Setter Property="Background" Value="{DynamicResource Brush.Field}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.TextPrimary}" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Width" Value="34" />
    <Setter Property="Height" Value="34" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
  </Style>
  <Style Selector="Button.iconbtn:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.NeutralBg}" />
  </Style>

  <!-- Bottone bianco semi-trasparente per l'header col gradiente -->
  <Style Selector="Button.onaccent">
    <Setter Property="Background" Value="#33FFFFFF" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="14,7" />
  </Style>
  <Style Selector="Button.onaccent:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="#55FFFFFF" />
    <Setter Property="Foreground" Value="White" />
  </Style>

  <!-- ===== Campi di testo ===== -->
  <Style Selector="TextBox">
    <Setter Property="CornerRadius" Value="6" />
    <Setter Property="Background" Value="{DynamicResource Brush.Field}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="BorderBrush" Value="Transparent" />
  </Style>
  <Style Selector="TextBox.error">
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.ErrorFg}" />
    <Setter Property="BorderThickness" Value="2" />
  </Style>

  <!-- ===== Progress ===== -->
  <Style Selector="ProgressBar">
    <Setter Property="MinHeight" Value="6" />
    <Setter Property="Height" Value="6" />
    <Setter Property="CornerRadius" Value="3" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.AccentGradient}" />
    <Setter Property="Background" Value="{DynamicResource Brush.Field}" />
  </Style>

  <!-- ===== Card ===== -->
  <Style Selector="Border.card">
    <Setter Property="Background" Value="{DynamicResource Brush.Card}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.CardBorder}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="12" />
    <Setter Property="Padding" Value="14" />
    <Setter Property="Margin" Value="0,6" />
    <Setter Property="BoxShadow" Value="0 2 8 0 #22FF5E62" />
  </Style>

  <!-- ===== Badge di stato (base = neutro) ===== -->
  <Style Selector="Border.badge">
    <Setter Property="CornerRadius" Value="999" />
    <Setter Property="Padding" Value="10,3" />
    <Setter Property="Background" Value="{DynamicResource Brush.NeutralBg}" />
    <Setter Property="VerticalAlignment" Value="Center" />
  </Style>
  <Style Selector="Border.badge > TextBlock">
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.NeutralFg}" />
  </Style>
  <Style Selector="Border.badge.success">
    <Setter Property="Background" Value="{DynamicResource Brush.SuccessBg}" />
  </Style>
  <Style Selector="Border.badge.success > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource Brush.SuccessFg}" />
  </Style>
  <Style Selector="Border.badge.warning">
    <Setter Property="Background" Value="{DynamicResource Brush.WarningBg}" />
  </Style>
  <Style Selector="Border.badge.warning > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource Brush.WarningFg}" />
  </Style>
  <Style Selector="Border.badge.error">
    <Setter Property="Background" Value="{DynamicResource Brush.ErrorBg}" />
  </Style>
  <Style Selector="Border.badge.error > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource Brush.ErrorFg}" />
  </Style>
  <Style Selector="Border.badge.progress">
    <Setter Property="Background" Value="{DynamicResource Brush.ProgressBg}" />
  </Style>
  <Style Selector="Border.badge.progress > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource Brush.ProgressFg}" />
  </Style>

  <!-- ===== DataGrid ===== -->
  <Style Selector="DataGrid">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="GridLinesVisibility" Value="None" />
  </Style>
  <Style Selector="DataGridRow:pointerover /template/ Rectangle#BackgroundRectangle">
    <Setter Property="Fill" Value="{DynamicResource Brush.Field}" />
    <Setter Property="Opacity" Value="1" />
  </Style>
  <Style Selector="DataGridRow:selected /template/ Rectangle#BackgroundRectangle">
    <Setter Property="Fill" Value="{DynamicResource Brush.ProgressBg}" />
    <Setter Property="Opacity" Value="1" />
  </Style>
</Styles>
```

Nota: se al run gli stili `DataGridRow ... #BackgroundRectangle` non avessero effetto (nome del template part diverso nella versione del tema DataGrid in uso), rimuovere quei due blocchi e ottenere lo stesso effetto override-ando in `Palette.axaml` (fuori dai ThemeDictionaries) le risorse colore del tema DataGrid: `<Color x:Key="DataGridRowHoveredBackgroundColor">#F4EFE9</Color>` e `<Color x:Key="DataGridRowSelectedBackgroundColor">#FFE9DF</Color>`. Verificare l'effetto visivamente nello smoke run del Task 8.

- [ ] **Step 2: Includi gli stili in `FileExplorer/App.axaml`**

In `Application.Styles`, dopo la riga `StyleInclude` del DataGrid, aggiungi:

```xml
    <StyleInclude Source="avares://FileExplorer/Styles/Controls.axaml" />
```

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Styles/Controls.axaml FileExplorer/App.axaml
git commit -m "feat(ui): add class-based control styles (buttons, cards, badges, fields)"
```

---

### Task 4: CopyStateKind + StateKind sul ViewModel

**Files:**
- Create: `FileExplorer/Models/CopyStateKind.cs`
- Create: `FileExplorer/Converters/EnumEqualsConverter.cs`
- Modify: `FileExplorer/ViewModels/FolderFilePairViewModel.cs`
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`

**Interfaces:**
- Consumes: `FolderFilePairViewModel` e `CopyPairsViewModel` esistenti (proprietà `Status`, flusso `StartCopyAsync`/`CopySingleFileAsync`/`CopyDirectoryAsync`).
- Produces: `enum FileExplorer.Models.CopyStateKind { Ready, Copying, Success, Warning, Error, Cancelled }`; proprietà `CopyStateKind StateKind` (notify) su `FolderFilePairViewModel`; proprietà `bool HasPairs` (notify) su `CopyPairsViewModel`; `FileExplorer.Converters.EnumEqualsConverter` (`IValueConverter`, parametro = nome membro enum → bool). Usati dal Task 6.

- [ ] **Step 1: Crea `FileExplorer/Models/CopyStateKind.cs`**

```csharp
namespace FileExplorer.Models;

/// <summary>
/// Stato di presentazione di una coppia di copia: pilota colore e classe del badge.
/// </summary>
public enum CopyStateKind
{
    Ready,
    Copying,
    Success,
    Warning,
    Error,
    Cancelled
}
```

- [ ] **Step 2: Crea `FileExplorer/Converters/EnumEqualsConverter.cs`**

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FileExplorer.Converters;

/// <summary>
/// True se il valore bindato (enum) ha lo stesso nome del parametro.
/// Uso: Classes.success="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Success}".
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null
        && parameter is string name
        && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 3: Aggiungi `StateKind` a `FolderFilePairViewModel`**

In `FileExplorer/ViewModels/FolderFilePairViewModel.cs`, aggiungi `using FileExplorer.Models;` se non già presente (c'è) e, dopo la proprietà `Status`, inserisci:

```csharp
    private CopyStateKind _stateKind = CopyStateKind.Ready;

    /// <summary>
    /// Stato di presentazione del badge; impostato da <see cref="CopyPairsViewModel"/>
    /// negli stessi punti in cui viene impostato <see cref="Status"/>.
    /// </summary>
    public CopyStateKind StateKind
    {
        get => _stateKind;
        set => this.RaiseAndSetIfChanged(ref _stateKind, value);
    }
```

- [ ] **Step 4: Imposta `StateKind` in `CopyPairsViewModel` accanto a ogni `Status`**

Modifiche puntuali in `FileExplorer/ViewModels/CopyPairsViewModel.cs` (il flusso non cambia):

- In `StartCopyAsync`, ramo `!pair.CanStart`: dopo `pair.Status = "Percorsi non validi";` aggiungi `pair.StateKind = CopyStateKind.Error;`
- In `StartCopyAsync`, dopo `pair.Status = "Copia in corso…";` aggiungi `pair.StateKind = CopyStateKind.Copying;`
- In `StartCopyAsync`, `catch (OperationCanceledException)`: dopo `pair.Status = "Annullato";` aggiungi `pair.StateKind = CopyStateKind.Cancelled;`
- In `StartCopyAsync`, `catch (Exception ex)`: dopo `pair.Status = ...;` aggiungi `pair.StateKind = CopyStateKind.Error;`
- In `CopySingleFileAsync`, in fondo, dopo la riga che imposta lo `Status` finale aggiungi:

```csharp
        pair.StateKind = pair.IsVerified == true ? CopyStateKind.Success : CopyStateKind.Warning;
```

- In `CopyDirectoryAsync`, nel blocco finale `if (!ct.IsCancellationRequested && knownFileCount != 0)`, dopo `pair.Status = "Completato";` aggiungi `pair.StateKind = CopyStateKind.Success;`
  (Caso "Nessun file da copiare": `StateKind` resta `Copying`→ riportalo a neutro aggiungendo subito dopo quel blocco `if`:

```csharp
        else if (knownFileCount == 0)
        {
            pair.StateKind = CopyStateKind.Ready;
        }
```

  Il mapping è quello della spec: nessun file → badge neutro.)

- [ ] **Step 5: Aggiungi `HasPairs` a `CopyPairsViewModel`**

Serve all'empty state del Task 6. Nel costruttore, come prima riga:

```csharp
        PathPairs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasPairs));
```

E tra le proprietà:

```csharp
    /// <summary>True se c'è almeno una coppia in lista (pilota l'empty state).</summary>
    public bool HasPairs => PathPairs.Count > 0;
```

`ObservableCollection` sta in `System.Collections.ObjectModel` (già importato); `CollectionChanged` non richiede using aggiuntivi con la lambda discard.

- [ ] **Step 6: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 7: Commit**

```bash
git add FileExplorer/Models/CopyStateKind.cs FileExplorer/Converters/EnumEqualsConverter.cs FileExplorer/ViewModels/FolderFilePairViewModel.cs FileExplorer/ViewModels/CopyPairsViewModel.cs
git commit -m "feat(ui): add presentation state (StateKind, HasPairs) and enum converter"
```

---

### Task 5: MainWindow come shell

**Files:**
- Modify: `FileExplorer/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `Brush.Surface` (Task 2); `views:CopyPairsView` esistente.
- Produces: shell definitiva; l'header dell'app NON sta qui (sta in CopyPairsView, Task 6). `FileBrowserView` non è più referenzata da nessuna vista (resta nel progetto).

- [ ] **Step 1: Sostituisci `FileExplorer/Views/MainWindow.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:FileExplorer.Views"
        x:Class="FileExplorer.Views.MainWindow"
        Title="File Explorer"
        Width="900" Height="640"
        MinWidth="640" MinHeight="480"
        Background="{DynamicResource Brush.Surface}">

  <views:CopyPairsView />

</Window>
```

(Menu con voci morte e TabControl rimossi di proposito: vedi spec, sezioni 2 e "Tab Esplora".)

- [ ] **Step 2: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 3: Commit**

```bash
git add FileExplorer/Views/MainWindow.axaml
git commit -m "feat(ui): make MainWindow a plain shell, drop dead menu and tabs"
```

---

### Task 6: CopyPairsView — header gradiente + card + empty state

**Files:**
- Modify: `FileExplorer/Views/CopyPairsView.axaml`

**Interfaces:**
- Consumes: classi/risorse dei Task 2-3; `StateKind`, `HasPairs`, `EnumEqualsConverter` del Task 4; comandi esistenti (`AddPairCommand`, `BrowseSourceCommand`, `BrowseDestinationCommand`, `StartCopyCommand`, `CancelCopyCommand`) e proprietà riga esistenti (`SourcePath`, `DestinationPath`, `Progress`, `Status`, `CanStart`, `IsCopying`, `FilesToProcess` con `Name`/`Size`/`LastModified`/`IsDirectory`).
- Produces: vista definitiva della scheda copia.

- [ ] **Step 1: Sostituisci `FileExplorer/Views/CopyPairsView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:conv="clr-namespace:FileExplorer.Converters"
             x:Class="FileExplorer.Views.CopyPairsView">

  <UserControl.Resources>
    <conv:EnumEqualsConverter x:Key="EnumEquals" />
  </UserControl.Resources>

  <DockPanel>

    <!-- Header con gradiente -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
          <i:Icon Value="fa-solid fa-copy" FontSize="20" Foreground="White" />
          <TextBlock Text="File Explorer" FontSize="18" FontWeight="Bold" Foreground="White" VerticalAlignment="Center" />
        </StackPanel>
        <Button Grid.Column="1" Classes="onaccent" Command="{Binding AddPairCommand}">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plus" />
            <TextBlock Text="Aggiungi coppia" />
          </StackPanel>
        </Button>
      </Grid>
    </Border>

    <Panel Background="{DynamicResource Brush.Surface}">

      <!-- Empty state -->
      <StackPanel IsVisible="{Binding !HasPairs}" VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="12">
        <i:Icon Value="fa-regular fa-clone" FontSize="52" Foreground="{DynamicResource Brush.TextMuted}" HorizontalAlignment="Center" />
        <TextBlock Text="Nessuna coppia di copia"
                   FontSize="16"
                   Foreground="{DynamicResource Brush.TextMuted}"
                   HorizontalAlignment="Center" />
        <Button Classes="primary" Command="{Binding AddPairCommand}" HorizontalAlignment="Center">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plus" />
            <TextBlock Text="Aggiungi la prima coppia" />
          </StackPanel>
        </Button>
      </StackPanel>

      <!-- Lista card -->
      <ScrollViewer IsVisible="{Binding HasPairs}">
        <ItemsControl ItemsSource="{Binding PathPairs}" Margin="20,12">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Border Classes="card">
                <StackPanel Spacing="8">

                  <!-- Sorgente -->
                  <Grid ColumnDefinitions="Auto,*,Auto" >
                    <i:Icon Grid.Column="0" Value="fa-regular fa-folder-open" Width="26"
                            Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
                    <TextBox Grid.Column="1" Text="{Binding SourcePath}" IsReadOnly="True"
                             Watermark="Sorgente…" Margin="8,0" />
                    <Button Grid.Column="2" Classes="iconbtn"
                            i:Attached.Icon="fa-solid fa-magnifying-glass"
                            Command="{Binding DataContext.BrowseSourceCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                            CommandParameter="{Binding}" />
                  </Grid>

                  <!-- Destinazione -->
                  <Grid ColumnDefinitions="Auto,*,Auto">
                    <i:Icon Grid.Column="0" Value="fa-solid fa-folder" Width="26"
                            Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
                    <TextBox Grid.Column="1" Text="{Binding DestinationPath}" IsReadOnly="True"
                             Watermark="Destinazione…" Margin="8,0" />
                    <Button Grid.Column="2" Classes="iconbtn"
                            i:Attached.Icon="fa-solid fa-magnifying-glass"
                            Command="{Binding DataContext.BrowseDestinationCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                            CommandParameter="{Binding}" />
                  </Grid>

                  <!-- Stato + comandi -->
                  <Grid ColumnDefinitions="*,Auto" Margin="0,4,0,0">
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
                      <ProgressBar Width="200" Minimum="0" Maximum="1" Value="{Binding Progress}" VerticalAlignment="Center" />
                      <Border Classes="badge"
                              Classes.success="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Success}"
                              Classes.warning="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Warning}"
                              Classes.error="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Error}"
                              Classes.progress="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Copying}">
                        <TextBlock Text="{Binding Status}" />
                      </Border>
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                      <Button Classes="primary" Content="Avvia"
                              Command="{Binding DataContext.StartCopyCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                              CommandParameter="{Binding}"
                              IsEnabled="{Binding CanStart}" />
                      <Button Classes="secondary" Content="Annulla"
                              Command="{Binding DataContext.CancelCopyCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                              CommandParameter="{Binding}"
                              IsEnabled="{Binding IsCopying}" />
                    </StackPanel>
                  </Grid>

                  <!-- File da elaborare -->
                  <Expander Header="Mostra file da elaborare">
                    <DataGrid ItemsSource="{Binding FilesToProcess}"
                              AutoGenerateColumns="False"
                              IsReadOnly="True"
                              MaxHeight="220"
                              Margin="0,5,0,0">
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
                        <DataGridTextColumn Header="Dimensione" Binding="{Binding Size}" Width="110" />
                        <DataGridTextColumn Header="Ultima modifica" Binding="{Binding LastModified}" Width="170" />
                      </DataGrid.Columns>
                    </DataGrid>
                  </Expander>

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

- [ ] **Step 2: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 3: Commit**

```bash
git add FileExplorer/Views/CopyPairsView.axaml
git commit -m "feat(ui): card-based copy list with gradient header and empty state"
```

---

### Task 7: SelectPathDialog — restyle + footer

**Files:**
- Modify: `FileExplorer/Views/SelectPathDialog.axaml`
- Modify: `FileExplorer/Views/SelectPathDialog.axaml.cs`

**Interfaces:**
- Consumes: classi/risorse Task 2-3; icone Task 1; `SelectPathDialogViewModel` esistente (`CurrentPath`, `Items`, `SelectedItem`, `NavigateTo`); `FileSystemService.GetPathType/GetParentPath`.
- Produces: dialogo definitivo. Nuovo handler `OnCancelClick` → `Close(null)` (il chiamante già gestisce risultato null/vuoto). Interazioni esistenti invariate.

- [ ] **Step 1: Sostituisci `FileExplorer/Views/SelectPathDialog.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        x:Class="FileExplorer.Views.SelectPathDialog"
        Title="Seleziona file o cartella"
        Width="640" Height="440"
        Background="{DynamicResource Brush.Surface}">

  <Grid RowDefinitions="Auto,*,Auto" Margin="12">

    <!-- Barra del percorso -->
    <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Margin="0,0,0,10">
      <Button Grid.Column="0" Classes="iconbtn"
              i:Attached.Icon="fa-solid fa-arrow-left"
              Click="OnBackClick" Margin="0,0,8,0" />
      <TextBox Grid.Column="1"
               x:Name="PathTextBar"
               Text="{Binding CurrentPath, Mode=TwoWay}"
               KeyDown="OnPathKeyDown" />
      <Button Grid.Column="2" Classes="primary" Content="Vai" Click="OnGoClick" Margin="8,0,0,0" />
    </Grid>

    <!-- Lista file/cartelle -->
    <Border Grid.Row="1" Classes="card" Padding="4" Margin="0">
      <DataGrid ItemsSource="{Binding Items}"
                SelectedItem="{Binding SelectedItem}"
                DoubleTapped="OnItemDoubleTapped"
                AutoGenerateColumns="False"
                IsReadOnly="True"
                SelectionMode="Single"
                HeadersVisibility="Column"
                HorizontalScrollBarVisibility="Auto"
                VerticalScrollBarVisibility="Auto">
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
          <DataGridTextColumn Header="Dimensione" Binding="{Binding Size}" Width="110" />
          <DataGridTextColumn Header="Ultima modifica" Binding="{Binding LastModified}" Width="170" />
        </DataGrid.Columns>
      </DataGrid>
    </Border>

    <!-- Footer -->
    <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,10,0,0">
      <Button Classes="secondary" Content="Annulla" Click="OnCancelClick" />
      <Button Classes="primary" Content="Seleziona" Click="OnSelectClick" />
    </StackPanel>

  </Grid>
</Window>
```

- [ ] **Step 2: Aggiorna il code-behind**

In `FileExplorer/Views/SelectPathDialog.axaml.cs`:

1. Rimuovi `using Avalonia.Media;` (niente più `Brushes`).
2. Sostituisci il corpo di `OnGoClick` con la versione a classi:

```csharp
    public void OnGoClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Se il percorso non esiste la barra viene evidenziata con la classe "error".
        if (FileSystemService.GetPathType(vm.CurrentPath) == PathType.Unknown)
        {
            PathTextBar.Classes.Add("error");
            return;
        }

        PathTextBar.Classes.Remove("error");
        vm.NavigateTo(vm.SelectedItem?.FullPath ?? vm.CurrentPath);
        e.Handled = true;
    }
```

3. Aggiungi il handler del nuovo bottone Annulla (chiude senza risultato, come la X):

```csharp
    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
```

Tutto il resto (OnItemDoubleTapped, OnSelectClick, OnPathKeyDown, OnBackClick, CloseAfterSelectElement) resta invariato.

- [ ] **Step 3: Build**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 4: Commit**

```bash
git add FileExplorer/Views/SelectPathDialog.axaml FileExplorer/Views/SelectPathDialog.axaml.cs
git commit -m "feat(ui): restyle path dialog with icons, footer actions and error class"
```

---

### Task 8: CLAUDE.md + smoke run finale

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: tutto quanto sopra.
- Produces: documentazione aggiornata; verifica runtime completata.

- [ ] **Step 1: Aggiorna `CLAUDE.md`**

Nella sezione "Project", sostituisci la riga:

```
- `FileExplorer/` — core project (`Models/`, `Services/`, `ViewModels/`, `Views/`)
```

con:

```
- `FileExplorer/` — core project (`Models/`, `Services/`, `ViewModels/`, `Views/`, `Converters/`, `Styles/`)
```

E dopo il paragrafo "Layering: …" aggiungi:

```
Styling: `Styles/Palette.axaml` holds all theme-aware brushes (`Brush.*`, light/dark via ThemeDictionaries); `Styles/Controls.axaml` holds class-based styles (`Button.primary/.secondary/.iconbtn/.onaccent`, `Border.card`, `Border.badge.*`, `TextBox.error`). Never hardcode colors in views — always `{DynamicResource Brush.*}`. Icons via Projektanker.Icons.Avalonia (`fa-*` FontAwesome).
```

- [ ] **Step 2: Build finale**

Run: `dotnet build FileExplorer.sln`
Expected: `Avvisi: 0`, `Errori: 0`

- [ ] **Step 3: Smoke run**

Run: `timeout 12 dotnet run --project FileExplorer.Desktop; echo "exit: $?"`
Expected: la finestra si apre e resta aperta fino al timeout (exit 124 = ucciso dal timeout, OK). Nessuna eccezione XAML/binding in output. Se compare `KeyNotFoundException`/`XamlLoadException`, la risorsa o lo stile indicato nel messaggio va corretto prima di proseguire.

- [ ] **Step 4: Verifica manuale (utente al monitor)**

Con l'app aperta controllare: empty state con bottone primario → "Aggiungi coppia" crea card → sfoglia apre dialogo ristilizzato → percorso inesistente + Vai = bordo rosso → selezione sorgente/destinazione → Avvia mostra badge `progress` e barra gradiente → fine copia badge `success` (o `warning` se checksum non corrisponde) → Annulla durante copia = badge neutro "Annullato". Ripetere un giro rapido col tema di sistema opposto (chiaro/scuro).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document styling conventions in CLAUDE.md"
```
