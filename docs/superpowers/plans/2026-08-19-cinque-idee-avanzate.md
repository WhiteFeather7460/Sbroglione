# Cinque idee avanzate (IDEE 7, 11, 5, 8, 10) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementare cinque funzionalità del backlog IDEE.md: profili di copia salvabili (7), confronto binario byte-range di due file (11), delta-copy a blocchi (5), watch-folder con sincronizzazione automatica (8) e sync bidirezionale con rilevamento conflitti (10).

**Architecture:** Ogni fase è autonoma e segue il pattern consolidato del repo: servizio statico (`Services/`) + store JSON persistente in `%APPDATA%/FileExplorer/` con seam di test (`CurrentPath`/`Directory` settabile, salvataggio atomico tmp+move, load tollerante) + ViewModel ReactiveUI con metodi pubblici per i test + view AXAML con soli `{DynamicResource Brush.*}`. Nessun DI container. Le fasi 1–2 estendono tab esistenti (Copia, Confronto), la fase 3 estende il motore `FileCopyService`, la fase 4 aggiunge la tab "Sync auto", la fase 5 estende la tab Confronto con il merge a due vie basato su baseline persistita.

**Tech Stack:** .NET 8, Avalonia 11.2.8, ReactiveUI, System.Text.Json, FileSystemWatcher, xunit (net10.0, `parallelizeTestCollections: false`).

**Spec:** `IDEE.md` (punti 5, 7, 8, 10, 11 — testo di ciascun punto = requisito; le decisioni di scope sono dichiarate nell'intro di ogni fase).

## Global Constraints

- Mai commit su `main`: un branch per fase (`feature/copy-profiles`, `feature/file-byte-compare`, `feature/delta-copy`, `feature/watch-folders`, `feature/bidirectional-sync`), PR a fine fase con `gh pr create`.
- Ordine fasi obbligato: 1 → 2 → 3 → 4 → 5. La Fase 3 tocca `CopyPairsViewModel`/`CopyPairsView` (richiede Fase 1 mergiata); la Fase 5 tocca `ComparisonViewModel`/`ComparisonView` (richiede Fase 2 mergiata). Ogni branch parte da `main` aggiornato dopo il merge della fase precedente.
- Niente co-author Claude nei commit. Messaggi Conventional Commits in italiano.
- Test: `dotnet test` dalla root. Build: `dotnet build FileExplorer.sln`. Run app (sandbox senza runtime 8): `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project FileExplorer.Desktop`.
- Servizi statici (pattern `AppSettingsStore`/`CopyJournalStore`/`ThemeStore`), niente DI container.
- Nessun colore hardcodato nelle view: sempre `{DynamicResource Brush.*}`; classi da `Styles/Controls.axaml`.
- Stringhe UI in italiano, come il resto dell'app.
- Ogni task dichiara il modello del subagente esecutore (`Model:`).
- Test: classi `sealed : IDisposable`, temp dir `fe-<slug>-<guid>`, salva/ripristina ogni statico mutato in ctor/Dispose, nomi `Metodo_Scenario_Risultato`.
- Al termine di ogni task: spuntare i checkbox del task in questo file prima di passare al successivo.

---
## Fase 1 — Profili di copia salvabili (IDEE punto 7) — branch `feature/copy-profiles`

Preset nominati che memorizzano l'intera lista di coppie sorgente/destinazione (incluse destinazioni extra e flag `SkipUnchanged`), riapplicabili con un click dalla scheda Copia. Persistenza JSON in AppData con il pattern esatto di `CopyJournalStore`; il nome è chiesto con un nuovo `InputDialog` riusabile costruito sul pattern `ConfirmDialog`/`ConfirmDialogHelper` (con seam `Override` per i test headless).

Nota rischi: `FolderFilePairViewModel.SourcePath` avvia in setter un refresh asincrono dello stato sorgente (`SourceStateRefresh`); l'applicazione di un profilo con percorsi inesistenti è legittima (la riga risulterà `CanStart == false` finché la sorgente non esiste). Nessuna opzione globale (throttle, parallelismo) è salvata nel profilo: restano in `AppSettings`.

---

### Task 1: Modello `CopyProfile` + `CopyProfileStore`

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Models/CopyProfile.cs`
- Create: `FileExplorer/Services/CopyProfileStore.cs`
- Test: `FileExplorer.Tests/CopyProfileStoreTests.cs`

**Interfaces:**
- Consumes: nulla (foglia).
- Produces:
  - `public class CopyProfile { string Id; string Name; List<CopyProfilePair> Pairs; }`
  - `public class CopyProfilePair { string SourcePath; string DestinationPath; List<string> ExtraDestinations; bool SkipUnchanged; }`
  - `public static Task<List<CopyProfile>> CopyProfileStore.LoadAsync()`
  - `public static Task CopyProfileStore.SaveAsync(IReadOnlyList<CopyProfile> profiles)`
  - `public static void CopyProfileStore.Sanitize(CopyProfile profile)`
  - `public static string CopyProfileStore.CurrentPath { get; set; }` (seam test)

- [x] **Step 0: Crea il branch di lavoro da main aggiornato**

```bash
git checkout main && git pull && git checkout -b feature/copy-profiles
```

- [x] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/CopyProfileStoreTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CopyProfileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalPath;

    public CopyProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copyprofiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalPath = CopyProfileStore.CurrentPath;
        CopyProfileStore.CurrentPath = Path.Combine(_root, "copy-profiles.json");
    }

    public void Dispose()
    {
        CopyProfileStore.CurrentPath = _originalPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(CopyProfileStore.CurrentPath, "{ non-json");
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProfiles()
    {
        var profile = new CopyProfile
        {
            Name = "Backup foto",
            Pairs =
            {
                new CopyProfilePair
                {
                    SourcePath = "/dati/foto",
                    DestinationPath = "/backup/foto",
                    ExtraDestinations = { "/nas/foto" },
                    SkipUnchanged = true
                }
            }
        };

        await CopyProfileStore.SaveAsync(new[] { profile });
        List<CopyProfile> loaded = await CopyProfileStore.LoadAsync();

        var restored = Assert.Single(loaded);
        Assert.Equal(profile.Id, restored.Id);
        Assert.Equal("Backup foto", restored.Name);
        var pair = Assert.Single(restored.Pairs);
        Assert.Equal("/dati/foto", pair.SourcePath);
        Assert.Equal("/backup/foto", pair.DestinationPath);
        Assert.Equal("/nas/foto", Assert.Single(pair.ExtraDestinations));
        Assert.True(pair.SkipUnchanged);
    }

    [Fact]
    public async Task LoadAsync_SortsByNameCaseInsensitive()
    {
        await CopyProfileStore.SaveAsync(new[]
        {
            new CopyProfile { Name = "zeta" },
            new CopyProfile { Name = "Alfa" },
            new CopyProfile { Name = "beta" }
        });

        List<CopyProfile> loaded = await CopyProfileStore.LoadAsync();

        Assert.Equal(new[] { "Alfa", "beta", "zeta" }, loaded.Select(p => p.Name));
    }

    [Fact]
    public void Sanitize_EmptyName_AssignsDefaultName()
    {
        var profile = new CopyProfile { Name = "   " };

        CopyProfileStore.Sanitize(profile);

        Assert.Equal("Profilo senza nome", profile.Name);
    }

    [Fact]
    public void Sanitize_PairWithoutPaths_IsRemoved()
    {
        var profile = new CopyProfile
        {
            Name = "Test",
            Pairs =
            {
                new CopyProfilePair(),
                new CopyProfilePair { SourcePath = "/src", DestinationPath = "/dst" }
            }
        };

        CopyProfileStore.Sanitize(profile);

        var pair = Assert.Single(profile.Pairs);
        Assert.Equal("/src", pair.SourcePath);
    }
}
```

- [x] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter CopyProfileStoreTests`
Expected: FAIL (tipi `CopyProfile`/`CopyProfileStore` non esistenti → errore di compilazione del progetto test).

- [x] **Step 3: Implement il modello e lo store**

```csharp
// FileExplorer/Models/CopyProfile.cs
using System;
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Preset nominato di coppie di copia, rieseguibile con un click dalla scheda Copia.</summary>
public class CopyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<CopyProfilePair> Pairs { get; set; } = new();
}

/// <summary>Singola coppia sorgente/destinazione memorizzata in un profilo.</summary>
public class CopyProfilePair
{
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public List<string> ExtraDestinations { get; set; } = new();
    public bool SkipUnchanged { get; set; }
}
```

```csharp
// FileExplorer/Services/CopyProfileStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Archivio dei profili di copia (JSON in AppData, pattern <see cref="CopyJournalStore"/>):
/// scrittura atomica e accessi serializzati.
/// </summary>
public static class CopyProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>Percorso predefinito del file profili.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "copy-profiles.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>
    /// Carica i profili ordinati per nome (case-insensitive); lista vuota se il file
    /// manca o è illeggibile.
    /// </summary>
    public static async Task<List<CopyProfile>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<CopyProfile>();

            await using var stream = File.OpenRead(CurrentPath);
            List<CopyProfile> profiles =
                await JsonSerializer.DeserializeAsync<List<CopyProfile>>(stream, Options).ConfigureAwait(false)
                ?? new List<CopyProfile>();

            foreach (var profile in profiles)
                Sanitize(profile);

            return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception)
        {
            return new List<CopyProfile>();
        }
    }

    /// <summary>Salva l'intera lista di profili (scrittura atomica tmp + move).</summary>
    public static async Task SaveAsync(IReadOnlyList<CopyProfile> profiles)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = CurrentPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, Options).ConfigureAwait(false);
            }

            File.Move(tempPath, CurrentPath, overwrite: true);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Normalizza un profilo in-place: nome vuoto → "Profilo senza nome"; le coppie con
    /// sorgente e destinazione entrambe vuote vengono scartate.
    /// </summary>
    public static void Sanitize(CopyProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = "Profilo senza nome";

        profile.Pairs.RemoveAll(pair =>
            string.IsNullOrWhiteSpace(pair.SourcePath) && string.IsNullOrWhiteSpace(pair.DestinationPath));
    }
}
```

- [x] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter CopyProfileStoreTests`
Expected: PASS (6 test).

- [x] **Step 5: Commit**

```bash
git add FileExplorer/Models/CopyProfile.cs FileExplorer/Services/CopyProfileStore.cs FileExplorer.Tests/CopyProfileStoreTests.cs
git commit -m "feat(profiles): modello CopyProfile e CopyProfileStore persistente"
```

---

### Task 2: `InputDialog` riusabile (input testo modale)

**Model:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/InputDialogViewModel.cs`
- Create: `FileExplorer/Views/InputDialog.axaml`
- Create: `FileExplorer/Views/InputDialog.axaml.cs`
- Create: `FileExplorer/ViewModels/InputDialogHelper.cs`
- Test: `FileExplorer.Tests/InputDialogViewModelTests.cs`

**Interfaces:**
- Consumes: nulla (foglia; pattern copiato da `ConfirmDialog`/`ConfirmDialogHelper`).
- Produces:
  - `public InputDialogViewModel(string title, string message, string? initialText = null)` con `string Title`, `string Message`, `string Text` (reactive), `bool CanConfirm`
  - `public static Task<string?> InputDialogHelper.ShowAsync(string title, string message, string? initialText)` — testo confermato o `null` su annulla
  - `internal static Func<string, string, string?, Task<string?>>? InputDialogHelper.Override { get; set; }` (seam test)

- [x] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/InputDialogViewModelTests.cs
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class InputDialogViewModelTests
{
    [Fact]
    public void Constructor_SetsTitleMessageAndInitialText()
    {
        var vm = new InputDialogViewModel("Titolo", "Messaggio", "iniziale");

        Assert.Equal("Titolo", vm.Title);
        Assert.Equal("Messaggio", vm.Message);
        Assert.Equal("iniziale", vm.Text);
    }

    [Fact]
    public void Constructor_NullInitialText_TextIsEmpty()
    {
        var vm = new InputDialogViewModel("T", "M", null);

        Assert.Equal(string.Empty, vm.Text);
    }

    [Fact]
    public void CanConfirm_EmptyOrWhitespaceText_IsFalse()
    {
        var vm = new InputDialogViewModel("T", "M");
        Assert.False(vm.CanConfirm);

        vm.Text = "   ";
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void Text_Changed_RaisesTextAndCanConfirm()
    {
        var vm = new InputDialogViewModel("T", "M");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Text = "Backup foto";

        Assert.True(vm.CanConfirm);
        Assert.Contains(nameof(InputDialogViewModel.Text), raised);
        Assert.Contains(nameof(InputDialogViewModel.CanConfirm), raised);
    }
}
```

- [x] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter InputDialogViewModelTests`
Expected: FAIL (tipo `InputDialogViewModel` non esistente → errore di compilazione del progetto test).

- [x] **Step 3: Implement ViewModel, dialog e helper**

```csharp
// FileExplorer/ViewModels/InputDialogViewModel.cs
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Contenuto di un dialog di input testo: titolo, messaggio e testo modificabile.</summary>
public class InputDialogViewModel : ReactiveObject
{
    private string _text;

    public InputDialogViewModel(string title, string message, string? initialText = null)
    {
        Title = title;
        Message = message;
        _text = initialText ?? string.Empty;
    }

    public string Title { get; }
    public string Message { get; }

    /// <summary>Testo digitato dall'utente.</summary>
    public string Text
    {
        get => _text;
        set
        {
            this.RaiseAndSetIfChanged(ref _text, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    /// <summary>True se il testo non è vuoto (abilita OK e la conferma con Invio).</summary>
    public bool CanConfirm => !string.IsNullOrWhiteSpace(Text);
}
```

```xml
<!-- FileExplorer/Views/InputDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia"
        x:Class="FileExplorer.Views.InputDialog"
        Title="{Binding Title}"
        Width="440" SizeToContent="Height"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface}">

  <StackPanel Margin="20" Spacing="16">

    <StackPanel Orientation="Horizontal" Spacing="12">
      <i:Icon Value="fa-solid fa-pen" FontSize="24"
              Foreground="{DynamicResource Brush.Accent}" VerticalAlignment="Top" />
      <TextBlock Text="{Binding Message}" TextWrapping="Wrap" MaxWidth="360"
                 Foreground="{DynamicResource Brush.TextPrimary}" VerticalAlignment="Center" />
    </StackPanel>

    <TextBox x:Name="InputBox" Text="{Binding Text}" Watermark="Nome…" KeyDown="OnTextKeyDown" />

    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button Classes="secondary" Content="Annulla" Click="OnCancelClick" />
      <Button Classes="primary" Content="OK" Click="OnConfirmClick" IsEnabled="{Binding CanConfirm}" />
    </StackPanel>

  </StackPanel>
</Window>
```

```csharp
// FileExplorer/Views/InputDialog.axaml.cs
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>Dialog modale di input testo: restituisce il testo (trimmato) su OK, null su annulla/chiusura.</summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Opened += (_, _) => InputBox.Focus();
    }

    public void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CloseWithText();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => CloseWithText();

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void CloseWithText()
    {
        if (DataContext is InputDialogViewModel vm && vm.CanConfirm)
            Close(vm.Text.Trim());
    }
}
```

```csharp
// FileExplorer/ViewModels/InputDialogHelper.cs
using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using FileExplorer.Views;

namespace FileExplorer.ViewModels;

/// <summary>
/// Apertura del dialog di input testo, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la risposta dell'utente.
/// </summary>
internal static class InputDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<string, string, string?, Task<string?>>? Override { get; set; }

    public static async Task<string?> ShowAsync(string title, string message, string? initialText)
    {
        if (Override is not null)
            return await Override(title, message, initialText);

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null; // senza finestra non c'è input: nessuna azione.

        var dialog = new InputDialog
        {
            DataContext = new InputDialogViewModel(title, message, initialText)
        };

        return await dialog.ShowDialog<string?>(owner);
    }
}
```

- [x] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter InputDialogViewModelTests`
Expected: PASS (4 test).

- [x] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/InputDialogViewModel.cs FileExplorer/Views/InputDialog.axaml FileExplorer/Views/InputDialog.axaml.cs FileExplorer/ViewModels/InputDialogHelper.cs FileExplorer.Tests/InputDialogViewModelTests.cs
git commit -m "feat(profiles): dialog di input riusabile con seam Override per i test"
```

---

### Task 3: Comandi profili in `CopyPairsViewModel` + barra profili in `CopyPairsView`

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs`
- Modify: `FileExplorer/Views/CopyPairsView.axaml`
- Modify: `IDEE.md` (punto 7 → `[x]`)
- Test: `FileExplorer.Tests/CopyPairsViewModelTests.cs` (estensione)

**Interfaces:**
- Consumes:
  - `CopyProfile`, `CopyProfilePair`, `CopyProfileStore.LoadAsync/SaveAsync/Sanitize` (Task 1)
  - `InputDialogHelper.ShowAsync(string, string, string?)` e `InputDialogHelper.Override` (Task 2)
  - `ConfirmDialogHelper.ShowAsync(string, string, string)` (esistente)
- Produces:
  - `public ObservableCollection<CopyProfile> Profiles { get; }`
  - `public CopyProfile? SelectedProfile { get; set; }`
  - `public Task ProfilesLoad { get; }` (avviato dal costruttore, atteso dai test)
  - `public ReactiveCommand<Unit, Unit> SaveProfileCommand/ApplyProfileCommand/DeleteProfileCommand { get; }`
  - `public Task SaveProfileAsync()`, `public void ApplyProfile()`, `public Task DeleteProfileAsync()`
  - `internal Task? LastProfilesSaveTask { get; private set; }` (seam test)

- [x] **Step 1: Write the failing tests**

In `FileExplorer.Tests/CopyPairsViewModelTests.cs` aggiornare ctor/Dispose (righe 14–32) così:

```csharp
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;
    private readonly string _originalJournalPath;
    private readonly string _originalProfilesPath;

    public CopyPairsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copypairs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
        AppSettingsStore.Current = new AppSettings();
        _originalJournalPath = CopyJournalStore.CurrentPath;
        CopyJournalStore.CurrentPath = Path.Combine(_root, "copy-journal.json");
        _originalProfilesPath = CopyProfileStore.CurrentPath;
        CopyProfileStore.CurrentPath = Path.Combine(_root, "copy-profiles.json");
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        CopyJournalStore.CurrentPath = _originalJournalPath;
        CopyProfileStore.CurrentPath = _originalProfilesPath;
        InputDialogHelper.Override = null;
        ConfirmDialogHelper.Override = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
```

Poi aggiungere in fondo alla classe (prima della graffa di chiusura) i nuovi test:

```csharp
    [Fact]
    public async Task Constructor_LoadsPersistedProfilesSortedByName()
    {
        await CopyProfileStore.SaveAsync(new[]
        {
            new CopyProfile { Name = "beta" },
            new CopyProfile { Name = "Alfa" }
        });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        Assert.Equal(new[] { "Alfa", "beta" }, vm.Profiles.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveProfile_CreatesProfileAndPersistsIt()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Backup foto");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var pair = new FolderFilePairViewModel
        {
            SourcePath = Path.Combine(_root, "src"),
            DestinationPath = Path.Combine(_root, "dst"),
            SkipUnchanged = true
        };
        pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, Path.Combine(_root, "extra")));
        vm.PathPairs.Add(pair);

        await vm.SaveProfileAsync();

        var profile = Assert.Single(vm.Profiles);
        Assert.Equal("Backup foto", profile.Name);
        Assert.Same(profile, vm.SelectedProfile);
        var stored = Assert.Single(profile.Pairs);
        Assert.Equal(pair.SourcePath, stored.SourcePath);
        Assert.Equal(pair.DestinationPath, stored.DestinationPath);
        Assert.Equal(Path.Combine(_root, "extra"), Assert.Single(stored.ExtraDestinations));
        Assert.True(stored.SkipUnchanged);

        List<CopyProfile> persisted = await CopyProfileStore.LoadAsync();
        Assert.Equal("Backup foto", Assert.Single(persisted).Name);
    }

    [Fact]
    public async Task SaveProfile_SameName_OverwritesExistingProfile()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>("Sync progetti");

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/a", DestinationPath = "/b" });
        await vm.SaveProfileAsync();

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/c", DestinationPath = "/d" });
        await vm.SaveProfileAsync();

        var profile = Assert.Single(vm.Profiles);
        Assert.Equal("Sync progetti", profile.Name);
        Assert.Equal(2, profile.Pairs.Count);
    }

    [Fact]
    public async Task SaveProfile_CancelledDialog_DoesNothing()
    {
        InputDialogHelper.Override = (_, _, _) => Task.FromResult<string?>(null);

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/a", DestinationPath = "/b" });

        await vm.SaveProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task ApplyProfile_ReplacesPathPairs()
    {
        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        vm.PathPairs.Add(new FolderFilePairViewModel { SourcePath = "/vecchia", DestinationPath = "/coppia" });

        var profile = new CopyProfile
        {
            Name = "Preset",
            Pairs =
            {
                new CopyProfilePair
                {
                    SourcePath = "/src1",
                    DestinationPath = "/dst1",
                    ExtraDestinations = { "/extra1" },
                    SkipUnchanged = true
                },
                new CopyProfilePair { SourcePath = "/src2", DestinationPath = "/dst2" }
            }
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ApplyProfile();

        Assert.Equal(2, vm.PathPairs.Count);
        Assert.Equal("/src1", vm.PathPairs[0].SourcePath);
        Assert.Equal("/dst1", vm.PathPairs[0].DestinationPath);
        Assert.True(vm.PathPairs[0].SkipUnchanged);
        Assert.Equal("/extra1", Assert.Single(vm.PathPairs[0].ExtraDestinations).Path);
        Assert.Equal("/src2", vm.PathPairs[1].SourcePath);
    }

    [Fact]
    public async Task ApplyProfile_PairIsCopying_DoesNotReplacePairs()
    {
        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;

        var copying = new FolderFilePairViewModel
        {
            SourcePath = "/vecchia",
            DestinationPath = "/coppia",
            IsCopying = true
        };
        vm.PathPairs.Add(copying);

        var profile = new CopyProfile
        {
            Name = "Preset",
            Pairs = { new CopyProfilePair { SourcePath = "/s", DestinationPath = "/d" } }
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ApplyProfile();

        Assert.Same(copying, Assert.Single(vm.PathPairs));
    }

    [Fact]
    public async Task DeleteProfile_Confirmed_RemovesAndPersists()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(true);

        await CopyProfileStore.SaveAsync(new[] { new CopyProfile { Name = "Da eliminare" } });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.SelectedProfile = Assert.Single(vm.Profiles);

        await vm.DeleteProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task DeleteProfile_NotConfirmed_KeepsProfile()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);

        await CopyProfileStore.SaveAsync(new[] { new CopyProfile { Name = "Da tenere" } });

        var vm = new CopyPairsViewModel();
        await vm.ProfilesLoad;
        vm.SelectedProfile = Assert.Single(vm.Profiles);

        await vm.DeleteProfileAsync();

        Assert.Single(vm.Profiles);
        Assert.NotNull(vm.SelectedProfile);
    }
```

- [x] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter CopyPairsViewModelTests`
Expected: FAIL (membri `Profiles`/`ProfilesLoad`/`SaveProfileAsync`/… non esistenti → errore di compilazione del progetto test).

- [x] **Step 3: Implement i membri profili in `CopyPairsViewModel`**

Le `using` esistenti bastano (Linq, ObjectModel, Reactive, Models, Services già importati).

3a. Dopo la riga `public ReactiveCommand<FolderFilePairViewModel, Unit> SimulateCommand { get; }` (riga 37) aggiungere:

```csharp
    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }

    /// <summary>Profili di copia salvati, ordinati per nome.</summary>
    public ObservableCollection<CopyProfile> Profiles { get; } = new();

    private CopyProfile? _selectedProfile;

    /// <summary>Profilo selezionato nella barra profili (null se nessuno).</summary>
    public CopyProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }
```

3b. Nel costruttore, dopo `SimulateCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(SimulatePairAsync);` aggiungere:

```csharp
        SaveProfileCommand = ReactiveCommand.CreateFromTask(SaveProfileAsync);
        ApplyProfileCommand = ReactiveCommand.Create(ApplyProfile);
        DeleteProfileCommand = ReactiveCommand.CreateFromTask(DeleteProfileAsync);
```

e, subito dopo la riga `JournalRestore = RestoreInterruptedJobsAsync();`:

```csharp
        ProfilesLoad = LoadProfilesAsync();
```

3c. Dopo la proprietà `public Task JournalRestore { get; }` aggiungere:

```csharp
    /// <summary>
    /// Task del caricamento profili, avviato dal costruttore.
    /// I test lo attendono; la UI non ne ha bisogno.
    /// </summary>
    public Task ProfilesLoad { get; }

    /// <summary>Task dell'ultimo salvataggio profili. Solo per i test.</summary>
    internal Task? LastProfilesSaveTask { get; private set; }
```

3d. Dopo il metodo `RestoreInterruptedJobsAsync` aggiungere i metodi profili:

```csharp
    private async Task LoadProfilesAsync()
    {
        List<CopyProfile> profiles = await CopyProfileStore.LoadAsync();
        foreach (var profile in profiles)
            Profiles.Add(profile);
    }

    /// <summary>
    /// Salva le coppie correnti come profilo: chiede il nome; se coincide (case-insensitive)
    /// con un profilo esistente lo sovrascrive, altrimenti lo inserisce mantenendo l'ordine.
    /// </summary>
    public async Task SaveProfileAsync()
    {
        string? name = await InputDialogHelper.ShowAsync(
            "Salva profilo", "Nome del profilo di copia:", SelectedProfile?.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();

        List<CopyProfilePair> pairs = PathPairs
            .Where(p => !string.IsNullOrWhiteSpace(p.SourcePath) || !string.IsNullOrWhiteSpace(p.DestinationPath))
            .Select(p => new CopyProfilePair
            {
                SourcePath = p.SourcePath ?? string.Empty,
                DestinationPath = p.DestinationPath ?? string.Empty,
                ExtraDestinations = p.ExtraDestinations.Select(e => e.Path).ToList(),
                SkipUnchanged = p.SkipUnchanged
            })
            .ToList();

        CopyProfile? existing = Profiles.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Pairs = pairs;
            SelectedProfile = existing;
        }
        else
        {
            var profile = new CopyProfile { Name = name, Pairs = pairs };
            int index = 0;
            while (index < Profiles.Count &&
                   StringComparer.OrdinalIgnoreCase.Compare(Profiles[index].Name, profile.Name) < 0)
                index++;
            Profiles.Insert(index, profile);
            SelectedProfile = profile;
        }

        LastProfilesSaveTask = SaveProfilesBestEffortAsync();
        await LastProfilesSaveTask;
    }

    /// <summary>
    /// Sostituisce le coppie correnti con quelle del profilo selezionato.
    /// No-op se nessun profilo è selezionato o se una copia è in corso.
    /// </summary>
    public void ApplyProfile()
    {
        if (SelectedProfile is not { } profile)
            return;

        if (PathPairs.Any(p => p.IsCopying))
            return; // nessuna sostituzione mentre una copia è in corso.

        PathPairs.Clear();
        foreach (var stored in profile.Pairs)
        {
            var pair = new FolderFilePairViewModel
            {
                SourcePath = stored.SourcePath,
                DestinationPath = stored.DestinationPath,
                SkipUnchanged = stored.SkipUnchanged
            };
            foreach (var extra in stored.ExtraDestinations)
                pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));
            PathPairs.Add(pair);
        }
    }

    /// <summary>Elimina il profilo selezionato previa conferma.</summary>
    public async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Elimina profilo",
            $"Eliminare il profilo \"{profile.Name}\"?",
            "Elimina");
        if (!confirmed)
            return;

        Profiles.Remove(profile);
        SelectedProfile = null;

        LastProfilesSaveTask = SaveProfilesBestEffortAsync();
        await LastProfilesSaveTask;
    }

    private async Task SaveProfilesBestEffortAsync()
    {
        try
        {
            await CopyProfileStore.SaveAsync(Profiles.ToList());
        }
        catch (Exception)
        {
            // best effort: i profili restano in memoria anche se il salvataggio fallisce.
        }
    }
```

- [x] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter CopyPairsViewModelTests`
Expected: PASS (16 test esistenti + 8 nuovi = 24 test).

- [x] **Step 5: Aggiungi la barra profili in `CopyPairsView.axaml`**

Inserire il blocco seguente subito dopo la chiusura del Border header (dopo la riga 35, `</Border>` del blocco `<!-- Header con gradiente -->`) e prima di `<Panel Background="{DynamicResource Brush.Surface}">`:

```xml
    <!-- Barra profili di copia -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.Card}"
            BorderBrush="{DynamicResource Brush.CardBorder}" BorderThickness="0,0,0,1" Padding="20,8">
      <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <i:Icon Value="fa-solid fa-bookmark" Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
        <TextBlock Text="Profilo:" VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
        <ComboBox Width="220" ItemsSource="{Binding Profiles}" SelectedItem="{Binding SelectedProfile}"
                  PlaceholderText="Nessun profilo">
          <ComboBox.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding Name}" />
            </DataTemplate>
          </ComboBox.ItemTemplate>
        </ComboBox>
        <Button Classes="iconbtn" i:Attached.Icon="fa-solid fa-floppy-disk"
                Command="{Binding SaveProfileCommand}"
                ToolTip.Tip="Salva le coppie correnti come profilo" />
        <Button Classes="iconbtn" i:Attached.Icon="fa-solid fa-folder-open"
                Command="{Binding ApplyProfileCommand}"
                ToolTip.Tip="Applica il profilo selezionato" />
        <Button Classes="iconbtn" i:Attached.Icon="fa-solid fa-trash"
                Command="{Binding DeleteProfileCommand}"
                ToolTip.Tip="Elimina il profilo selezionato" />
      </StackPanel>
    </Border>
```

Nota icone: `fa-floppy-disk`, `fa-bookmark`, `fa-folder-open`, `fa-trash` sono tutte nel set FontAwesome 6 Free Solid già usato dall'app.

- [x] **Step 6: Build e smoke-run**

Run: `dotnet build FileExplorer.sln`
Expected: build OK, nessun warning nuovo.

Se `DISPLAY` è impostato: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project FileExplorer.Desktop` — verificare che la barra profili compaia sotto l'header della scheda Copia e che salvataggio/applicazione/eliminazione funzionino.

- [x] **Step 7: Aggiorna IDEE.md e commit**

In `IDEE.md` cambiare la riga del punto 7 da:

```markdown
7. `[ ]` **Profili di copia salvabili** — preset nominati (es. "Backup foto", "Sync progetti") che memorizzano coppie sorgente/destinazione, filtri, opzioni di verifica e parallelismo. Un click per rieseguire. *(M)*
```

a:

```markdown
7. `[x]` **Profili di copia salvabili** — preset nominati (es. "Backup foto", "Sync progetti") che memorizzano coppie sorgente/destinazione, filtri, opzioni di verifica e parallelismo. Un click per rieseguire. *(M)*
```

```bash
git add FileExplorer/ViewModels/CopyPairsViewModel.cs FileExplorer/Views/CopyPairsView.axaml FileExplorer.Tests/CopyPairsViewModelTests.cs IDEE.md
git commit -m "feat(profiles): salvataggio, applicazione ed eliminazione profili di copia"
```

- [x] **Step 8: Fine fase — test completi, push e PR**

Run: `dotnet test`
Expected: PASS (tutti i test della soluzione).

```bash
git push -u origin feature/copy-profiles
gh pr create --title "Profili di copia salvabili (IDEE punto 7)" --body "Preset nominati di coppie di copia con persistenza JSON in AppData, barra profili nella scheda Copia (salva/applica/elimina), nuovo InputDialog riusabile. Chiude il punto 7 di IDEE.md."
```

---
## Fase 2 — Confronto byte-range di due file (IDEE punto 11) — branch `feature/file-byte-compare`

Confronto binario in streaming di due file: primo offset diverso, percentuale identica, elenco degli intervalli di byte differenti (uniti se contigui, troncati oltre `maxRanges`). Nuovo servizio statico `FileByteCompareService` + card "Confronto binario di due file" nella scheda Confronto, sotto quella esistente. Nessuna dipendenza dalle altre fasi.

---

### Task 4: `FileByteCompareService`

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Services/FileByteCompareService.cs`
- Test: `FileExplorer.Tests/FileByteCompareServiceTests.cs`

**Interfaces:**
- Consumes: `CompareProgress(int Processed, int Total)` (già definito in `FileExplorer/Services/DirectoryComparisonService.cs:12`).
- Produces:
  - `public sealed record ByteRangeDiff(long Offset, long Length);`
  - `public sealed record FileCompareResult(long LeftLength, long RightLength, long? FirstDifferenceOffset, long IdenticalBytes, IReadOnlyList<ByteRangeDiff> DifferentRanges, bool RangesTruncated)` con `double IdenticalFraction` e `bool AreIdentical`
  - `public static Task<FileCompareResult> CompareAsync(string leftPath, string rightPath, Action<CompareProgress>? onProgress, CancellationToken ct, int bufferSize = 1024 * 1024, int maxRanges = 256)`

Semantica (vincolante, i test la codificano):
- Progresso in **blocchi**: `Total = ceil(max(LeftLength, RightLength) / bufferSize)`, prima invocazione `(0, Total)`.
- La coda oltre la lunghezza del file più corto è **un unico intervallo differente** (eventualmente fuso con un intervallo aperto che termina a `minLength`); `FirstDifferenceOffset` in quel caso è `minLength` se il prefisso comune è identico.
- `IdenticalBytes` conta solo i byte uguali nella zona sovrapposta; `IdenticalFraction` rapporta al file più lungo (1.0 per due file vuoti).
- Oltre `maxRanges` intervalli: `RangesTruncated = true`, gli intervalli successivi non vengono accumulati ma `IdenticalBytes` e `FirstDifferenceOffset` restano esatti.
- Cancellazione: `ct.ThrowIfCancellationRequested()` a ogni blocco, `ct` passato alle letture.

- [ ] **Step 1: Branch da main aggiornato**

```bash
git checkout main && git pull && git checkout -b feature/file-byte-compare
```

- [ ] **Step 2: Write the failing tests**

```csharp
// FileExplorer.Tests/FileByteCompareServiceTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class FileByteCompareServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fe-bytecmp-" + Guid.NewGuid().ToString("N"));

    public FileByteCompareServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<string> WriteFileAsync(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    [Fact]
    public async Task CompareAsync_IdenticalFiles_AreIdentical()
    {
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };
        string left = await WriteFileAsync("l.bin", data);
        string right = await WriteFileAsync("r.bin", data);

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.True(result.AreIdentical);
        Assert.Null(result.FirstDifferenceOffset);
        Assert.Equal(8, result.IdenticalBytes);
        Assert.Empty(result.DifferentRanges);
        Assert.False(result.RangesTruncated);
        Assert.Equal(1.0, result.IdenticalFraction);
    }

    [Fact]
    public async Task CompareAsync_SingleByteDifference_ReportsOffsetAndSingleRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 3, 4, 99, 6, 7, 8, 9 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.False(result.AreIdentical);
        Assert.Equal(5, result.FirstDifferenceOffset);
        Assert.Equal(9, result.IdenticalBytes);
        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(5, 1), range);
    }

    [Fact]
    public async Task CompareAsync_ContiguousDifferences_MergedIntoOneRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 90, 91, 92, 93, 7 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(3, 4), range);
        Assert.Equal(4, result.IdenticalBytes);
    }

    [Fact]
    public async Task CompareAsync_DifferenceAcrossBlockBoundary_MergedIntoOneRange()
    {
        // bufferSize = 4: la differenza (offset 2..5) attraversa il confine tra blocco 0 e blocco 1.
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 82, 83, 84, 85, 6, 7 });

        var result = await FileByteCompareService.CompareAsync(
            left, right, null, CancellationToken.None, bufferSize: 4);

        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(2, 4), range);
        Assert.Equal(2, result.FirstDifferenceOffset);
    }

    [Fact]
    public async Task CompareAsync_DifferentLengths_TailIsSingleRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 3, 4, 5 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.False(result.AreIdentical);
        Assert.Equal(6, result.FirstDifferenceOffset);
        Assert.Equal(6, result.IdenticalBytes);
        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(6, 4), range);
        Assert.Equal(0.6, result.IdenticalFraction, precision: 10);
    }

    [Fact]
    public async Task CompareAsync_EmptyFiles_AreIdenticalWithFractionOne()
    {
        string left = await WriteFileAsync("l.bin", Array.Empty<byte>());
        string right = await WriteFileAsync("r.bin", Array.Empty<byte>());

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.True(result.AreIdentical);
        Assert.Equal(1.0, result.IdenticalFraction);
        Assert.Empty(result.DifferentRanges);
    }

    [Fact]
    public async Task CompareAsync_MaxRangesExceeded_SetsTruncatedButKeepsCounts()
    {
        // Differenze alternate agli offset 0, 2, 4, 6 → 4 intervalli, maxRanges = 2.
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 0, 1, 0, 1, 0, 1 });
        string right = await WriteFileAsync("r.bin", new byte[] { 9, 1, 9, 1, 9, 1, 9, 1 });

        var result = await FileByteCompareService.CompareAsync(
            left, right, null, CancellationToken.None, maxRanges: 2);

        Assert.True(result.RangesTruncated);
        Assert.Equal(2, result.DifferentRanges.Count);
        Assert.Equal(new[] { new ByteRangeDiff(0, 1), new ByteRangeDiff(2, 1) }, result.DifferentRanges.ToArray());
        Assert.Equal(0, result.FirstDifferenceOffset);
        Assert.Equal(4, result.IdenticalBytes);
    }

    [Fact]
    public async Task CompareAsync_ReportsBlockProgress()
    {
        string left = await WriteFileAsync("l.bin", new byte[10]);
        string right = await WriteFileAsync("r.bin", new byte[10]);

        var seen = new System.Collections.Generic.List<CompareProgress>();
        await FileByteCompareService.CompareAsync(
            left, right, p => { lock (seen) seen.Add(p); }, CancellationToken.None, bufferSize: 4);

        // 10 byte / blocchi da 4 → Total = 3; prima invocazione (0,3), ultima (3,3).
        Assert.Equal(new CompareProgress(0, 3), seen.First());
        Assert.Equal(new CompareProgress(3, 3), seen.Last());
    }

    [Fact]
    public async Task CompareAsync_CancelledToken_ThrowsOperationCanceled()
    {
        string left = await WriteFileAsync("l.bin", new byte[1024]);
        string right = await WriteFileAsync("r.bin", new byte[1024]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileByteCompareService.CompareAsync(left, right, null, cts.Token));
    }
}
```

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test --filter FileByteCompareServiceTests`
Expected: FAIL (tipi `FileByteCompareService`/`ByteRangeDiff`/`FileCompareResult` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 4: Implement `FileByteCompareService`**

```csharp
// FileExplorer/Services/FileByteCompareService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Intervallo di byte differenti tra due file (offset assoluto e lunghezza).</summary>
public sealed record ByteRangeDiff(long Offset, long Length);

/// <summary>
/// Esito del confronto binario di due file: primo offset diverso, byte identici
/// nella zona sovrapposta, intervalli differenti (eventualmente troncati).
/// La coda oltre il file più corto conta come un unico intervallo differente.
/// </summary>
public sealed record FileCompareResult(
    long LeftLength,
    long RightLength,
    long? FirstDifferenceOffset,
    long IdenticalBytes,
    IReadOnlyList<ByteRangeDiff> DifferentRanges,
    bool RangesTruncated)
{
    /// <summary>Frazione identica rispetto al file più lungo (1.0 per due file vuoti).</summary>
    public double IdenticalFraction =>
        Math.Max(LeftLength, RightLength) == 0
            ? 1.0
            : (double)IdenticalBytes / Math.Max(LeftLength, RightLength);

    /// <summary>Vero se i due file sono byte-per-byte identici.</summary>
    public bool AreIdentical => FirstDifferenceOffset is null && LeftLength == RightLength;
}

/// <summary>
/// Confronto binario in streaming di due file, a blocchi. Gli intervalli differenti
/// contigui vengono uniti anche attraverso i confini di blocco; oltre
/// <c>maxRanges</c> intervalli l'elenco è troncato ma i contatori restano esatti.
/// Il progresso è in blocchi: Total = ceil(max(len) / bufferSize).
/// </summary>
public static class FileByteCompareService
{
    private const int DefaultBufferSize = 1024 * 1024;

    public static async Task<FileCompareResult> CompareAsync(
        string leftPath,
        string rightPath,
        Action<CompareProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        int maxRanges = 256)
    {
        ct.ThrowIfCancellationRequested();

        await using var left = new FileStream(
            leftPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var right = new FileStream(
            rightPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

        long leftLength = left.Length;
        long rightLength = right.Length;
        long minLength = Math.Min(leftLength, rightLength);
        long maxLength = Math.Max(leftLength, rightLength);
        int totalBlocks = (int)((maxLength + bufferSize - 1) / bufferSize);

        byte[] leftBuffer = new byte[bufferSize];
        byte[] rightBuffer = new byte[bufferSize];

        long? firstDifference = null;
        long identicalBytes = 0;
        var ranges = new List<ByteRangeDiff>();
        bool truncated = false;
        long openRangeStart = -1; // inizio dell'intervallo differente aperto, -1 = nessuno

        long position = 0;
        int processedBlocks = 0;
        onProgress?.Invoke(new CompareProgress(0, totalBlocks));

        while (position < minLength)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(bufferSize, minLength - position);
            await left.ReadAtLeastAsync(leftBuffer.AsMemory(0, toRead), toRead, throwOnEndOfStream: true, ct);
            await right.ReadAtLeastAsync(rightBuffer.AsMemory(0, toRead), toRead, throwOnEndOfStream: true, ct);

            if (openRangeStart < 0 && leftBuffer.AsSpan(0, toRead).SequenceEqual(rightBuffer.AsSpan(0, toRead)))
            {
                // Fast path: blocco interamente identico e nessun intervallo aperto.
                identicalBytes += toRead;
            }
            else
            {
                for (int i = 0; i < toRead; i++)
                {
                    if (leftBuffer[i] == rightBuffer[i])
                    {
                        identicalBytes++;
                        if (openRangeStart >= 0)
                        {
                            CloseRange(ranges, openRangeStart, position + i, maxRanges, ref truncated);
                            openRangeStart = -1;
                        }
                    }
                    else
                    {
                        firstDifference ??= position + i;
                        if (openRangeStart < 0)
                            openRangeStart = position + i;
                    }
                }
            }

            position += toRead;
            processedBlocks++;
            onProgress?.Invoke(new CompareProgress(processedBlocks, totalBlocks));
        }

        if (maxLength > minLength)
        {
            // Coda oltre il file più corto: unico intervallo differente
            // (fuso con l'eventuale intervallo aperto che termina a minLength).
            firstDifference ??= minLength;
            if (openRangeStart < 0)
                openRangeStart = minLength;
            CloseRange(ranges, openRangeStart, maxLength, maxRanges, ref truncated);
            openRangeStart = -1;

            if (processedBlocks < totalBlocks)
            {
                processedBlocks = totalBlocks;
                onProgress?.Invoke(new CompareProgress(processedBlocks, totalBlocks));
            }
        }
        else if (openRangeStart >= 0)
        {
            CloseRange(ranges, openRangeStart, minLength, maxRanges, ref truncated);
            openRangeStart = -1;
        }

        return new FileCompareResult(
            leftLength, rightLength, firstDifference, identicalBytes, ranges, truncated);
    }

    private static void CloseRange(
        List<ByteRangeDiff> ranges, long start, long endExclusive, int maxRanges, ref bool truncated)
    {
        if (ranges.Count >= maxRanges)
        {
            truncated = true;
            return;
        }

        ranges.Add(new ByteRangeDiff(start, endExclusive - start));
    }
}
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test --filter FileByteCompareServiceTests`
Expected: PASS (9 test).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Services/FileByteCompareService.cs FileExplorer.Tests/FileByteCompareServiceTests.cs
git commit -m "feat(compare): FileByteCompareService per confronto binario a intervalli"
```

---

### Task 5: Card "Confronto binario" nella scheda Confronto

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/ComparisonViewModel.cs`
- Modify: `FileExplorer/Views/ComparisonView.axaml`
- Modify: `IDEE.md` (punto 11 `[ ]` → `[x]`)
- Test: `FileExplorer.Tests/ComparisonViewModelTests.cs` (estensione)

**Interfaces:**
- Consumes (dal Task 4):
  - `FileByteCompareService.CompareAsync(string, string, Action<CompareProgress>?, CancellationToken, int, int)`
  - `FileCompareResult` (`AreIdentical`, `FirstDifferenceOffset`, `IdenticalFraction`, `DifferentRanges`, `RangesTruncated`)
- Produces (usate solo dalla view):
  - `string? LeftFilePath`, `string? RightFilePath`, `bool IsFileComparing`, `string FileCompareStatus`, `FileCompareResult? FileResult`, `bool HasFileResult`
  - `string FirstDiffText`, `string IdenticalPercentText`, `string RangeCountText`
  - `ReactiveCommand<Unit, Unit> BrowseLeftFileCommand/BrowseRightFileCommand/CompareFilesCommand/CancelFileCompareCommand`
  - `public async Task CompareFilesAsync()` (pubblico per i test)

Nota formattazione: le stringhe derivate usano `CultureInfo.GetCultureInfo("it-IT")` esplicita (offset con separatore migliaia "1.048.576", percentuale `0.##`), così i test sono deterministici su qualunque culture di macchina.

- [ ] **Step 1: Write the failing tests**

Contenuto completo aggiornato di `FileExplorer.Tests/ComparisonViewModelTests.cs` (i primi 4 test sono quelli esistenti, invariati):

```csharp
// FileExplorer.Tests/ComparisonViewModelTests.cs
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

    [Fact]
    public async Task CompareFilesAsync_IdenticalFiles_SetsAreIdenticalAndStatus()
    {
        string leftFile = Path.Combine(_left, "f.bin");
        string rightFile = Path.Combine(_right, "f.bin");
        await File.WriteAllBytesAsync(leftFile, new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(rightFile, new byte[] { 1, 2, 3, 4 });

        using var viewModel = new ComparisonViewModel { LeftFilePath = leftFile, RightFilePath = rightFile };

        await viewModel.CompareFilesAsync();

        Assert.True(viewModel.HasFileResult);
        Assert.True(viewModel.FileResult!.AreIdentical);
        Assert.False(viewModel.IsFileComparing);
        Assert.Equal("File identici", viewModel.FileCompareStatus);
        Assert.Equal("Nessuna differenza", viewModel.FirstDiffText);
        Assert.Equal("100 % identico", viewModel.IdenticalPercentText);
        Assert.Equal("0 intervalli differenti", viewModel.RangeCountText);
    }

    [Fact]
    public async Task CompareFilesAsync_DifferentFiles_ReportsOffsetAndPercent()
    {
        string leftFile = Path.Combine(_left, "f.bin");
        string rightFile = Path.Combine(_right, "f.bin");
        // 10 byte, prefisso identico di 6 → primo diverso a offset 6, 60 % identico.
        await File.WriteAllBytesAsync(leftFile, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        await File.WriteAllBytesAsync(rightFile, new byte[] { 0, 1, 2, 3, 4, 5 });

        using var viewModel = new ComparisonViewModel { LeftFilePath = leftFile, RightFilePath = rightFile };

        await viewModel.CompareFilesAsync();

        Assert.True(viewModel.HasFileResult);
        Assert.False(viewModel.FileResult!.AreIdentical);
        Assert.Equal("Primo byte diverso: offset 6 (0x6)", viewModel.FirstDiffText);
        Assert.Equal("60 % identico", viewModel.IdenticalPercentText);
        Assert.Equal("1 intervalli differenti", viewModel.RangeCountText);
        Assert.Contains("diversi", viewModel.FileCompareStatus);
    }

    [Fact]
    public async Task CompareFilesAsync_InvalidPaths_SetsErrorStatus()
    {
        using var viewModel = new ComparisonViewModel
        {
            LeftFilePath = Path.Combine(_left, "manca.bin"),
            RightFilePath = null
        };

        await viewModel.CompareFilesAsync();

        Assert.False(viewModel.HasFileResult);
        Assert.Contains("Selezionare", viewModel.FileCompareStatus);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter ComparisonViewModelTests`
Expected: FAIL (proprietà `LeftFilePath`/`CompareFilesAsync` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement ViewModel**

Contenuto completo aggiornato di `FileExplorer/ViewModels/ComparisonViewModel.cs`:

```csharp
// FileExplorer/ViewModels/ComparisonViewModel.cs
using System;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Confronto": confronta due directory (cascata dimensione → SHA-256)
/// ed esporta il report in HTML/CSV/JSON; confronta inoltre due file byte per byte
/// (primo offset diverso, percentuale identica, intervalli differenti).
/// </summary>
public class ComparisonViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo ItCulture = CultureInfo.GetCultureInfo("it-IT");

    private CancellationTokenSource? _compareCts;
    private CancellationTokenSource? _fileCompareCts;
    private string? _comparedLeftRoot;
    private string? _comparedRightRoot;

    public ComparisonViewModel()
    {
        BrowseLeftCommand = ReactiveCommand.CreateFromTask(BrowseLeftAsync);
        BrowseRightCommand = ReactiveCommand.CreateFromTask(BrowseRightAsync);
        CompareCommand = ReactiveCommand.CreateFromTask(CompareAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExportHtmlCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Html));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Csv));
        ExportJsonCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Json));
        BrowseLeftFileCommand = ReactiveCommand.CreateFromTask(BrowseLeftFileAsync);
        BrowseRightFileCommand = ReactiveCommand.CreateFromTask(BrowseRightFileAsync);
        CompareFilesCommand = ReactiveCommand.CreateFromTask(CompareFilesAsync);
        CancelFileCompareCommand = ReactiveCommand.Create(CancelFileCompare);
    }

    public ReactiveCommand<Unit, Unit> BrowseLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportHtmlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseLeftFileCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightFileCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareFilesCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelFileCompareCommand { get; }

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

    private string? _leftFilePath;
    public string? LeftFilePath
    {
        get => _leftFilePath;
        set => this.RaiseAndSetIfChanged(ref _leftFilePath, value);
    }

    private string? _rightFilePath;
    public string? RightFilePath
    {
        get => _rightFilePath;
        set => this.RaiseAndSetIfChanged(ref _rightFilePath, value);
    }

    private bool _isFileComparing;
    public bool IsFileComparing
    {
        get => _isFileComparing;
        private set => this.RaiseAndSetIfChanged(ref _isFileComparing, value);
    }

    private string _fileCompareStatus = "Selezionare due file da confrontare";
    public string FileCompareStatus
    {
        get => _fileCompareStatus;
        private set => this.RaiseAndSetIfChanged(ref _fileCompareStatus, value);
    }

    private FileCompareResult? _fileResult;
    public FileCompareResult? FileResult
    {
        get => _fileResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref _fileResult, value);
            this.RaisePropertyChanged(nameof(HasFileResult));
            this.RaisePropertyChanged(nameof(FirstDiffText));
            this.RaisePropertyChanged(nameof(IdenticalPercentText));
            this.RaisePropertyChanged(nameof(RangeCountText));
        }
    }

    public bool HasFileResult => FileResult is not null;

    public string FirstDiffText => FileResult switch
    {
        null => string.Empty,
        { FirstDifferenceOffset: long offset } =>
            $"Primo byte diverso: offset {offset.ToString("N0", ItCulture)} (0x{offset:X})",
        _ => "Nessuna differenza"
    };

    public string IdenticalPercentText => FileResult is { } result
        ? string.Format(ItCulture, "{0:0.##} % identico", result.IdenticalFraction * 100)
        : string.Empty;

    public string RangeCountText => FileResult is { } result
        ? $"{result.DifferentRanges.Count} intervalli differenti" +
          (result.RangesTruncated ? " (elenco troncato)" : string.Empty)
        : string.Empty;

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

    private async Task BrowseLeftFileAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: false, LeftFilePath);
        if (!string.IsNullOrEmpty(selected))
            LeftFilePath = selected;
    }

    private async Task BrowseRightFileAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: false, RightFilePath);
        if (!string.IsNullOrEmpty(selected))
            RightFilePath = selected;
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
            // Catturati prima dell'await: le TextBox restano editabili durante il confronto,
            // quindi LeftPath/RightPath potrebbero cambiare mentre CompareAsync è in corso.
            string left = LeftPath;
            string right = RightPath;

            int parallelism = Math.Max(2, Environment.ProcessorCount - 1);
            var result = await DirectoryComparisonService.CompareAsync(
                left, right, parallelism,
                progress => StatusText = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            Result = result;
            _comparedLeftRoot = left;
            _comparedRightRoot = right;
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

    /// <summary>Confronta i due file selezionati byte per byte. Pubblico per i test.</summary>
    public async Task CompareFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftFilePath) || string.IsNullOrWhiteSpace(RightFilePath)
            || !File.Exists(LeftFilePath) || !File.Exists(RightFilePath))
        {
            FileCompareStatus = "Selezionare due file esistenti";
            return;
        }

        _fileCompareCts?.Cancel();
        _fileCompareCts?.Dispose();
        _fileCompareCts = new CancellationTokenSource();
        var ct = _fileCompareCts.Token;

        IsFileComparing = true;
        FileResult = null;
        FileCompareStatus = "Confronto in corso…";

        try
        {
            // Catturati prima dell'await, come per il confronto directory.
            string left = LeftFilePath;
            string right = RightFilePath;

            var result = await FileByteCompareService.CompareAsync(
                left, right,
                progress => FileCompareStatus = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            FileResult = result;
            FileCompareStatus = result.AreIdentical
                ? "File identici"
                : $"File diversi ({result.DifferentRanges.Count} intervalli)";
        }
        catch (OperationCanceledException)
        {
            FileCompareStatus = "Confronto annullato";
        }
        catch (Exception ex)
        {
            FileCompareStatus = $"Errore: {ex.Message}";
        }
        finally
        {
            IsFileComparing = false;
        }
    }

    private void CancelFileCompare() => _fileCompareCts?.Cancel();

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
        if (Result is null || _comparedLeftRoot is null || _comparedRightRoot is null)
            return null;

        try
        {
            DateTime generatedUtc = DateTime.UtcNow;
            string filePath = Path.Combine(
                targetDirectory, ComparisonReportExporter.SuggestFileName(format, generatedUtc));

            await ComparisonReportExporter.ExportAsync(
                filePath, Result, format, _comparedLeftRoot, _comparedRightRoot, generatedUtc, CancellationToken.None);

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
        _fileCompareCts?.Cancel();
        _fileCompareCts?.Dispose();
        _fileCompareCts = null;
        GC.SuppressFinalize(this);
    }
}
```

Nota su `FirstDiffText`: il pattern `{ FirstDifferenceOffset: long offset }` matcha solo quando l'offset non è null; per un risultato identico (offset null) cade nel ramo `_` → "Nessuna differenza".

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter ComparisonViewModelTests`
Expected: PASS (7 test: 4 esistenti + 3 nuovi).

- [ ] **Step 5: Update view**

Contenuto completo aggiornato di `FileExplorer/Views/ComparisonView.axaml` (nuova card in fondo):

```xml
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

            <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextMuted}" />
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
                <TextBlock Text="Identici" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding DifferentCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Diversi" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding LeftOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a sinistra" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding RightOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a destra" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
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

        <Border Classes="card">
          <StackPanel Spacing="10">
            <TextBlock Text="Confronto binario di due file" FontSize="15" FontWeight="SemiBold"
                       Foreground="{DynamicResource Brush.TextPrimary}" />

            <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,8,Auto">
              <TextBlock Grid.Row="0" Grid.Column="0" Text="File 1:" Width="70" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding LeftFilePath}" Watermark="Primo file" />
              <Button Grid.Row="0" Grid.Column="2" Classes="iconbtn" Command="{Binding BrowseLeftFileCommand}"
                      ToolTip.Tip="Sfoglia">
                <i:Icon Value="fa-solid fa-file" />
              </Button>

              <TextBlock Grid.Row="2" Grid.Column="0" Text="File 2:" Width="70" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding RightFilePath}" Watermark="Secondo file" />
              <Button Grid.Row="2" Grid.Column="2" Classes="iconbtn" Command="{Binding BrowseRightFileCommand}"
                      ToolTip.Tip="Sfoglia">
                <i:Icon Value="fa-solid fa-file" />
              </Button>
            </Grid>

            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button Classes="primary" Command="{Binding CompareFilesCommand}" IsEnabled="{Binding !IsFileComparing}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <i:Icon Value="fa-solid fa-code-compare" />
                  <TextBlock Text="Confronta file" />
                </StackPanel>
              </Button>
              <Button Classes="secondary" Command="{Binding CancelFileCompareCommand}"
                      IsEnabled="{Binding IsFileComparing}">
                <TextBlock Text="Annulla" />
              </Button>
            </StackPanel>

            <TextBlock Text="{Binding FileCompareStatus}" Foreground="{DynamicResource Brush.TextMuted}" />

            <StackPanel Spacing="4" IsVisible="{Binding HasFileResult}">
              <TextBlock Text="{Binding FirstDiffText}" Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBlock Text="{Binding IdenticalPercentText}" Foreground="{DynamicResource Brush.TextPrimary}" />
              <TextBlock Text="{Binding RangeCountText}" Foreground="{DynamicResource Brush.TextMuted}" />
            </StackPanel>
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>

  </DockPanel>

</UserControl>
```

- [ ] **Step 6: Build e verifica manuale minima**

Run: `dotnet build FileExplorer.sln`
Expected: Build succeeded, 0 errori. (Facoltativo, se DISPLAY disponibile: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project FileExplorer.Desktop` e verifica visiva della card nella scheda Confronto.)

- [ ] **Step 7: Update IDEE.md**

In `IDEE.md`, riga del punto 11: `11. \`[ ]\`` → `11. \`[x]\``.

- [ ] **Step 8: Run full test suite**

Run: `dotnet test`
Expected: PASS, nessun test rotto (316 preesistenti + 12 nuovi della fase).

- [ ] **Step 9: Commit, push e PR**

```bash
git add FileExplorer/ViewModels/ComparisonViewModel.cs FileExplorer/Views/ComparisonView.axaml FileExplorer.Tests/ComparisonViewModelTests.cs IDEE.md
git commit -m "feat(compare): card confronto binario di due file nella scheda Confronto"
git push -u origin feature/file-byte-compare
gh pr create --title "Confronto byte-range di due file (IDEE 11)" --body "Confronto binario in streaming di due file: primo offset diverso, percentuale identica, intervalli differenti (troncati oltre 256). Nuova card nella scheda Confronto."
```

---
## Fase 3 — Delta-copy a blocchi (IDEE punto 5) — branch `feature/delta-copy`

**Decisione di design.** Niente rolling-checksum stile rsync: quel meccanismo serve quando la destinazione è raggiungibile solo tramite un canale remoto a banda limitata e conviene scambiare firme invece di dati. Qui la destinazione è un filesystem direttamente scrivibile, quindi si usa una **sync in-place a blocchi fissi** (stile blocksync/TeraCopy): sorgente e destinazione vengono letti a blocchi alla stessa posizione e viene riscritto **solo** il blocco che differisce. Il risparmio è sulle **scritture** (usura SSD, dischi/reti lente in scrittura), non sulle letture. Il meccanismo gestisce solo modifiche in-place, non inserzioni/rimozioni di byte (che disallineano i blocchi successivi: restano corretti ma vengono riscritti) — adatto ai casi d'uso del punto 5: VM, database, video in editing.

**Ordine e conflitti.** Questa fase modifica `CopyPairsViewModel` e `SettingsView.axaml`, toccati anche dalla Fase 1 (`feature/copy-profiles`): il branch va creato da `main` **dopo il merge della Fase 1**. I punti di modifica sono indipendenti (qui: `CopyDirectoryAsync` privato e card "Copia" delle Impostazioni; Fase 1: comandi profili e card dedicata) — un eventuale conflitto è banale e va risolto conservando entrambe le feature.

---

### Task 6: DeltaCopyService

**Model:** opus

(logica di integrità dati: seek/riscritture in-place su file esistenti dell'utente)

**Files:**
- Create: `FileExplorer/Services/DeltaCopyService.cs`
- Test: `FileExplorer.Tests/DeltaCopyServiceTests.cs`

**Interfaces:**
- Consumes: `FileCopyService.CopyFileAsync(string, string, Action<long>?, CancellationToken, int)` (fallback destinazione mancante), `IoThrottleService.WaitAsync(long, CancellationToken)`.
- Produces:
  - `public sealed record DeltaCopyResult(long TotalBytes, long BytesWritten, int BlocksTotal, int BlocksChanged);`
  - `public static Task<DeltaCopyResult> DeltaCopyService.CopyFileDeltaAsync(string sourcePath, string destinationPath, Action<long>? onBytesCopied, CancellationToken ct, int blockSize = 1024 * 1024)`
  - Semantica di `onBytesCopied`: identica a `FileCopyService.CopyFileAsync` — conta i byte **sorgente** processati per blocco (così `CopyProgress.Fraction` resta coerente a monte).

- [ ] **Step 0: Branch di lavoro**

```bash
git checkout main && git pull
git switch -c feature/delta-copy
```

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/DeltaCopyServiceTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DeltaCopyServiceTests : IDisposable
{
    private readonly string _root;

    // Il throttle legge AppSettingsStore.Current: salvato/ripristinato per non contaminare gli altri test.
    private readonly AppSettings _originalCurrent;

    public DeltaCopyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] Pattern(int length, byte seed = 0) =>
        Enumerable.Range(0, length).Select(i => (byte)((i + seed) % 256)).ToArray();

    [Fact]
    public async Task CopyFileDeltaAsync_IdenticalDestination_WritesNothing()
    {
        string source = Path.Combine(_root, "same-src.bin");
        string destination = Path.Combine(_root, "same-dst.bin");
        byte[] content = Pattern(3 * 4096);
        await File.WriteAllBytesAsync(source, content);
        await File.WriteAllBytesAsync(destination, content);

        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, null, CancellationToken.None, blockSize: 4096);

        Assert.Equal(0, result.BytesWritten);
        Assert.Equal(0, result.BlocksChanged);
        Assert.Equal(3, result.BlocksTotal);
        Assert.Equal(content.LongLength, result.TotalBytes);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_SingleChangedBlock_RewritesOnlyThatBlock()
    {
        string source = Path.Combine(_root, "mid-src.bin");
        string destination = Path.Combine(_root, "mid-dst.bin");
        byte[] sourceContent = Pattern(3 * 4096);
        byte[] destinationContent = (byte[])sourceContent.Clone();
        destinationContent[4096 + 100] ^= 0xFF; // un byte diverso nel secondo blocco
        await File.WriteAllBytesAsync(source, sourceContent);
        await File.WriteAllBytesAsync(destination, destinationContent);

        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, null, CancellationToken.None, blockSize: 4096);

        Assert.Equal(4096, result.BytesWritten);
        Assert.Equal(1, result.BlocksChanged);
        Assert.Equal(3, result.BlocksTotal);
        Assert.Equal(sourceContent, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_SourceLonger_ExtendsDestination()
    {
        string source = Path.Combine(_root, "grow-src.bin");
        string destination = Path.Combine(_root, "grow-dst.bin");
        byte[] sourceContent = Pattern(10000);
        await File.WriteAllBytesAsync(source, sourceContent);
        await File.WriteAllBytesAsync(destination, sourceContent.AsSpan(0, 8192).ToArray());

        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, null, CancellationToken.None, blockSize: 4096);

        // Blocchi 1-2 (8192 byte) identici; il terzo blocco (1808 byte) manca in destinazione.
        Assert.Equal(1808, result.BytesWritten);
        Assert.Equal(1, result.BlocksChanged);
        Assert.Equal(sourceContent, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_SourceShorter_TruncatesDestination()
    {
        string source = Path.Combine(_root, "shrink-src.bin");
        string destination = Path.Combine(_root, "shrink-dst.bin");
        byte[] sourceContent = Pattern(4096);
        byte[] destinationContent = Pattern(8192); // stesso prefisso, coda in più
        await File.WriteAllBytesAsync(source, sourceContent);
        await File.WriteAllBytesAsync(destination, destinationContent);

        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, null, CancellationToken.None, blockSize: 4096);

        Assert.Equal(0, result.BytesWritten); // primo blocco identico: la coda è solo troncata
        Assert.Equal(sourceContent, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_MissingDestination_FallsBackToFullCopy()
    {
        string source = Path.Combine(_root, "new-src.bin");
        string destination = Path.Combine(_root, "new-dst.bin");
        byte[] content = Pattern(10000);
        await File.WriteAllBytesAsync(source, content);

        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, null, CancellationToken.None, blockSize: 4096);

        Assert.Equal(content.LongLength, result.TotalBytes);
        Assert.Equal(content.LongLength, result.BytesWritten);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_AlignsDestinationMtimeToSource()
    {
        string source = Path.Combine(_root, "mtime-src.bin");
        string destination = Path.Combine(_root, "mtime-dst.bin");
        await File.WriteAllBytesAsync(source, Pattern(100));
        await File.WriteAllBytesAsync(destination, Pattern(100, seed: 7));
        var sourceTime = new DateTime(2021, 3, 2, 8, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, sourceTime);

        await DeltaCopyService.CopyFileDeltaAsync(source, destination, null, CancellationToken.None);

        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public async Task CopyFileDeltaAsync_CallbackSumsToTotalBytes()
    {
        string source = Path.Combine(_root, "cb-src.bin");
        string destination = Path.Combine(_root, "cb-dst.bin");
        await File.WriteAllBytesAsync(source, Pattern(10000));
        await File.WriteAllBytesAsync(destination, Pattern(10000, seed: 3));

        long totalReported = 0;
        var result = await DeltaCopyService.CopyFileDeltaAsync(
            source, destination, delta => totalReported += delta, CancellationToken.None, blockSize: 4096);

        Assert.Equal(result.TotalBytes, totalReported);
        Assert.Equal(10000, totalReported);
    }

    [Fact]
    public async Task CopyFileDeltaAsync_CancelledToken_Throws()
    {
        string source = Path.Combine(_root, "cancel-src.bin");
        string destination = Path.Combine(_root, "cancel-dst.bin");
        await File.WriteAllBytesAsync(source, Pattern(4096));
        await File.WriteAllBytesAsync(destination, Pattern(4096, seed: 5));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeltaCopyService.CopyFileDeltaAsync(source, destination, null, cts.Token));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter DeltaCopyServiceTests`
Expected: FAIL (`DeltaCopyService`/`DeltaCopyResult` non esistono → errore di compilazione del progetto test).

- [ ] **Step 3: Implement DeltaCopyService**

```csharp
// FileExplorer/Services/DeltaCopyService.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Esito di una delta-copy: byte totali della sorgente, byte effettivamente riscritti
/// in destinazione e conteggio dei blocchi (totali e cambiati).
/// </summary>
public sealed record DeltaCopyResult(long TotalBytes, long BytesWritten, int BlocksTotal, int BlocksChanged);

/// <summary>
/// Copia "delta" in-place a blocchi fissi (stile blocksync): legge sorgente e destinazione
/// alla stessa posizione e riscrive solo i blocchi diversi. Il risparmio è sulle scritture
/// (usura SSD, dischi/reti lente in scrittura) per file grandi modificati poco (VM, database,
/// video in editing). Gestisce solo modifiche in-place: un inserimento/rimozione di byte
/// disallinea i blocchi successivi, che risultano diversi e vengono riscritti
/// (correttezza garantita, risparmio nullo in quel caso).
/// </summary>
public static class DeltaCopyService
{
    private const int DefaultBlockSize = 1024 * 1024; // 1 MB, come FileCopyService

    /// <summary>
    /// Sincronizza <paramref name="destinationPath"/> con <paramref name="sourcePath"/>
    /// riscrivendo solo i blocchi diversi. Se la destinazione non esiste, ricade sulla
    /// copia integrale. <paramref name="onBytesCopied"/> conta i byte sorgente processati
    /// per blocco (stessa semantica di <see cref="FileCopyService.CopyFileAsync"/>).
    /// </summary>
    public static async Task<DeltaCopyResult> CopyFileDeltaAsync(
        string sourcePath,
        string destinationPath,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int blockSize = DefaultBlockSize)
    {
        if (blockSize <= 0)
            blockSize = DefaultBlockSize;

        if (!File.Exists(destinationPath))
        {
            // Nulla da confrontare: copia integrale (throttle, mtime e callback già gestiti lì).
            await FileCopyService.CopyFileAsync(sourcePath, destinationPath, onBytesCopied, ct, blockSize);
            long length = new FileInfo(sourcePath).Length;
            int blocks = (int)((length + blockSize - 1) / blockSize);
            return new DeltaCopyResult(length, length, blocks, blocks);
        }

        long sourceLength;
        long bytesWritten = 0;
        int blocksTotal = 0;
        int blocksChanged = 0;

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destination = new FileStream(destinationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sourceLength = source.Length;
            var sourceBuffer = new byte[blockSize];
            var destinationBuffer = new byte[blockSize];

            int read;
            while ((read = await FillBufferAsync(source, sourceBuffer, ct)) > 0)
            {
                blocksTotal++;

                // Il throttle conta i byte sorgente processati, come in FileCopyService.
                await IoThrottleService.WaitAsync(read, ct);

                long blockStart = destination.Position;
                int destinationRead = await FillBufferAsync(destination, destinationBuffer, ct);

                bool identical = destinationRead == read
                    && sourceBuffer.AsSpan(0, read).SequenceEqual(destinationBuffer.AsSpan(0, read));

                if (!identical)
                {
                    // Torna all'inizio del blocco e riscrivi i byte della sorgente. La posizione
                    // finale (blockStart + read) resta allineata a quella della sorgente anche
                    // quando la destinazione aveva letto una quantità diversa di byte.
                    destination.Seek(blockStart, SeekOrigin.Begin);
                    await destination.WriteAsync(sourceBuffer.AsMemory(0, read), ct);
                    blocksChanged++;
                    bytesWritten += read;
                }

                onBytesCopied?.Invoke(read);
            }

            // Coda della destinazione oltre la lunghezza della sorgente: troncata.
            if (destination.Length != sourceLength)
                destination.SetLength(sourceLength);

            await destination.FlushAsync(ct);
        }

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato,
        // come fa FileCopyService.
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));

        return new DeltaCopyResult(sourceLength, bytesWritten, blocksTotal, blocksChanged);
    }

    /// <summary>
    /// Riempe il buffer fino alla capienza o all'EOF: ReadAsync può restituire letture parziali
    /// anche lontano dall'EOF, e il confronto a blocchi richiede blocchi pieni e allineati.
    /// </summary>
    private static async Task<int> FillBufferAsync(FileStream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter DeltaCopyServiceTests`
Expected: PASS (8 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/DeltaCopyService.cs FileExplorer.Tests/DeltaCopyServiceTests.cs
git commit -m "feat(copy): DeltaCopyService con sync in-place a blocchi"
```

---

### Task 7: Integrazione delta-copy nel motore, setting e UI

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/Models/AppSettings.cs`
- Modify: `FileExplorer/Services/FileCopyService.cs` (firme `CopyDirectoryAsync`/`CopyDirectoryToManyAsync` + worker)
- Modify: `FileExplorer/ViewModels/SettingsViewModel.cs`
- Modify: `FileExplorer/Views/SettingsView.axaml`
- Modify: `FileExplorer/ViewModels/CopyPairsViewModel.cs` (solo il metodo privato `CopyDirectoryAsync`, righe ~403-492)
- Modify: `IDEE.md` (punto 5 → `[x]`)
- Test: `FileExplorer.Tests/FileCopyServiceTests.cs`, `FileExplorer.Tests/SettingsViewModelTests.cs`, `FileExplorer.Tests/AppSettingsStoreTests.cs` (aggiunte)

**Interfaces:**
- Consumes: `DeltaCopyService.CopyFileDeltaAsync(...)` e `DeltaCopyResult` (Task 6), `SizeFormatter.Format(long)`.
- Produces:
  - `AppSettings.DeltaCopyEnabled : bool` (default `false`)
  - `FileCopyService.CopyDirectoryAsync(string sourceRoot, string destinationRoot, int maxDegreeOfParallelism, Action<CopyProgress>? onProgress, CancellationToken ct, int bufferSize = DefaultBufferSize, bool skipUnchanged = false, bool deltaCopy = false, Action<long>? onBytesWritten = null)`
  - `FileCopyService.CopyDirectoryToManyAsync(string sourceRoot, IReadOnlyList<string> destinationRoots, int maxDegreeOfParallelism, Action<CopyProgress>? onProgress, CancellationToken ct, int bufferSize = DefaultBufferSize, bool skipUnchanged = false, bool deltaCopy = false, Action<long>? onBytesWritten = null)` — i nuovi parametri sono opzionali e IN CODA: nessun call-site esistente si rompe.
  - `SettingsViewModel.DeltaCopyEnabled : bool` (pattern auto-save standard)
  - Semantica `onBytesWritten`: byte fisicamente scritti in destinazione, invocato una volta per file per destinazione (con delta: `DeltaCopyResult.BytesWritten`; senza delta o per destinazioni mancanti: lunghezza del file per numero di destinazioni scritte). I file saltati da `skipUnchanged` non lo invocano.

- [ ] **Step 1: Write the failing tests**

Aggiungere in coda a `FileExplorer.Tests/FileCopyServiceTests.cs` (prima della chiusura della classe):

```csharp
    [Fact]
    public async Task CopyDirectoryAsync_DeltaCopy_RewritesOnlyChangedFileBytes()
    {
        string sourceRoot = Path.Combine(_root, "delta-src");
        string destinationRoot = Path.Combine(_root, "delta-dst");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "same.bin"), new byte[2048]);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "changed.bin"), new byte[1024]);

        // Prima copia completa, poi la sorgente di changed.bin cambia contenuto a parità di dimensione.
        await FileCopyService.CopyDirectoryAsync(sourceRoot, destinationRoot, 1, null, CancellationToken.None);
        byte[] newContent = Enumerable.Repeat((byte)0xAB, 1024).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "changed.bin"), newContent);

        long written = 0;
        await FileCopyService.CopyDirectoryAsync(
            sourceRoot, destinationRoot, 1, null, CancellationToken.None,
            deltaCopy: true,
            onBytesWritten: bytes => written += bytes);

        Assert.Equal(1024, written); // same.bin intatto (0 byte); changed.bin riscritto per intero
        Assert.Equal(newContent, await File.ReadAllBytesAsync(Path.Combine(destinationRoot, "changed.bin")));
    }

    [Fact]
    public async Task CopyDirectoryToManyAsync_DeltaCopy_MixedDestinations_CountsSourceBytesOnce()
    {
        string sourceRoot = Path.Combine(_root, "deltamix-src");
        Directory.CreateDirectory(sourceRoot);
        byte[] content = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "file.bin"), content);

        string existingRoot = Path.Combine(_root, "deltamix-d1");
        string missingRoot = Path.Combine(_root, "deltamix-d2");
        // d1 riceve una prima copia (resta identica alla sorgente), d2 parte vuota.
        await FileCopyService.CopyDirectoryAsync(sourceRoot, existingRoot, 1, null, CancellationToken.None);

        long progressTotal = 0;
        long written = 0;
        await FileCopyService.CopyDirectoryToManyAsync(
            sourceRoot, new List<string> { existingRoot, missingRoot }, 1,
            progress => progressTotal = progress.CopiedBytes,
            CancellationToken.None,
            deltaCopy: true,
            onBytesWritten: bytes => written += bytes);

        Assert.Equal(4096, progressTotal); // byte sorgente contati una volta sola
        Assert.Equal(4096, written);       // d2 copia piena (4096) + d1 delta (0)
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(missingRoot, "file.bin")));
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(existingRoot, "file.bin")));
    }

    [Fact]
    public async Task CopyDirectoryAsync_WithoutDelta_ReportsBytesWrittenPerFile()
    {
        string sourceRoot = Path.Combine(_root, "written-src");
        string destinationRoot = Path.Combine(_root, "written-dst");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "a.bin"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "b.bin"), new byte[50]);

        long written = 0;
        await FileCopyService.CopyDirectoryAsync(
            sourceRoot, destinationRoot, 1, null, CancellationToken.None,
            onBytesWritten: bytes => written += bytes);

        Assert.Equal(150, written);
    }
```

Aggiungere in coda a `FileExplorer.Tests/SettingsViewModelTests.cs`:

```csharp
    [Fact]
    public void DeltaCopyEnabled_Toggle_UpdatesCurrentSettings()
    {
        var vm = new SettingsViewModel();
        Assert.False(vm.DeltaCopyEnabled); // default disattivo

        vm.DeltaCopyEnabled = true;
        Assert.True(AppSettingsStore.Current.DeltaCopyEnabled);
    }
```

Aggiungere in coda a `FileExplorer.Tests/AppSettingsStoreTests.cs`:

```csharp
    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsDeltaCopyEnabled()
    {
        await AppSettingsStore.SaveAsync(StorePath, new AppSettings { DeltaCopyEnabled = true });
        var loaded = await AppSettingsStore.LoadAsync(StorePath);

        Assert.True(loaded.DeltaCopyEnabled);
    }
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter "FileCopyServiceTests|SettingsViewModelTests|AppSettingsStoreTests"`
Expected: FAIL (parametri `deltaCopy`/`onBytesWritten` e proprietà `DeltaCopyEnabled` inesistenti → errore di compilazione).

- [ ] **Step 3: Implement — AppSettings**

In `FileExplorer/Models/AppSettings.cs`, dopo la proprietà `ThrottleMBps`:

```csharp
    /// <summary>Limite di banda in MB/s (usato solo se <see cref="ThrottleEnabled"/>).</summary>
    public int ThrottleMBps { get; set; } = 50;

    /// <summary>Delta-copy: se il file di destinazione esiste, riscrive solo i blocchi cambiati.</summary>
    public bool DeltaCopyEnabled { get; set; }
```

- [ ] **Step 4: Implement — FileCopyService**

Sostituire integralmente `CopyDirectoryAsync` e `CopyDirectoryToManyAsync` in `FileExplorer/Services/FileCopyService.cs` con le versioni seguenti (nuovi parametri opzionali in coda, dopo `skipUnchanged`; `CopyFileAsync`, `CopyFileToManyAsync` e `IsUnchanged` restano invariati):

```csharp
    /// <summary>
    /// Copia ricorsivamente una cartella (più file in parallelo), replicando la struttura
    /// di <paramref name="sourceRoot"/> sotto <paramref name="destinationRoot"/>.
    /// Il primo evento di avanzamento comunica il totale di file e byte da copiare.
    /// Con <paramref name="deltaCopy"/> i file già presenti in destinazione vengono
    /// sincronizzati a blocchi (solo i blocchi diversi sono riscritti);
    /// <paramref name="onBytesWritten"/> riporta i byte fisicamente scritti.
    /// </summary>
    public static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false,
        bool deltaCopy = false,
        Action<long>? onBytesWritten = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration).ToList();
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

                if (skipUnchanged && IsUnchanged(sourceFile, destinationFile))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                void ReportProgress(long deltaBytes)
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }

                if (deltaCopy && File.Exists(destinationFile))
                {
                    DeltaCopyResult result = await DeltaCopyService.CopyFileDeltaAsync(
                        sourceFile, destinationFile, ReportProgress, ct, bufferSize);
                    onBytesWritten?.Invoke(result.BytesWritten);
                    return;
                }

                await CopyFileAsync(sourceFile, destinationFile, ReportProgress, ct, bufferSize);
                onBytesWritten?.Invoke(new FileInfo(sourceFile).Length);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Copia ricorsivamente una cartella verso più destinazioni (più file in parallelo),
    /// leggendo ogni file sorgente una sola volta. L'avanzamento conta i byte della sorgente.
    /// Con <paramref name="deltaCopy"/> le destinazioni in cui il file esiste già vengono
    /// sincronizzate a blocchi; quelle in cui manca ricevono la copia integrale in fan-out.
    /// </summary>
    public static async Task CopyDirectoryToManyAsync(
        string sourceRoot,
        IReadOnlyList<string> destinationRoots,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false,
        bool deltaCopy = false,
        Action<long>? onBytesWritten = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration).ToList();
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

                if (skipUnchanged && destinationFiles.All(destination => IsUnchanged(sourceFile, destination)))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }

                foreach (var destinationFile in destinationFiles)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                void ReportProgress(long deltaBytes)
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }

                if (!deltaCopy)
                {
                    await CopyFileToManyAsync(sourceFile, destinationFiles, ReportProgress, ct, bufferSize);
                    onBytesWritten?.Invoke(new FileInfo(sourceFile).Length * destinationFiles.Count);
                    return;
                }

                var existing = destinationFiles.Where(File.Exists).ToList();
                var missing = destinationFiles.Where(destination => !File.Exists(destination)).ToList();

                // I byte sorgente vanno contati una sola volta: il progresso lo riporta il primo
                // percorso eseguito (fan-out sulle destinazioni mancanti, altrimenti la prima delta).
                bool progressReported = false;

                if (missing.Count > 0)
                {
                    await CopyFileToManyAsync(sourceFile, missing, ReportProgress, ct, bufferSize);
                    progressReported = true;
                    onBytesWritten?.Invoke(new FileInfo(sourceFile).Length * missing.Count);
                }

                foreach (var destinationFile in existing)
                {
                    DeltaCopyResult result = await DeltaCopyService.CopyFileDeltaAsync(
                        sourceFile, destinationFile, progressReported ? null : ReportProgress, ct, bufferSize);
                    progressReported = true;
                    onBytesWritten?.Invoke(result.BytesWritten);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
```

- [ ] **Step 5: Implement — SettingsViewModel e SettingsView**

In `FileExplorer/ViewModels/SettingsViewModel.cs`, dopo la proprietà `VerifyChecksumAfterCopy`:

```csharp
    /// <summary>Delta-copy: riscrive solo i blocchi cambiati quando la destinazione esiste già.</summary>
    public bool DeltaCopyEnabled
    {
        get => AppSettingsStore.Current.DeltaCopyEnabled;
        set
        {
            if (AppSettingsStore.Current.DeltaCopyEnabled == value)
                return;

            AppSettingsStore.Current.DeltaCopyEnabled = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }
```

In `FileExplorer/Views/SettingsView.axaml`, nella card "Copia", subito dopo la riga "Verifica checksum dopo la copia":

```xml
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="Delta-copy (riscrive solo i blocchi cambiati)"
                         VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
              <ToggleSwitch Grid.Column="1" IsChecked="{Binding DeltaCopyEnabled}" />
            </Grid>
```

- [ ] **Step 6: Implement — CopyPairsViewModel**

Sostituire integralmente il metodo privato `CopyDirectoryAsync` di `FileExplorer/ViewModels/CopyPairsViewModel.cs` con:

```csharp
    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, IReadOnlyList<string> destinations, CancellationToken ct)
    {
        int knownFileCount = -1;
        var tracker = new SpeedTracker(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in destinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        bool deltaEnabled = AppSettingsStore.Current.DeltaCopyEnabled;
        long deltaWrittenBytes = 0;
        long totalSourceBytes = 0;

        await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            destinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: progress =>
            {
                if (knownFileCount != progress.TotalFiles)
                {
                    knownFileCount = progress.TotalFiles;
                    totalSourceBytes = progress.TotalBytes;
                    pair.Status = progress.TotalFiles == 0
                        ? "Nessun file da copiare"
                        : $"Copia cartella… ({progress.TotalFiles} file)";
                    tracker.Start(progress.TotalBytes);
                }

                pair.Progress = progress.Fraction;
                tracker.Report(progress.CopiedBytes);
                if (tracker.TryTakeSnapshot(out var snapshot))
                    PublishSpeed(pair, snapshot);
            },
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged,
            deltaCopy: deltaEnabled,
            onBytesWritten: written => Interlocked.Add(ref deltaWrittenBytes, written));

        if (knownFileCount > 0)
            pair.SpeedText = $"media {FormatSpeed(tracker.AverageBytesPerSecond)} · picco {FormatSpeed(tracker.PeakBytesPerSecond)}";

        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        // Suffisso informativo della delta-copy: quanti byte sono stati davvero riscritti.
        string deltaSuffix = deltaEnabled
            ? $" · delta: scritti {SizeFormatter.Format(Interlocked.Read(ref deltaWrittenBytes))} su {SizeFormatter.Format(totalSourceBytes)}"
            : string.Empty;

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato" + deltaSuffix;
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        pair.Status = "Verifica checksum…";
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in destinations)
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
            pair.Status = $"Completato e verificato ({totalVerified} file)" + deltaSuffix;
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {mismatchedTotal} file diversi, {missingTotal} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
    }
```

- [ ] **Step 7: Run tests, verify they pass**

Run: `dotnet test`
Expected: PASS — l'intera suite (i test esistenti di `FileCopyServiceTests`/`CopyPairsViewModelTests` non devono rompersi: i nuovi parametri sono opzionali e il default `DeltaCopyEnabled = false` conserva il comportamento attuale).

- [ ] **Step 8: Commit**

```bash
git add FileExplorer/Models/AppSettings.cs FileExplorer/Services/FileCopyService.cs \
        FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer/Views/SettingsView.axaml \
        FileExplorer/ViewModels/CopyPairsViewModel.cs \
        FileExplorer.Tests/FileCopyServiceTests.cs FileExplorer.Tests/SettingsViewModelTests.cs \
        FileExplorer.Tests/AppSettingsStoreTests.cs
git commit -m "feat(copy): delta-copy integrata nel motore con toggle nelle Impostazioni"
```

- [ ] **Step 9: Chiusura fase — IDEE.md, push e PR**

In `IDEE.md`, punto 5: `[ ]` → `[x]`.

```bash
git add IDEE.md
git commit -m "docs(idee): punto 5 delta-copy implementato"
git push -u origin feature/delta-copy
gh pr create --title "Delta-copy a blocchi (IDEE punto 5)" --body "Sync in-place a blocchi fissi: con destinazione esistente riscrive solo i blocchi cambiati. Toggle nelle Impostazioni (default off), report byte riscritti nello stato della copia. Fallback automatico a copia integrale per destinazioni nuove."
```

---
## Fase 4 — Watch-folder / sincronizzazione automatica (IDEE punto 8) — branch `feature/watch-folders`

**Scope della fase.** Regole di sincronizzazione automatica sorgente → destinazione, ognuna con due modalità: **"Al cambiamento"** (`FileSystemWatcher` con debounce e coalescing degli eventi) e **"Ogni N minuti"** (intervallo fisso). L'orario fisso cron-style citato in IDEE è **rimandato** (fuori scope). La sync è `FileCopyService.CopyDirectoryAsync(..., skipUnchanged: true)`: incrementale per costruzione (dimensione + mtime con tolleranza 2s), nessun motore nuovo di copia. Nuova tab **"Sync auto"** in `MainWindow`. Le regole persistono in `watch-rules.json` (AppData) e i runner delle regole attive partono all'avvio dell'app.

**Limiti dichiarati (documentati nel codice):**
- Se la sorgente non esiste al momento dello `Start`, il runner **non parte** (nessun retry automatico): stato "Sorgente non trovata", l'utente corregge il percorso o riattiva la regola.
- Le sync watch-folder **non usano** `CopyJournalStore`: una sync interrotta a metà (crash/chiusura) non viene ripresa dal journal; la successiva sync incrementale ricopia i file incompleti perché mtime/dimensione non combaciano.
- Nessun handler di shutdown nell'app (non esiste oggi): i runner muoiono col processo.
- Le regole senza sorgente o destinazione non vengono persistite (`Sanitize` dello store le scarta): una riga appena aggiunta e mai compilata sparisce al riavvio, per design.

**Rischi (flakiness test asincroni) e mitigazioni:**
- `FileSystemWatcher` può emettere più eventi per una singola scrittura (Created+Changed) e i tempi variano per piattaforma (inotify su Linux) → i test non contano gli eventi ma le **sync**, con `DebounceDelay` di test a 200 ms che coalesca la raffica.
- Niente sleep fissi come unica sincronizzazione: helper `WaitUntilAsync` (polling a 10 ms, timeout 5 s) per le attese "finché", `TaskCompletionSource` per orchestrare la sync bloccata; i delay fissi servono solo come "finestra di quiete" per verificare che NON succeda altro.
- Tutti i seam statici (`DebounceDelay`, `SyncOverride`, `IntervalOverride`, `CurrentPath`, `ConfirmDialogHelper.Override`) salvati in ctor e ripristinati in `Dispose`; `StopAll()` in `Dispose`; le collection xunit girano già in seriale (`parallelizeTestCollections: false`).
- Intervallo in minuti troppo lungo per i test → seam `IntervalOverride` (`Func<WatchRule, TimeSpan>`) che nei test restituisce 50 ms.

---

### Task 8: Modello `WatchRule` + `WatchRuleStore`

**Model:** haiku

**Files:**
- Create: `FileExplorer/Models/WatchRule.cs`
- Create: `FileExplorer/Services/WatchRuleStore.cs`
- Test: `FileExplorer.Tests/WatchRuleStoreTests.cs`

**Interfaces:**
- Consumes: nulla (foglia).
- Produces:
  - `public enum WatchMode { OnChange, Interval }`
  - `public class WatchRule { string Id; string SourcePath; string DestinationPath; bool Enabled; WatchMode Mode; int IntervalMinutes; }`
  - `WatchRuleStore.DefaultPath : string` / `WatchRuleStore.CurrentPath : string { get; set; }`
  - `public static List<WatchRule> WatchRuleStore.Load()` (sincrono, solo avvio)
  - `public static Task<List<WatchRule>> WatchRuleStore.LoadAsync()`
  - `public static Task WatchRuleStore.SaveAsync(IReadOnlyList<WatchRule> rules)`
  - `internal static List<WatchRule> WatchRuleStore.Sanitize(IEnumerable<WatchRule> rules)`

- [ ] **Step 0: Branch di lavoro da main aggiornato**

```bash
git checkout main && git pull && git checkout -b feature/watch-folders
```

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/WatchRuleStoreTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class WatchRuleStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalCurrentPath;

    public WatchRuleStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watchrules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrentPath = WatchRuleStore.CurrentPath;
        WatchRuleStore.CurrentPath = Path.Combine(_root, "sub", "watch-rules.json");
    }

    public void Dispose()
    {
        WatchRuleStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static WatchRule CreateRule() => new()
    {
        SourcePath = "/tmp/src",
        DestinationPath = "/tmp/dst",
        Mode = WatchMode.Interval,
        IntervalMinutes = 15
    };

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRule()
    {
        WatchRule rule = CreateRule();

        await WatchRuleStore.SaveAsync(new[] { rule });
        var loaded = await WatchRuleStore.LoadAsync();

        WatchRule single = Assert.Single(loaded);
        Assert.Equal(rule.Id, single.Id);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("/tmp/dst", single.DestinationPath);
        Assert.True(single.Enabled);
        Assert.Equal(WatchMode.Interval, single.Mode);
        Assert.Equal(15, single.IntervalMinutes);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WatchRuleStore.CurrentPath)!);
        await File.WriteAllTextAsync(WatchRuleStore.CurrentPath, "{ json rotto");

        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_DiscardsRulesWithoutPaths()
    {
        var incomplete = new WatchRule { SourcePath = "", DestinationPath = "/tmp/dst" };
        var complete = CreateRule();

        await WatchRuleStore.SaveAsync(new[] { incomplete, complete });
        var loaded = await WatchRuleStore.LoadAsync();

        Assert.Equal(complete.Id, Assert.Single(loaded).Id);
    }

    [Fact]
    public async Task SaveAsync_ClampsIntervalMinutes()
    {
        WatchRule low = CreateRule();
        low.IntervalMinutes = 0;
        WatchRule high = CreateRule();
        high.IntervalMinutes = 99999;

        await WatchRuleStore.SaveAsync(new[] { low, high });
        var loaded = await WatchRuleStore.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(1, loaded[0].IntervalMinutes);
        Assert.Equal(1440, loaded[1].IntervalMinutes);
    }

    [Fact]
    public async Task Load_Sync_ReadsSavedRules()
    {
        await WatchRuleStore.SaveAsync(new[] { CreateRule() });

        Assert.Single(WatchRuleStore.Load());
    }

    [Fact]
    public void Sanitize_AssignsIdWhenMissing()
    {
        var rule = CreateRule();
        rule.Id = "";

        var sanitized = WatchRuleStore.Sanitize(new[] { rule });

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(sanitized).Id));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter WatchRuleStoreTests`
Expected: FAIL (tipi `WatchRule`/`WatchRuleStore` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement `WatchRule` e `WatchRuleStore`**

```csharp
// FileExplorer/Models/WatchRule.cs
using System;

namespace FileExplorer.Models;

/// <summary>Modalità di sincronizzazione di una regola watch-folder.</summary>
public enum WatchMode
{
    /// <summary>Sincronizza al cambiamento della sorgente (FileSystemWatcher + debounce).</summary>
    OnChange,

    /// <summary>Sincronizza a intervallo fisso di minuti.</summary>
    Interval
}

/// <summary>Regola di sincronizzazione automatica sorgente → destinazione.</summary>
public class WatchRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public WatchMode Mode { get; set; } = WatchMode.OnChange;
    public int IntervalMinutes { get; set; } = 30;
}
```

```csharp
// FileExplorer/Services/WatchRuleStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza delle regole watch-folder (JSON in AppData, pattern
/// <see cref="CopyJournalStore"/>): scrittura atomica e salvataggi serializzati.
/// Le regole senza sorgente o destinazione non vengono persistite.
/// </summary>
public static class WatchRuleStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    internal const int MinIntervalMinutes = 1;
    internal const int MaxIntervalMinutes = 1440;

    /// <summary>Percorso predefinito del file regole.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "watch-rules.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>
    /// Carica le regole in modo sincrono. Solo per l'avvio dell'app
    /// (pattern <see cref="AppSettingsStore.LoadCurrent"/>): evita sync-over-async
    /// prima che il dispatcher sia attivo.
    /// </summary>
    public static List<WatchRule> Load()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<WatchRule>();

            string json = File.ReadAllText(CurrentPath);
            var rules = JsonSerializer.Deserialize<List<WatchRule>>(json, Options) ?? new List<WatchRule>();
            return Sanitize(rules);
        }
        catch (Exception)
        {
            return new List<WatchRule>();
        }
    }

    /// <summary>Carica le regole; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<WatchRule>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<WatchRule>();

            await using var stream = File.OpenRead(CurrentPath);
            var rules = await JsonSerializer.DeserializeAsync<List<WatchRule>>(stream, Options).ConfigureAwait(false)
                        ?? new List<WatchRule>();
            return Sanitize(rules);
        }
        catch (Exception)
        {
            return new List<WatchRule>();
        }
    }

    /// <summary>Salva l'intera lista (atomico: tmp + move).</summary>
    public static async Task SaveAsync(IReadOnlyList<WatchRule> rules)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<WatchRule> sanitized = Sanitize(rules);

            string? directory = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = CurrentPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, sanitized, Options).ConfigureAwait(false);
            }

            File.Move(tempPath, CurrentPath, overwrite: true);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Normalizza: clamp dell'intervallo, id garantito, scarto delle regole senza percorsi.</summary>
    internal static List<WatchRule> Sanitize(IEnumerable<WatchRule> rules)
    {
        var result = new List<WatchRule>();
        foreach (WatchRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.SourcePath) || string.IsNullOrWhiteSpace(rule.DestinationPath))
                continue;

            rule.IntervalMinutes = Math.Clamp(rule.IntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes);
            if (string.IsNullOrWhiteSpace(rule.Id))
                rule.Id = Guid.NewGuid().ToString("N");
            result.Add(rule);
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter WatchRuleStoreTests`
Expected: PASS (7 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Models/WatchRule.cs FileExplorer/Services/WatchRuleStore.cs FileExplorer.Tests/WatchRuleStoreTests.cs
git commit -m "feat(watch): modello WatchRule e store persistente watch-rules.json"
```

---

### Task 9: `WatchFolderService` (runner per regola: debounce, intervallo, sync incrementale)

**Model:** opus

**Files:**
- Create: `FileExplorer/Services/WatchFolderService.cs`
- Test: `FileExplorer.Tests/WatchFolderServiceTests.cs`

**Interfaces:**
- Consumes: `WatchRule`/`WatchMode` (Task 8); `FileCopyService.CopyDirectoryAsync(string, string, int, Action<CopyProgress>?, CancellationToken, int, bool)`; `DiskTypeService.GetDiskTypeAsync(string?, CancellationToken)`; `CopyParallelismResolver.Resolve(AppSettings, DiskType, DiskType)`; `AppSettingsStore.Current`.
- Produces:
  - `public sealed record WatchStatus(string RuleId, bool IsRunning, DateTime? LastRunUtc, string Message)`
  - `public static event Action<WatchStatus>? WatchFolderService.StatusChanged`
  - `public static void WatchFolderService.Start(WatchRule rule)` (idempotente per Id; non parte se la sorgente non esiste)
  - `public static void WatchFolderService.Stop(string ruleId)` / `public static void StopAll()`
  - `public static Task WatchFolderService.RunNowAsync(WatchRule rule)`
  - `public static IReadOnlyCollection<string> WatchFolderService.ActiveRuleIds`
  - Seam interni per test: `internal static TimeSpan DebounceDelay { get; set; }` (default 3 s), `internal static Func<WatchRule, TimeSpan>? IntervalOverride { get; set; }`, `internal static Func<WatchRule, CancellationToken, Task>? SyncOverride { get; set; }`, `internal static void RaiseStatus(WatchStatus status)`

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/WatchFolderServiceTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class WatchFolderServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _originalDebounce;
    private readonly Func<WatchRule, TimeSpan>? _originalInterval;
    private readonly Func<WatchRule, CancellationToken, Task>? _originalSync;
    private int _syncCount;

    public WatchFolderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _originalDebounce = WatchFolderService.DebounceDelay;
        _originalInterval = WatchFolderService.IntervalOverride;
        _originalSync = WatchFolderService.SyncOverride;

        WatchFolderService.DebounceDelay = TimeSpan.FromMilliseconds(200);
        WatchFolderService.SyncOverride = (_, _) =>
        {
            Interlocked.Increment(ref _syncCount);
            return Task.CompletedTask;
        };
    }

    public void Dispose()
    {
        WatchFolderService.StopAll();
        WatchFolderService.DebounceDelay = _originalDebounce;
        WatchFolderService.IntervalOverride = _originalInterval;
        WatchFolderService.SyncOverride = _originalSync;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private WatchRule CreateRule(WatchMode mode = WatchMode.OnChange)
    {
        string source = Path.Combine(_root, "src");
        string destination = Path.Combine(_root, "dst");
        Directory.CreateDirectory(source);
        return new WatchRule { SourcePath = source, DestinationPath = destination, Mode = mode };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        long start = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - start < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "condizione non raggiunta entro il timeout");
    }

    [Fact]
    public async Task Start_FileCreated_TriggersOneSyncAfterDebounce()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "ciao");

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
        await Task.Delay(600); // finestra di quiete: nessuna sync ulteriore
        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task Start_BurstOfEvents_CoalescesIntoOneSync()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        for (int i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, $"f{i}.txt"), "x");

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
        await Task.Delay(600);
        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task EventsDuringSync_RunSecondSyncAfterwards()
    {
        var firstSyncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        WatchFolderService.SyncOverride = async (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                firstSyncStarted.TrySetResult();
                await releaseFirstSync.Task;
            }
        };

        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "1");
        await firstSyncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Evento mentre la prima sync è in corso → deve accodare una seconda sync.
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "b.txt"), "2");
        await Task.Delay(300); // lascia arrivare l'evento al watcher
        releaseFirstSync.TrySetResult();

        await WaitUntilAsync(() => Volatile.Read(ref count) >= 2);
    }

    [Fact]
    public async Task Stop_PreventsFurtherSyncs()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        WatchFolderService.Stop(rule.Id);

        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "ciao");

        await Task.Delay(600);
        Assert.Equal(0, Volatile.Read(ref _syncCount));
        Assert.Empty(WatchFolderService.ActiveRuleIds);
    }

    [Fact]
    public void Start_SameRuleTwice_KeepsSingleRunner()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        WatchFolderService.Start(rule);

        Assert.Equal(rule.Id, Assert.Single(WatchFolderService.ActiveRuleIds));
    }

    [Fact]
    public void Start_MissingSource_DoesNotStartAndReportsError()
    {
        var statuses = new List<WatchStatus>();
        Action<WatchStatus> handler = status => { lock (statuses) statuses.Add(status); };
        WatchFolderService.StatusChanged += handler;
        try
        {
            var rule = new WatchRule
            {
                SourcePath = Path.Combine(_root, "manca"),
                DestinationPath = Path.Combine(_root, "dst")
            };
            WatchFolderService.Start(rule);

            Assert.Empty(WatchFolderService.ActiveRuleIds);
            lock (statuses)
                Assert.Contains(statuses, s => s.RuleId == rule.Id && s.Message.StartsWith("Sorgente non trovata"));
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    [Fact]
    public async Task RunNowAsync_WithoutRunner_ExecutesOneShot()
    {
        WatchRule rule = CreateRule();

        await WatchFolderService.RunNowAsync(rule);

        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task IntervalMode_RunsRepeatedly()
    {
        WatchFolderService.IntervalOverride = _ => TimeSpan.FromMilliseconds(50);
        WatchRule rule = CreateRule(WatchMode.Interval);
        WatchFolderService.Start(rule);

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 2);
        WatchFolderService.Stop(rule.Id);
    }

    [Fact]
    public async Task RunNowAsync_WithoutOverride_CopiesFiles()
    {
        WatchFolderService.SyncOverride = null; // sync reale
        WatchRule rule = CreateRule();
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "doc.txt"), "contenuto");

        await WatchFolderService.RunNowAsync(rule);

        Assert.Equal("contenuto", await File.ReadAllTextAsync(Path.Combine(rule.DestinationPath, "doc.txt")));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter WatchFolderServiceTests`
Expected: FAIL (tipi `WatchFolderService`/`WatchStatus` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement `WatchFolderService`**

```csharp
// FileExplorer/Services/WatchFolderService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>Stato di una regola watch-folder, notificato via <see cref="WatchFolderService.StatusChanged"/>.</summary>
public sealed record WatchStatus(string RuleId, bool IsRunning, DateTime? LastRunUtc, string Message);

/// <summary>
/// Motore delle regole watch-folder: un runner per regola attiva.
/// OnChange: FileSystemWatcher + debounce con coalescing (una sola sync per raffica
/// di eventi; eventi arrivati durante una sync ne accodano una successiva).
/// Interval: sync ogni <see cref="WatchRule.IntervalMinutes"/> minuti.
/// La sync è <see cref="FileCopyService.CopyDirectoryAsync"/> con skipUnchanged=true
/// (incrementale). Non usa CopyJournalStore: una sync interrotta a metà viene
/// completata dalla successiva grazie al confronto dimensione+mtime.
/// </summary>
public static class WatchFolderService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RuleRunner> Runners = new();

    /// <summary>
    /// Notifica di stato. Invocato su thread di background: i ViewModel assegnano
    /// proprietà reactive direttamente, come per i callback di progresso della copia.
    /// </summary>
    public static event Action<WatchStatus>? StatusChanged;

    /// <summary>Finestra di quiete dopo l'ultimo evento prima di sincronizzare. Ridotta nei test.</summary>
    internal static TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Override dell'intervallo (test). Default: IntervalMinutes della regola.</summary>
    internal static Func<WatchRule, TimeSpan>? IntervalOverride { get; set; }

    /// <summary>Override della sync (test). Default: <see cref="DefaultSyncAsync"/>.</summary>
    internal static Func<WatchRule, CancellationToken, Task>? SyncOverride { get; set; }

    /// <summary>Id delle regole con runner attivo.</summary>
    public static IReadOnlyCollection<string> ActiveRuleIds
    {
        get
        {
            lock (Gate)
                return Runners.Keys.ToList();
        }
    }

    /// <summary>
    /// Avvia (o riavvia) il runner della regola. Idempotente per Id.
    /// Limite dichiarato: se la sorgente non esiste il runner non parte
    /// (nessun retry automatico); viene emesso uno stato di errore.
    /// </summary>
    public static void Start(WatchRule rule)
    {
        Stop(rule.Id);

        if (!Directory.Exists(rule.SourcePath))
        {
            RaiseStatus(new WatchStatus(rule.Id, false, null, $"Sorgente non trovata: {rule.SourcePath}"));
            return;
        }

        var runner = new RuleRunner(rule);
        lock (Gate)
            Runners[rule.Id] = runner;
        runner.Start();
    }

    /// <summary>Ferma il runner della regola (no-op se assente).</summary>
    public static void Stop(string ruleId)
    {
        RuleRunner? runner;
        lock (Gate)
            Runners.Remove(ruleId, out runner);
        runner?.Dispose();
    }

    /// <summary>Ferma tutti i runner (test e chiusure future).</summary>
    public static void StopAll()
    {
        List<RuleRunner> toStop;
        lock (Gate)
        {
            toStop = Runners.Values.ToList();
            Runners.Clear();
        }

        foreach (RuleRunner runner in toStop)
            runner.Dispose();
    }

    /// <summary>Esegue subito una sync: tramite il runner se attivo (serializzata), altrimenti one-shot.</summary>
    public static async Task RunNowAsync(WatchRule rule)
    {
        RuleRunner? runner;
        lock (Gate)
            Runners.TryGetValue(rule.Id, out runner);

        if (runner is not null)
        {
            await runner.RunOnceAsync().ConfigureAwait(false);
            return;
        }

        await SyncWithStatusAsync(rule, lastRunUtc: null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Sync reale: copia incrementale directory → directory con parallelismo adattivo.</summary>
    internal static async Task DefaultSyncAsync(WatchRule rule, CancellationToken ct)
    {
        DiskType sourceType = await DiskTypeService.GetDiskTypeAsync(rule.SourcePath, ct).ConfigureAwait(false);
        DiskType destinationType = await DiskTypeService.GetDiskTypeAsync(rule.DestinationPath, ct).ConfigureAwait(false);
        int parallelism = CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType);

        await FileCopyService.CopyDirectoryAsync(
            rule.SourcePath,
            rule.DestinationPath,
            parallelism,
            onProgress: null,
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: true).ConfigureAwait(false);
    }

    /// <summary>Emette un cambio di stato (interno: usato anche dai test dei ViewModel).</summary>
    internal static void RaiseStatus(WatchStatus status) => StatusChanged?.Invoke(status);

    /// <summary>
    /// Esegue una sync emettendo gli stati prima/dopo. Ritorna il nuovo LastRunUtc.
    /// Le eccezioni (tranne la cancellazione) diventano uno stato di errore: mai
    /// propagate fuori dai loop dei runner.
    /// </summary>
    private static async Task<DateTime?> SyncWithStatusAsync(WatchRule rule, DateTime? lastRunUtc, CancellationToken ct)
    {
        RaiseStatus(new WatchStatus(rule.Id, true, lastRunUtc, "Sincronizzazione…"));
        try
        {
            Func<WatchRule, CancellationToken, Task> sync = SyncOverride ?? DefaultSyncAsync;
            await sync(rule, ct).ConfigureAwait(false);
            DateTime completed = DateTime.UtcNow;
            RaiseStatus(new WatchStatus(rule.Id, false, completed, $"Completata alle {completed.ToLocalTime():HH:mm:ss}"));
            return completed;
        }
        catch (OperationCanceledException)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, "Interrotta"));
            throw;
        }
        catch (Exception ex)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, $"Errore: {ex.Message}"));
            return lastRunUtc;
        }
    }

    /// <summary>Runner di una singola regola: watcher/loop dedicati e CTS proprio.</summary>
    private sealed class RuleRunner : IDisposable
    {
        private readonly WatchRule _rule;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _wake = new(0, 1);
        private readonly SemaphoreSlim _syncGate = new(1, 1);
        private FileSystemWatcher? _watcher;
        private int _dirty;
        private DateTime? _lastRunUtc;

        public RuleRunner(WatchRule rule) => _rule = rule;

        public void Start()
        {
            if (_rule.Mode == WatchMode.OnChange)
            {
                _watcher = new FileSystemWatcher(_rule.SourcePath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Created += (_, _) => Signal();
                _watcher.Changed += (_, _) => Signal();
                _watcher.Renamed += (_, _) => Signal();
                _watcher.Deleted += (_, _) => Signal();
                _watcher.Error += (_, e) =>
                    RaiseStatus(new WatchStatus(_rule.Id, false, _lastRunUtc, $"Errore watcher: {e.GetException().Message}"));
                _watcher.EnableRaisingEvents = true;

                _ = Task.Run(() => LoopOnChangeAsync(_cts.Token));
            }
            else
            {
                _ = Task.Run(() => LoopIntervalAsync(_cts.Token));
            }
        }

        /// <summary>Sync manuale, serializzata con quelle del loop tramite <see cref="_syncGate"/>.</summary>
        public Task RunOnceAsync() => RunSyncAsync(_cts.Token);

        private void Signal()
        {
            // Coalescing: un solo release pendente per qualsiasi numero di eventi.
            if (Interlocked.Exchange(ref _dirty, 1) == 0)
            {
                try
                {
                    _wake.Release();
                }
                catch (SemaphoreFullException)
                {
                    // già segnalato
                }
            }
        }

        private async Task LoopOnChangeAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await _wake.WaitAsync(ct).ConfigureAwait(false);

                    // Debounce: attende una finestra di quiete coalescendo gli eventi.
                    do
                    {
                        Interlocked.Exchange(ref _dirty, 0);
                        await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
                    }
                    while (Volatile.Read(ref _dirty) == 1);

                    // Consuma l'eventuale release residuo maturato durante il debounce.
                    while (_wake.CurrentCount > 0)
                        await _wake.WaitAsync(ct).ConfigureAwait(false);

                    await RunSyncAsync(ct).ConfigureAwait(false);
                    // Eventi arrivati durante la sync hanno rimesso _dirty/_wake:
                    // il giro successivo riparte da WaitAsync e riesegue.
                }
            }
            catch (OperationCanceledException)
            {
                // stop richiesto
            }
        }

        private async Task LoopIntervalAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    TimeSpan interval = IntervalOverride?.Invoke(_rule)
                                        ?? TimeSpan.FromMinutes(Math.Clamp(
                                            _rule.IntervalMinutes,
                                            WatchRuleStore.MinIntervalMinutes,
                                            WatchRuleStore.MaxIntervalMinutes));
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    await RunSyncAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // stop richiesto
            }
        }

        private async Task RunSyncAsync(CancellationToken ct)
        {
            await _syncGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _lastRunUtc = await SyncWithStatusAsync(_rule, _lastRunUtc, ct).ConfigureAwait(false);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _watcher?.Dispose();
            // Il CTS non viene disposto qui: loop e sync in volo potrebbero ancora
            // osservare il token. Cancellato resta innocuo; lo raccoglie il GC.
        }
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter WatchFolderServiceTests`
Expected: PASS (9 test).

- [ ] **Step 5: Run full suite (regressioni)**

Run: `dotnet test`
Expected: PASS (tutti i test, nessuna regressione).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Services/WatchFolderService.cs FileExplorer.Tests/WatchFolderServiceTests.cs
git commit -m "feat(watch): WatchFolderService con debounce, intervallo e sync incrementale"
```

---

### Task 10: `WatchFoldersViewModel` + `WatchRuleViewModel`

**Model:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/WatchRuleViewModel.cs`
- Create: `FileExplorer/ViewModels/WatchFoldersViewModel.cs`
- Test: `FileExplorer.Tests/WatchFoldersViewModelTests.cs`

**Interfaces:**
- Consumes: `WatchRule`/`WatchMode`/`WatchRuleStore` (Task 8); `WatchFolderService.Start/Stop/RunNowAsync/StatusChanged/RaiseStatus`, `WatchStatus` (Task 9); `ConfirmDialogHelper.ShowAsync(string, string, string)`; `SelectPathDialogHelper.ShowAsync(bool, string?)`.
- Produces:
  - `public class WatchRuleViewModel : ReactiveObject` — ctor `(WatchRule model, WatchFoldersViewModel? owner)`; proprietà `Model : WatchRule`, `Owner : WatchFoldersViewModel?`, `SourcePath`/`DestinationPath : string`, `Enabled : bool`, `IsOnChange`/`IsInterval : bool` (adapter radio), `IntervalMinutes : int` (clamp 1..1440), `StatusText`/`LastRunText : string?`
  - `public class WatchFoldersViewModel : ViewModelBase, IDisposable` — `Rules : ObservableCollection<WatchRuleViewModel>`, `HasRules : bool`, `RulesLoad : Task`, `internal LastSaveTask : Task?`, `internal ManageRunners : bool = true`, comandi `AddRuleCommand`, `RemoveRuleCommand`, `BrowseSourceCommand`, `BrowseDestinationCommand`, `RunNowCommand`, `internal void OnRuleChanged(WatchRuleViewModel)`

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/WatchFoldersViewModelTests.cs
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class WatchFoldersViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalStorePath;
    private readonly Func<string, string, string, Task<bool>>? _originalConfirm;

    public WatchFoldersViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watchvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalStorePath = WatchRuleStore.CurrentPath;
        WatchRuleStore.CurrentPath = Path.Combine(_root, "watch-rules.json");
        _originalConfirm = ConfirmDialogHelper.Override;
    }

    public void Dispose()
    {
        ConfirmDialogHelper.Override = _originalConfirm;
        WatchRuleStore.CurrentPath = _originalStorePath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static WatchFoldersViewModel CreateVm() => new() { ManageRunners = false };

    private static async Task<WatchRuleViewModel> AddCompleteRuleAsync(WatchFoldersViewModel vm)
    {
        vm.AddRule();
        WatchRuleViewModel rule = vm.Rules[^1];
        rule.SourcePath = "/tmp/src";
        rule.DestinationPath = "/tmp/dst";
        await vm.LastSaveTask!;
        return rule;
    }

    [Fact]
    public async Task AddRule_AddsRowWithoutSaving()
    {
        var vm = CreateVm();
        await vm.RulesLoad;

        vm.AddRule();

        Assert.Single(vm.Rules);
        Assert.True(vm.HasRules);
        Assert.Null(vm.LastSaveTask); // regola vuota: verrebbe scartata dal Sanitize
    }

    [Fact]
    public async Task RuleChange_PersistsToStore()
    {
        var vm = CreateVm();
        await vm.RulesLoad;

        await AddCompleteRuleAsync(vm);

        var loaded = await WatchRuleStore.LoadAsync();
        WatchRule single = Assert.Single(loaded);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("/tmp/dst", single.DestinationPath);
    }

    [Fact]
    public async Task RemoveRule_Confirmed_RemovesAndPersists()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(true);
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);

        await vm.RemoveRuleAsync(rule);
        await vm.LastSaveTask!;

        Assert.Empty(vm.Rules);
        Assert.False(vm.HasRules);
        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task RemoveRule_Declined_KeepsRule()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);

        await vm.RemoveRuleAsync(rule);

        Assert.Single(vm.Rules);
        Assert.Single(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task Ctor_LoadsExistingRules()
    {
        await WatchRuleStore.SaveAsync(new[]
        {
            new WatchRule { SourcePath = "/tmp/src", DestinationPath = "/tmp/dst", Enabled = false }
        });

        var vm = CreateVm();
        await vm.RulesLoad;

        WatchRuleViewModel single = Assert.Single(vm.Rules);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("Disattivata", single.StatusText);
    }

    [Fact]
    public async Task StatusChanged_UpdatesMatchingRow()
    {
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);
        var lastRun = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

        WatchFolderService.RaiseStatus(new WatchStatus(rule.Model.Id, false, lastRun, "Completata alle 12:00:00"));

        Assert.Equal("Completata alle 12:00:00", rule.StatusText);
        Assert.NotNull(rule.LastRunText);
        Assert.StartsWith("Ultima sync:", rule.LastRunText);
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);
        string? before = rule.StatusText;

        vm.Dispose();
        WatchFolderService.RaiseStatus(new WatchStatus(rule.Model.Id, true, null, "Sincronizzazione…"));

        Assert.Equal(before, rule.StatusText);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter WatchFoldersViewModelTests`
Expected: FAIL (tipi `WatchFoldersViewModel`/`WatchRuleViewModel` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement `WatchRuleViewModel`**

```csharp
// FileExplorer/ViewModels/WatchRuleViewModel.cs
using System;

using FileExplorer.Models;
using FileExplorer.Services;

using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Riga reattiva di una regola watch-folder; inoltra ogni modifica al parent.</summary>
public class WatchRuleViewModel : ReactiveObject
{
    private string? _statusText = "In attesa";
    private string? _lastRunText;

    public WatchRuleViewModel(WatchRule model, WatchFoldersViewModel? owner)
    {
        Model = model;
        Owner = owner;
    }

    /// <summary>Modello persistito sottostante.</summary>
    public WatchRule Model { get; }

    /// <summary>ViewModel della scheda; null solo nei test di unità della riga.</summary>
    public WatchFoldersViewModel? Owner { get; }

    public string SourcePath
    {
        get => Model.SourcePath;
        set
        {
            if (Model.SourcePath == value)
                return;
            Model.SourcePath = value;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public string DestinationPath
    {
        get => Model.DestinationPath;
        set
        {
            if (Model.DestinationPath == value)
                return;
            Model.DestinationPath = value;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value)
                return;
            Model.Enabled = value;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    /// <summary>Adapter radio: agisce solo su true (pattern IsTheme* di SettingsViewModel).</summary>
    public bool IsOnChange
    {
        get => Model.Mode == WatchMode.OnChange;
        set
        {
            if (!value || Model.Mode == WatchMode.OnChange)
                return;
            Model.Mode = WatchMode.OnChange;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsInterval));
            Owner?.OnRuleChanged(this);
        }
    }

    /// <summary>Adapter radio: agisce solo su true.</summary>
    public bool IsInterval
    {
        get => Model.Mode == WatchMode.Interval;
        set
        {
            if (!value || Model.Mode == WatchMode.Interval)
                return;
            Model.Mode = WatchMode.Interval;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsOnChange));
            Owner?.OnRuleChanged(this);
        }
    }

    public int IntervalMinutes
    {
        get => Model.IntervalMinutes;
        set
        {
            int clamped = Math.Clamp(value, WatchRuleStore.MinIntervalMinutes, WatchRuleStore.MaxIntervalMinutes);
            if (Model.IntervalMinutes == clamped)
                return;
            Model.IntervalMinutes = clamped;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public string? StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string? LastRunText
    {
        get => _lastRunText;
        set => this.RaiseAndSetIfChanged(ref _lastRunText, value);
    }
}
```

- [ ] **Step 4: Implement `WatchFoldersViewModel`**

```csharp
// FileExplorer/ViewModels/WatchFoldersViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

using FileExplorer.Models;
using FileExplorer.Services;

using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Sync auto": gestione delle regole watch-folder. Persiste su
/// <see cref="WatchRuleStore"/> a ogni modifica e riallinea i runner di
/// <see cref="WatchFolderService"/>. I runner iniziali sono avviati da App.
/// </summary>
public class WatchFoldersViewModel : ViewModelBase, IDisposable
{
    private readonly Action<WatchStatus> _statusHandler;

    public WatchFoldersViewModel()
    {
        AddRuleCommand = ReactiveCommand.Create(AddRule);
        RemoveRuleCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(RemoveRuleAsync);
        BrowseSourceCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(BrowseDestinationAsync);
        RunNowCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(RunNowAsync);

        _statusHandler = OnStatusChanged;
        WatchFolderService.StatusChanged += _statusHandler;

        RulesLoad = LoadRulesAsync();
    }

    public ObservableCollection<WatchRuleViewModel> Rules { get; } = new();

    public bool HasRules => Rules.Count > 0;

    /// <summary>Caricamento iniziale; attendibile nei test (pattern JournalRestore).</summary>
    public Task RulesLoad { get; }

    /// <summary>Ultimo salvataggio best-effort; attendibile nei test.</summary>
    internal Task? LastSaveTask { get; private set; }

    /// <summary>False nei test headless: nessun runner reale (pattern ApplyThemesToApplication).</summary>
    internal bool ManageRunners { get; set; } = true;

    public ReactiveCommand<Unit, Unit> AddRuleCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> RemoveRuleCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> BrowseSourceCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> BrowseDestinationCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> RunNowCommand { get; }

    /// <summary>Pubblico per i test.</summary>
    public void AddRule()
    {
        Rules.Add(new WatchRuleViewModel(new WatchRule(), this));
        this.RaisePropertyChanged(nameof(HasRules));
        // Nessun salvataggio: una regola senza percorsi verrebbe scartata dal Sanitize dello store.
    }

    /// <summary>Pubblico per i test.</summary>
    public async Task RemoveRuleAsync(WatchRuleViewModel rule)
    {
        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Rimuovere la regola?",
            $"La sincronizzazione automatica {rule.SourcePath} → {rule.DestinationPath} verrà rimossa.",
            "Rimuovi");
        if (!confirmed)
            return;

        if (ManageRunners)
            WatchFolderService.Stop(rule.Model.Id);
        Rules.Remove(rule);
        this.RaisePropertyChanged(nameof(HasRules));
        SaveRules();
    }

    private async Task BrowseSourceAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.SourcePath);
        if (selected is not null)
            rule.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.DestinationPath);
        if (selected is not null)
            rule.DestinationPath = selected;
    }

    /// <summary>Pubblico per i test.</summary>
    public async Task RunNowAsync(WatchRuleViewModel rule)
    {
        try
        {
            await WatchFolderService.RunNowAsync(rule.Model);
        }
        catch (OperationCanceledException)
        {
            // runner fermato durante l'esecuzione manuale: lo stato lo segnala già
        }
    }

    /// <summary>Chiamato dalle righe a ogni modifica: persiste e riallinea il runner.</summary>
    internal void OnRuleChanged(WatchRuleViewModel rule)
    {
        SaveRules();

        if (!ManageRunners)
            return;

        WatchFolderService.Stop(rule.Model.Id);
        if (rule.Model.Enabled
            && !string.IsNullOrWhiteSpace(rule.Model.SourcePath)
            && !string.IsNullOrWhiteSpace(rule.Model.DestinationPath))
        {
            WatchFolderService.Start(rule.Model);
        }
    }

    private void SaveRules()
    {
        List<WatchRule> models = Rules.Select(r => r.Model).ToList();
        LastSaveTask = SaveRulesAsync(models);
    }

    private static async Task SaveRulesAsync(IReadOnlyList<WatchRule> rules)
    {
        try
        {
            await WatchRuleStore.SaveAsync(rules);
        }
        catch (Exception)
        {
            // best effort: la UI non deve rompersi se il disco non è scrivibile
        }
    }

    private async Task LoadRulesAsync()
    {
        List<WatchRule> rules = await WatchRuleStore.LoadAsync();
        foreach (WatchRule rule in rules)
        {
            Rules.Add(new WatchRuleViewModel(rule, this)
            {
                StatusText = rule.Enabled ? "In attesa" : "Disattivata"
            });
        }

        this.RaisePropertyChanged(nameof(HasRules));
        // I runner delle regole attive sono già stati avviati da App all'apertura.
    }

    private void OnStatusChanged(WatchStatus status)
    {
        // Thread di background: assegnazioni dirette come per i progressi di copia.
        WatchRuleViewModel? row = Rules.FirstOrDefault(r => r.Model.Id == status.RuleId);
        if (row is null)
            return;

        row.StatusText = status.Message;
        if (status.LastRunUtc is { } lastRun)
            row.LastRunText = $"Ultima sync: {lastRun.ToLocalTime():HH:mm:ss}";
    }

    public void Dispose() => WatchFolderService.StatusChanged -= _statusHandler;
}
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test --filter WatchFoldersViewModelTests`
Expected: PASS (7 test).

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/ViewModels/WatchRuleViewModel.cs FileExplorer/ViewModels/WatchFoldersViewModel.cs FileExplorer.Tests/WatchFoldersViewModelTests.cs
git commit -m "feat(watch): WatchFoldersViewModel con persistenza e riallineamento runner"
```

---

### Task 11: `WatchFoldersView`, tab "Sync auto", avvio runner in App, docs e PR

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Views/WatchFoldersView.axaml`
- Create: `FileExplorer/Views/WatchFoldersView.axaml.cs`
- Modify: `FileExplorer/Views/MainWindow.axaml` (nuova TabItem dopo "Confronto")
- Modify: `FileExplorer/App.axaml.cs` (avvio runner regole attive)
- Modify: `IDEE.md` (punto 8 → `[x]`)
- Modify: `CLAUDE.md` (riga su tab Sync auto)
- Modify: `docs/superpowers/plans/<questo piano>.md` (spuntare i task della fase)

**Interfaces:**
- Consumes: `WatchFoldersViewModel` (Task 10); `WatchRuleStore.Load()` (Task 8); `WatchFolderService.Start(WatchRule)` (Task 9).
- Produces: nessuna nuova API (solo UI + avvio).

- [ ] **Step 1: Create `WatchFoldersView.axaml`**

```xml
<!-- FileExplorer/Views/WatchFoldersView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             x:Class="FileExplorer.Views.WatchFoldersView">

  <DockPanel>

    <!-- Header con gradiente -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Brush.AccentGradient}" Padding="20,14">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
          <i:Icon Value="fa-solid fa-rotate" FontSize="20" Foreground="{DynamicResource Brush.OnAccent}" />
          <TextBlock Text="Sync auto" FontSize="18" FontWeight="Bold" Foreground="{DynamicResource Brush.OnAccent}" VerticalAlignment="Center" />
        </StackPanel>
        <Button Grid.Column="1" Classes="onaccent" Command="{Binding AddRuleCommand}">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plus" />
            <TextBlock Text="Aggiungi regola" />
          </StackPanel>
        </Button>
      </Grid>
    </Border>

    <Panel Background="{DynamicResource Brush.Surface}">

      <!-- Empty state -->
      <StackPanel IsVisible="{Binding !HasRules}" VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="12">
        <i:Icon Value="fa-solid fa-rotate" FontSize="52" Foreground="{DynamicResource Brush.TextMuted}" HorizontalAlignment="Center" />
        <TextBlock Text="Nessuna regola di sincronizzazione automatica"
                   FontSize="16"
                   Foreground="{DynamicResource Brush.TextMuted}"
                   HorizontalAlignment="Center" />
        <Button Classes="primary" Command="{Binding AddRuleCommand}" HorizontalAlignment="Center">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <i:Icon Value="fa-solid fa-plus" />
            <TextBlock Text="Aggiungi la prima regola" />
          </StackPanel>
        </Button>
      </StackPanel>

      <!-- Lista card -->
      <ScrollViewer IsVisible="{Binding HasRules}">
        <ItemsControl ItemsSource="{Binding Rules}" Margin="20,12">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Border Classes="card">
                <StackPanel Spacing="8">

                  <!-- Sorgente -->
                  <Grid ColumnDefinitions="Auto,*,Auto">
                    <i:Icon Grid.Column="0" Value="fa-regular fa-folder-open" Width="26"
                            Foreground="{DynamicResource Brush.TextMuted}" VerticalAlignment="Center" />
                    <TextBox Grid.Column="1" Text="{Binding SourcePath}" IsReadOnly="True"
                             Watermark="Cartella da monitorare…" Margin="8,0" />
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

                  <!-- Modalità + attivazione -->
                  <Grid ColumnDefinitions="*,Auto" Margin="0,4,0,0">
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="16" VerticalAlignment="Center">
                      <RadioButton Content="Al cambiamento" IsChecked="{Binding IsOnChange}" />
                      <RadioButton Content="Ogni" IsChecked="{Binding IsInterval}" />
                      <NumericUpDown Width="120" Minimum="1" Maximum="1440" Increment="5"
                                     Value="{Binding IntervalMinutes}" IsEnabled="{Binding IsInterval}" />
                      <TextBlock Text="minuti" VerticalAlignment="Center"
                                 Foreground="{DynamicResource Brush.TextMuted}" />
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                      <TextBlock Text="Attiva" VerticalAlignment="Center"
                                 Foreground="{DynamicResource Brush.TextPrimary}" />
                      <ToggleSwitch IsChecked="{Binding Enabled}" />
                    </StackPanel>
                  </Grid>

                  <!-- Stato + comandi -->
                  <Grid ColumnDefinitions="*,Auto" Margin="0,4,0,0">
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
                      <Border Classes="badge">
                        <TextBlock Text="{Binding StatusText}" />
                      </Border>
                      <TextBlock Text="{Binding LastRunText}" VerticalAlignment="Center"
                                 Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                      <Button Classes="secondary" Content="Esegui ora"
                              Command="{Binding DataContext.RunNowCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                              CommandParameter="{Binding}" />
                      <Button Classes="iconbtn"
                              i:Attached.Icon="fa-solid fa-trash"
                              ToolTip.Tip="Rimuovi regola"
                              Command="{Binding DataContext.RemoveRuleCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                              CommandParameter="{Binding}" />
                    </StackPanel>
                  </Grid>

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

```csharp
// FileExplorer/Views/WatchFoldersView.axaml.cs
using Avalonia.Controls;

using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class WatchFoldersView : UserControl
{
    public WatchFoldersView()
    {
        InitializeComponent();
        // Pattern del progetto: la tab crea il proprio ViewModel. Come le altre
        // view non dispone il VM IDisposable (la tab vive quanto la finestra).
        DataContext = new WatchFoldersViewModel();
    }
}
```

- [ ] **Step 2: Add the tab in `MainWindow.axaml`**

Inserire dopo la `TabItem` "Confronto" (riga 38, dopo `</TabItem>` di `views:ComparisonView`) e prima di quella "Duplicati":

```xml
    <TabItem>
      <TabItem.Header>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <i:Icon Value="fa-solid fa-rotate" />
          <TextBlock Text="Sync auto" />
        </StackPanel>
      </TabItem.Header>
      <views:WatchFoldersView />
    </TabItem>
```

- [ ] **Step 3: Start runners in `App.axaml.cs`**

Contenuto completo del file dopo la modifica (aggiunta del blocco `foreach` dopo il tema, prima di `desktop.MainWindow`):

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FileExplorer.Models;
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
            AppSettingsStore.LoadCurrent();
            ColorTheme? customTheme = AppSettingsStore.Current.CustomThemeId is { } themeId
                ? ThemeStore.Load(themeId)
                : null;
            if (customTheme is not null)
                ThemeService.Apply(customTheme);
            else
                RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);

            // Avvia i runner watch-folder delle regole attive. Nessun handler di
            // shutdown nell'app: i runner muoiono col processo (limite dichiarato).
            foreach (WatchRule rule in WatchRuleStore.Load())
            {
                if (rule.Enabled)
                    WatchFolderService.Start(rule);
            }

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

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build FileExplorer.sln && dotnet test`
Expected: build OK, PASS (tutti i test).

- [ ] **Step 5: Smoke test manuale**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project FileExplorer.Desktop`
Verifiche a mano (il sandbox non ha il runtime .NET 8: serve il roll-forward):
1. La tab "Sync auto" compare con icona rotate ed empty state.
2. "Aggiungi regola" → card con sorgente/destinazione, radio modalità, toggle Attiva.
3. Compilati i percorsi con due cartelle di prova, creare un file nella sorgente → dopo ~3 s lo stato passa a "Sincronizzazione…" poi "Completata alle HH:mm:ss" e il file appare nella destinazione.
4. "Esegui ora" forza una sync immediata.
5. Riavviare l'app → la regola ricompare e la sync automatica riparte (file nuovo nella sorgente → copiato).

- [ ] **Step 6: Update docs (IDEE, CLAUDE.md, piano)**

In `IDEE.md`, riga del punto 8, sostituire:

```markdown
8. `[ ]` **Copia programmata / watch-folder** — monitor di una cartella (FileSystemWatcher) con sincronizzazione automatica verso la destinazione al cambiamento, o a orario fisso. È il cuore di GoodSync/SyncBackPro (a pagamento). *(A)*
```

con:

```markdown
8. `[x]` **Copia programmata / watch-folder** — monitor di una cartella (FileSystemWatcher) con sincronizzazione automatica verso la destinazione al cambiamento, o a intervallo di minuti (l'orario fisso resta da fare). È il cuore di GoodSync/SyncBackPro (a pagamento). *(A)*
```

In `CLAUDE.md`, sezione "Project", dopo il paragrafo "Layering: …There is no DI container.", aggiungere la riga:

```markdown
Watch-folder: la tab "Sync auto" (`WatchFoldersView`) gestisce regole di sincronizzazione automatica (`WatchRule`/`WatchRuleStore`); i runner (`WatchFolderService`) delle regole attive partono in `App.OnFrameworkInitializationCompleted` e muoiono col processo (nessun handler di shutdown).
```

Spuntare i checkbox dei Task 8–11 in questo piano.

- [ ] **Step 7: Commit, push e PR**

```bash
git add FileExplorer/Views/WatchFoldersView.axaml FileExplorer/Views/WatchFoldersView.axaml.cs FileExplorer/Views/MainWindow.axaml FileExplorer/App.axaml.cs IDEE.md CLAUDE.md docs/superpowers/plans/
git commit -m "feat(watch): tab Sync auto con avvio runner all'apertura"
git push -u origin feature/watch-folders
gh pr create --title "Watch-folder: sincronizzazione automatica (IDEE 8)" --body "Regole di sync automatica sorgente→destinazione: al cambiamento (FileSystemWatcher + debounce 3s con coalescing) o ogni N minuti. Sync incrementale via CopyDirectoryAsync skipUnchanged. Nuova tab Sync auto; regole persistite in watch-rules.json; runner avviati all'apertura dell'app. Limiti documentati: niente orario fisso cron, niente retry se la sorgente manca allo start, nessun journal per le sync automatiche."
```

---
## Fase 5 — Sync bidirezionale con rilevamento conflitti (IDEE punto 10) — branch `feature/bidirectional-sync`

**Scope MVP.** Nessuna propagazione delle eliminazioni: un file mancante da un lato viene ricopiato dall'altro, mai cancellato (zero operazioni distruttive). Conflitto = file modificato/creato da entrambi i lati con contenuto potenzialmente diverso; la risoluzione (Sinistra / Destra / Salta) spetta all'utente. Rilevamento modifiche rispetto alla baseline: dimensione diversa **oppure** |Δ LastWriteTimeUtc| ≥ 2 s (stessa tolleranza di `FileCopyService.IsUnchanged`). Prima sync (nessuna baseline): file presente su entrambi i lati e diverso (size diversa, oppure size uguale ma mtime oltre tolleranza) → conflitto `BothCreated`; presente su un solo lato → copia verso il lato mancante. La UI vive nella tab Confronto come terza card, riusando `LeftPath`/`RightPath` esistenti.

**Dipendenza.** Richiede la Fase 2 (`feature/file-byte-compare`) già mergiata su `main`: `ComparisonViewModel`/`ComparisonView` conterranno già la card del confronto binario. Creare il branch da `main` **dopo** quel merge. I blocchi di codice dei Modify qui sotto mostrano l'edit sullo stato **attuale** del repo: dopo il merge della Fase 2 gli anchor potrebbero spostarsi — risolvere mantenendo entrambe le feature (le modifiche toccano punti separati, i conflitti attesi sono banali: using, comandi nel costruttore, `Dispose`, card aggiuntive nello `StackPanel`).

---

### Task 12: `SyncBaseline` + `SyncBaselineStore`

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Models/SyncBaseline.cs`
- Create: `FileExplorer/Services/SyncBaselineStore.cs`
- Test: `FileExplorer.Tests/SyncBaselineStoreTests.cs`

**Interfaces:**
- Consumes: nulla (foglia).
- Produces:
  - `public class SyncEntry { public long Size { get; set; } public DateTime LastWriteUtc { get; set; } }`
  - `public class SyncBaseline { public string LeftRoot { get; set; } public string RightRoot { get; set; } public DateTime LastSyncUtc { get; set; } public Dictionary<string, SyncEntry> Entries { get; set; } }` (chiave = path relativo)
  - `public static string SyncBaselineStore.BaselinesDirectory { get; set; }` (seam di test, pattern `ThemeStore.ThemesDirectory`)
  - `internal static string SyncBaselineStore.PathFor(string leftRoot, string rightRoot)`
  - `public static SyncBaseline? SyncBaselineStore.Load(string leftRoot, string rightRoot)` (sync, tollerante)
  - `public static Task SyncBaselineStore.SaveAsync(SyncBaseline baseline)` (atomico tmp+move)
  - `public static void SyncBaselineStore.Delete(string leftRoot, string rightRoot)` (best effort)

- [ ] **Step 0: Branch da main aggiornato (dopo il merge della Fase 2)**

```bash
git checkout main && git pull
git checkout -b feature/bidirectional-sync
```

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/SyncBaselineStoreTests.cs
using System;
using System.IO;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class SyncBaselineStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fe-syncbase-" + Guid.NewGuid().ToString("N"));
    private readonly string _originalDirectory;

    public SyncBaselineStoreTests()
    {
        Directory.CreateDirectory(_root);
        _originalDirectory = SyncBaselineStore.BaselinesDirectory;
        SyncBaselineStore.BaselinesDirectory = Path.Combine(_root, "baselines");
    }

    public void Dispose()
    {
        SyncBaselineStore.BaselinesDirectory = _originalDirectory;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Save_then_Load_roundtrips()
    {
        var baseline = new SyncBaseline
        {
            LeftRoot = Path.Combine(_root, "left"),
            RightRoot = Path.Combine(_root, "right"),
            LastSyncUtc = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc)
        };
        baseline.Entries["sub/file.txt"] = new SyncEntry { Size = 42, LastWriteUtc = baseline.LastSyncUtc };

        await SyncBaselineStore.SaveAsync(baseline);
        SyncBaseline? loaded = SyncBaselineStore.Load(baseline.LeftRoot, baseline.RightRoot);

        Assert.NotNull(loaded);
        Assert.Equal(42, loaded!.Entries["sub/file.txt"].Size);
        Assert.Equal(baseline.LastSyncUtc, loaded.LastSyncUtc);
        Assert.Equal(baseline.LeftRoot, loaded.LeftRoot);
        Assert.Equal(baseline.RightRoot, loaded.RightRoot);
    }

    [Fact]
    public void PathFor_TrailingSeparator_SameKey()
    {
        string left = Path.Combine(_root, "left");
        string right = Path.Combine(_root, "right");

        Assert.Equal(
            SyncBaselineStore.PathFor(left, right),
            SyncBaselineStore.PathFor(
                left + Path.DirectorySeparatorChar,
                right + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void PathFor_SwappedRoots_DifferentKey()
    {
        string left = Path.Combine(_root, "left");
        string right = Path.Combine(_root, "right");

        Assert.NotEqual(
            SyncBaselineStore.PathFor(left, right),
            SyncBaselineStore.PathFor(right, left));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull()
    {
        string left = Path.Combine(_root, "left");
        string right = Path.Combine(_root, "right");
        Directory.CreateDirectory(SyncBaselineStore.BaselinesDirectory);
        await File.WriteAllTextAsync(SyncBaselineStore.PathFor(left, right), "{ non json");

        Assert.Null(SyncBaselineStore.Load(left, right));
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.Null(SyncBaselineStore.Load(Path.Combine(_root, "a"), Path.Combine(_root, "b")));
    }

    [Fact]
    public async Task Delete_RemovesBaseline()
    {
        var baseline = new SyncBaseline
        {
            LeftRoot = Path.Combine(_root, "l"),
            RightRoot = Path.Combine(_root, "r")
        };
        await SyncBaselineStore.SaveAsync(baseline);

        SyncBaselineStore.Delete(baseline.LeftRoot, baseline.RightRoot);

        Assert.Null(SyncBaselineStore.Load(baseline.LeftRoot, baseline.RightRoot));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter SyncBaselineStoreTests`
Expected: FAIL (tipi `SyncBaseline`/`SyncBaselineStore` non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement model + store**

```csharp
// FileExplorer/Models/SyncBaseline.cs
using System;
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Stato di un file al momento dell'ultima sincronizzazione riuscita.</summary>
public class SyncEntry
{
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
}

/// <summary>
/// Baseline di una coppia di cartelle sincronizzate: per ogni path relativo,
/// dimensione e mtime concordati all'ultima sync. Serve a distinguere
/// "modificato da un solo lato" da "modificato da entrambi" (conflitto).
/// </summary>
public class SyncBaseline
{
    public string LeftRoot { get; set; } = "";
    public string RightRoot { get; set; } = "";
    public DateTime LastSyncUtc { get; set; }
    public Dictionary<string, SyncEntry> Entries { get; set; } = new();
}
```

```csharp
// FileExplorer/Services/SyncBaselineStore.cs
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza delle baseline di sincronizzazione bidirezionale: un file JSON
/// per coppia di cartelle in AppData/FileExplorer/sync-baselines/, nominato con
/// l'hash dei due path normalizzati. Letture tolleranti: file mancante o
/// corrotto → null, mai eccezioni verso la UI.
/// </summary>
public static class SyncBaselineStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Directory dei file baseline. Settabile per i test.</summary>
    public static string BaselinesDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer", "sync-baselines");

    /// <summary>
    /// Path del file baseline per la coppia (left, right). I path vengono
    /// normalizzati (assoluti, senza separatore finale) e, sui filesystem
    /// tipicamente case-insensitive (Windows, macOS), portati a lower-invariant —
    /// stessa logica di DirectoryComparisonService.DefaultPathComparer.
    /// Nome file = primi 32 caratteri dell'SHA-256 esadecimale di "left|right".
    /// </summary>
    internal static string PathFor(string leftRoot, string rightRoot)
    {
        string key = Normalize(leftRoot) + "|" + Normalize(rightRoot);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string name = Convert.ToHexString(hash).ToLowerInvariant()[..32];
        return Path.Combine(BaselinesDirectory, name + ".json");
    }

    private static string Normalize(string root)
    {
        string full = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? full.ToLowerInvariant()
            : full;
    }

    /// <summary>Carica la baseline della coppia, o null se assente/corrotta.</summary>
    public static SyncBaseline? Load(string leftRoot, string rightRoot)
    {
        try
        {
            string path = PathFor(leftRoot, rightRoot);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<SyncBaseline>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Salva la baseline in modo atomico (file temporaneo + move).</summary>
    public static async Task SaveAsync(SyncBaseline baseline)
    {
        Directory.CreateDirectory(BaselinesDirectory);
        string path = PathFor(baseline.LeftRoot, baseline.RightRoot);
        string tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, baseline, Options);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Elimina la baseline della coppia, se esiste. Best effort.</summary>
    public static void Delete(string leftRoot, string rightRoot)
    {
        try
        {
            string path = PathFor(leftRoot, rightRoot);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: una baseline orfana non è un errore per l'utente.
        }
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter SyncBaselineStoreTests`
Expected: PASS (6 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Models/SyncBaseline.cs FileExplorer/Services/SyncBaselineStore.cs FileExplorer.Tests/SyncBaselineStoreTests.cs
git commit -m "feat(sync): SyncBaseline e SyncBaselineStore persistente"
```

---

### Task 13: `BidirectionalSyncService` (pianificazione + applicazione)

**Model:** opus

**Files:**
- Create: `FileExplorer/Services/BidirectionalSyncService.cs`
- Test: `FileExplorer.Tests/BidirectionalSyncServiceTests.cs`

**Interfaces:**
- Consumes: `SyncBaseline`/`SyncEntry` (Task 12), `DirectoryComparisonService.DefaultPathComparer` (internal, stesso assembly), `CompareProgress`, `CopyProgress`, `FileCopyService.CopyFileAsync(string, string, Action<long>?, CancellationToken, int)`.
- Produces:
  - `public enum SyncConflictKind { BothModified, BothCreated }`
  - `public sealed record SyncConflict(string RelativePath, SyncConflictKind Kind);`
  - `public sealed record SyncPlan(IReadOnlyList<string> CopyToRight, IReadOnlyList<string> CopyToLeft, IReadOnlyList<SyncConflict> Conflicts)` con `public int TotalOperations => CopyToRight.Count + CopyToLeft.Count;`
  - `public enum SyncResolution { UseLeft, UseRight, Skip }`
  - `public static Task<SyncPlan> BidirectionalSyncService.PlanAsync(string leftRoot, string rightRoot, SyncBaseline? baseline, Action<CompareProgress>? onProgress, CancellationToken ct)`
  - `public static Task<SyncBaseline> BidirectionalSyncService.ApplyAsync(string leftRoot, string rightRoot, SyncPlan plan, IReadOnlyDictionary<string, SyncResolution> conflictResolutions, Action<CopyProgress>? onProgress, CancellationToken ct)`

Matrice di classificazione di `PlanAsync` (per ogni path relativo dell'unione dei due alberi; "cambiato" = size diversa vs baseline OR |Δmtime| ≥ 2 s; "stesso stato" = size uguale AND |Δmtime| < 2 s):

| In baseline | Sinistra | Destra | Esito |
|---|---|---|---|
| sì | cambiato | invariato | `CopyToRight` |
| sì | invariato | cambiato | `CopyToLeft` |
| sì | cambiato | cambiato | `Conflict(BothModified)` |
| sì | invariato | invariato | niente |
| sì | presente | mancante | `CopyToRight` (mai eliminazioni) |
| sì | mancante | presente | `CopyToLeft` (mai eliminazioni) |
| no | presente, ≠ | presente, ≠ | `Conflict(BothCreated)` |
| no | presente, stesso stato | presente, stesso stato | niente (entrerà in baseline all'Apply) |
| no | presente | assente | `CopyToRight` |
| no | assente | presente | `CopyToLeft` |

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/BidirectionalSyncServiceTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class BidirectionalSyncServiceTests : IDisposable
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, SyncResolution> NoResolutions =
        new Dictionary<string, SyncResolution>();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fe-bidir-" + Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public BidirectionalSyncServiceTests()
    {
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static async Task WriteFileAsync(string root, string relative, string content, DateTime mtimeUtc)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
    }

    private SyncBaseline BaselineWith(params (string Relative, long Size, DateTime Mtime)[] entries)
    {
        var baseline = new SyncBaseline { LeftRoot = _left, RightRoot = _right, LastSyncUtc = BaseTime };
        foreach (var (relative, size, mtime) in entries)
            baseline.Entries[relative] = new SyncEntry { Size = size, LastWriteUtc = mtime };
        return baseline;
    }

    // --- PlanAsync, prima sync (baseline assente) ---

    [Fact]
    public async Task Plan_NoBaseline_OneSideOnly_CopiesTowardMissingSide()
    {
        await WriteFileAsync(_left, "solo-sx.txt", "a", BaseTime);
        await WriteFileAsync(_right, "solo-dx.txt", "b", BaseTime);

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline: null, onProgress: null, CancellationToken.None);

        Assert.Equal(new[] { "solo-sx.txt" }, plan.CopyToRight);
        Assert.Equal(new[] { "solo-dx.txt" }, plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(2, plan.TotalOperations);
    }

    [Fact]
    public async Task Plan_NoBaseline_BothSidesIdentical_NoOperations()
    {
        await WriteFileAsync(_left, "uguale.txt", "stesso", BaseTime);
        await WriteFileAsync(_right, "uguale.txt", "stesso", BaseTime.AddSeconds(1)); // entro tolleranza

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline: null, onProgress: null, CancellationToken.None);

        Assert.Empty(plan.CopyToRight);
        Assert.Empty(plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task Plan_NoBaseline_BothSidesDifferent_BothCreatedConflict()
    {
        await WriteFileAsync(_left, "conflitto.txt", "versione sinistra", BaseTime);
        await WriteFileAsync(_right, "conflitto.txt", "dx", BaseTime);

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline: null, onProgress: null, CancellationToken.None);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("conflitto.txt", conflict.RelativePath);
        Assert.Equal(SyncConflictKind.BothCreated, conflict.Kind);
    }

    [Fact]
    public async Task Plan_NoBaseline_SameSizeDifferentMtime_BothCreatedConflict()
    {
        await WriteFileAsync(_left, "ambiguo.txt", "12345", BaseTime);
        await WriteFileAsync(_right, "ambiguo.txt", "abcde", BaseTime.AddSeconds(10));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline: null, onProgress: null, CancellationToken.None);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(SyncConflictKind.BothCreated, conflict.Kind);
    }

    // --- PlanAsync, con baseline ---

    [Fact]
    public async Task Plan_LeftChangedOnly_CopyToRight()
    {
        await WriteFileAsync(_left, "doc.txt", "nuovo contenuto", BaseTime.AddMinutes(5));
        await WriteFileAsync(_right, "doc.txt", "orig", BaseTime);
        SyncBaseline baseline = BaselineWith(("doc.txt", 4, BaseTime));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline, onProgress: null, CancellationToken.None);

        Assert.Equal(new[] { "doc.txt" }, plan.CopyToRight);
        Assert.Empty(plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task Plan_RightChangedOnly_CopyToLeft()
    {
        await WriteFileAsync(_left, "doc.txt", "orig", BaseTime);
        await WriteFileAsync(_right, "doc.txt", "nuovo contenuto", BaseTime.AddMinutes(5));
        SyncBaseline baseline = BaselineWith(("doc.txt", 4, BaseTime));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline, onProgress: null, CancellationToken.None);

        Assert.Empty(plan.CopyToRight);
        Assert.Equal(new[] { "doc.txt" }, plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task Plan_BothChanged_BothModifiedConflict()
    {
        await WriteFileAsync(_left, "doc.txt", "sinistra v2", BaseTime.AddMinutes(5));
        await WriteFileAsync(_right, "doc.txt", "destra v2 diversa", BaseTime.AddMinutes(7));
        SyncBaseline baseline = BaselineWith(("doc.txt", 4, BaseTime));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline, onProgress: null, CancellationToken.None);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(SyncConflictKind.BothModified, conflict.Kind);
        Assert.Empty(plan.CopyToRight);
        Assert.Empty(plan.CopyToLeft);
    }

    [Fact]
    public async Task Plan_UnchangedBothSides_NoOperations()
    {
        await WriteFileAsync(_left, "doc.txt", "orig", BaseTime);
        await WriteFileAsync(_right, "doc.txt", "orig", BaseTime);
        SyncBaseline baseline = BaselineWith(("doc.txt", 4, BaseTime));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline, onProgress: null, CancellationToken.None);

        Assert.Empty(plan.CopyToRight);
        Assert.Empty(plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task Plan_DeletedOnRight_RecopiedToRight()
    {
        // In baseline, presente solo a sinistra: niente propagazione dell'eliminazione,
        // il file viene ricopiato verso destra.
        await WriteFileAsync(_left, "doc.txt", "orig", BaseTime);
        SyncBaseline baseline = BaselineWith(("doc.txt", 4, BaseTime));

        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline, onProgress: null, CancellationToken.None);

        Assert.Equal(new[] { "doc.txt" }, plan.CopyToRight);
        Assert.Empty(plan.CopyToLeft);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task Plan_ReportsProgress()
    {
        await WriteFileAsync(_left, "a.txt", "1", BaseTime);
        await WriteFileAsync(_right, "b.txt", "2", BaseTime);
        var seen = new List<CompareProgress>();

        await BidirectionalSyncService.PlanAsync(
            _left, _right, baseline: null, seen.Add, CancellationToken.None);

        Assert.Equal(2, seen.Count);
        Assert.Equal(new CompareProgress(2, 2), seen[^1]);
    }

    [Fact]
    public async Task Plan_Cancelled_Throws()
    {
        await WriteFileAsync(_left, "a.txt", "1", BaseTime);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BidirectionalSyncService.PlanAsync(_left, _right, null, null, cts.Token));
    }

    // --- ApplyAsync ---

    [Fact]
    public async Task Apply_PlannedCopies_CopiesBothDirections()
    {
        await WriteFileAsync(_left, "solo-sx.txt", "sx", BaseTime);
        await WriteFileAsync(_right, "solo-dx.txt", "dx", BaseTime);
        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, null, null, CancellationToken.None);

        SyncBaseline baseline = await BidirectionalSyncService.ApplyAsync(
            _left, _right, plan, NoResolutions, onProgress: null, CancellationToken.None);

        Assert.Equal("sx", await File.ReadAllTextAsync(Path.Combine(_right, "solo-sx.txt")));
        Assert.Equal("dx", await File.ReadAllTextAsync(Path.Combine(_left, "solo-dx.txt")));
        Assert.True(baseline.Entries.ContainsKey("solo-sx.txt"));
        Assert.True(baseline.Entries.ContainsKey("solo-dx.txt"));
    }

    [Fact]
    public async Task Apply_ConflictUseLeft_CopiesLeftToRight()
    {
        await WriteFileAsync(_left, "c.txt", "vince sinistra", BaseTime);
        await WriteFileAsync(_right, "c.txt", "dx", BaseTime);
        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, null, null, CancellationToken.None);
        var resolutions = new Dictionary<string, SyncResolution> { ["c.txt"] = SyncResolution.UseLeft };

        SyncBaseline baseline = await BidirectionalSyncService.ApplyAsync(
            _left, _right, plan, resolutions, onProgress: null, CancellationToken.None);

        Assert.Equal("vince sinistra", await File.ReadAllTextAsync(Path.Combine(_right, "c.txt")));
        Assert.True(baseline.Entries.ContainsKey("c.txt"));
    }

    [Fact]
    public async Task Apply_ConflictUseRight_CopiesRightToLeft()
    {
        await WriteFileAsync(_left, "c.txt", "sx", BaseTime);
        await WriteFileAsync(_right, "c.txt", "vince destra", BaseTime);
        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, null, null, CancellationToken.None);
        var resolutions = new Dictionary<string, SyncResolution> { ["c.txt"] = SyncResolution.UseRight };

        SyncBaseline baseline = await BidirectionalSyncService.ApplyAsync(
            _left, _right, plan, resolutions, onProgress: null, CancellationToken.None);

        Assert.Equal("vince destra", await File.ReadAllTextAsync(Path.Combine(_left, "c.txt")));
        Assert.True(baseline.Entries.ContainsKey("c.txt"));
    }

    [Fact]
    public async Task Apply_ConflictSkipped_FilesUntouched_ExcludedFromBaseline()
    {
        await WriteFileAsync(_left, "c.txt", "sx", BaseTime);
        await WriteFileAsync(_right, "c.txt", "destra diversa", BaseTime);
        await WriteFileAsync(_left, "ok.txt", "ok", BaseTime);
        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, null, null, CancellationToken.None);

        // Nessuna risoluzione: il conflitto resta Skip.
        SyncBaseline baseline = await BidirectionalSyncService.ApplyAsync(
            _left, _right, plan, NoResolutions, onProgress: null, CancellationToken.None);

        Assert.Equal("sx", await File.ReadAllTextAsync(Path.Combine(_left, "c.txt")));
        Assert.Equal("destra diversa", await File.ReadAllTextAsync(Path.Combine(_right, "c.txt")));
        // Il file divergente resta fuori dalla baseline: alla prossima Plan riemerge come conflitto.
        Assert.False(baseline.Entries.ContainsKey("c.txt"));
        Assert.True(baseline.Entries.ContainsKey("ok.txt"));
    }

    [Fact]
    public async Task Apply_CreatesNestedDestinationDirectories()
    {
        await WriteFileAsync(_left, Path.Combine("sub", "deep", "n.txt"), "annidato", BaseTime);
        SyncPlan plan = await BidirectionalSyncService.PlanAsync(
            _left, _right, null, null, CancellationToken.None);

        await BidirectionalSyncService.ApplyAsync(
            _left, _right, plan, NoResolutions, onProgress: null, CancellationToken.None);

        Assert.Equal("annidato",
            await File.ReadAllTextAsync(Path.Combine(_right, "sub", "deep", "n.txt")));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter BidirectionalSyncServiceTests`
Expected: FAIL (tipo `BidirectionalSyncService` non esistente → errore di compilazione del progetto test).

- [ ] **Step 3: Implement the service**

```csharp
// FileExplorer/Services/BidirectionalSyncService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>Tipo di conflitto rilevato dalla pianificazione della sync.</summary>
public enum SyncConflictKind
{
    /// <summary>Il file era in baseline ed è cambiato da entrambi i lati.</summary>
    BothModified,

    /// <summary>Il file non era in baseline ed è comparso, diverso, da entrambi i lati.</summary>
    BothCreated
}

/// <summary>Conflitto su un path relativo: la risoluzione spetta all'utente.</summary>
public sealed record SyncConflict(string RelativePath, SyncConflictKind Kind);

/// <summary>Piano di sincronizzazione: copie non conflittuali e conflitti da risolvere.</summary>
public sealed record SyncPlan(
    IReadOnlyList<string> CopyToRight,
    IReadOnlyList<string> CopyToLeft,
    IReadOnlyList<SyncConflict> Conflicts)
{
    /// <summary>Numero di copie non conflittuali pianificate.</summary>
    public int TotalOperations => CopyToRight.Count + CopyToLeft.Count;
}

/// <summary>Risoluzione scelta dall'utente per un conflitto.</summary>
public enum SyncResolution
{
    UseLeft,
    UseRight,
    Skip
}

/// <summary>
/// Sincronizzazione bidirezionale senza propagazione delle eliminazioni: un file
/// mancante da un lato viene ricopiato dall'altro, mai cancellato. "Cambiato"
/// rispetto alla baseline = dimensione diversa oppure |Δ LastWriteTimeUtc| ≥ 2 s
/// (stessa tolleranza di FileCopyService.IsUnchanged).
/// </summary>
public static class BidirectionalSyncService
{
    private const double MtimeToleranceSeconds = 2.0;

    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>Stato osservato di un file: dimensione, mtime e path relativo con il casing reale.</summary>
    private readonly record struct FileState(long Size, DateTime LastWriteUtc, string RelativePath);

    /// <summary>
    /// Pianifica la sync classificando l'unione dei due alberi rispetto alla baseline
    /// (null = prima sync). Nessuna scrittura su disco. Liste ordinate con il comparer
    /// di piattaforma di DirectoryComparisonService.
    /// </summary>
    public static Task<SyncPlan> PlanAsync(
        string leftRoot,
        string rightRoot,
        SyncBaseline? baseline,
        Action<CompareProgress>? onProgress,
        CancellationToken ct)
        => Task.Run(() => Plan(leftRoot, rightRoot, baseline, onProgress, ct), ct);

    private static SyncPlan Plan(
        string leftRoot,
        string rightRoot,
        SyncBaseline? baseline,
        Action<CompareProgress>? onProgress,
        CancellationToken ct)
    {
        StringComparer comparer = DirectoryComparisonService.DefaultPathComparer;
        Dictionary<string, FileState> left = RelativeFileSet(leftRoot, comparer, ct);
        Dictionary<string, FileState> right = RelativeFileSet(rightRoot, comparer, ct);

        // Le chiavi della baseline arrivano dal JSON con comparer di default:
        // le ricopiamo in un dizionario con il comparer di piattaforma.
        var baselineEntries = new Dictionary<string, SyncEntry>(comparer);
        if (baseline is not null)
        {
            foreach (var (key, entry) in baseline.Entries)
                baselineEntries.TryAdd(key, entry);
        }

        var union = new HashSet<string>(left.Keys, comparer);
        union.UnionWith(right.Keys);

        var copyToRight = new List<string>();
        var copyToLeft = new List<string>();
        var conflicts = new List<SyncConflict>();
        int processed = 0;

        foreach (string relative in union.OrderBy(p => p, comparer))
        {
            ct.ThrowIfCancellationRequested();
            bool inLeft = left.TryGetValue(relative, out FileState l);
            bool inRight = right.TryGetValue(relative, out FileState r);
            bool inBaseline = baselineEntries.TryGetValue(relative, out SyncEntry? b);

            if (inLeft && inRight)
            {
                if (inBaseline)
                {
                    bool leftChanged = Changed(l, b!);
                    bool rightChanged = Changed(r, b!);
                    if (leftChanged && rightChanged)
                        conflicts.Add(new SyncConflict(relative, SyncConflictKind.BothModified));
                    else if (leftChanged)
                        copyToRight.Add(relative);
                    else if (rightChanged)
                        copyToLeft.Add(relative);
                    // Invariato da entrambi i lati: niente da fare.
                }
                else if (!SameState(l, r))
                {
                    conflicts.Add(new SyncConflict(relative, SyncConflictKind.BothCreated));
                }
                // Nuovo da entrambi ma identico (size uguale, mtime entro tolleranza):
                // niente da fare, entrerà in baseline alla prossima Apply.
            }
            else if (inLeft)
            {
                // Presente solo a sinistra (nuovo, oppure eliminato a destra):
                // si ricopia sempre, mai si propaga l'eliminazione.
                copyToRight.Add(relative);
            }
            else if (inRight)
            {
                copyToLeft.Add(relative);
            }
            // Presente solo in baseline (eliminato da entrambi): l'entry decade
            // automaticamente alla ricostruzione della baseline.

            onProgress?.Invoke(new CompareProgress(++processed, union.Count));
        }

        return new SyncPlan(copyToRight, copyToLeft, conflicts);
    }

    /// <summary>
    /// Applica il piano: esegue le copie non conflittuali e i conflitti risolti
    /// (UseLeft → sinistra→destra, UseRight → destra→sinistra, Skip o assente → niente),
    /// poi rienumera entrambi gli alberi e restituisce la nuova baseline con i soli
    /// file concordi (stessa dimensione, mtime entro tolleranza). I file saltati o
    /// ancora divergenti restano fuori, così alla prossima pianificazione riemergono
    /// come conflitti. Il salvataggio della baseline spetta al chiamante.
    /// </summary>
    public static async Task<SyncBaseline> ApplyAsync(
        string leftRoot,
        string rightRoot,
        SyncPlan plan,
        IReadOnlyDictionary<string, SyncResolution> conflictResolutions,
        Action<CopyProgress>? onProgress,
        CancellationToken ct)
    {
        var operations = new List<(string SourcePath, string DestinationPath)>();
        foreach (string relative in plan.CopyToRight)
            operations.Add((Path.Combine(leftRoot, relative), Path.Combine(rightRoot, relative)));
        foreach (string relative in plan.CopyToLeft)
            operations.Add((Path.Combine(rightRoot, relative), Path.Combine(leftRoot, relative)));

        foreach (SyncConflict conflict in plan.Conflicts)
        {
            if (!conflictResolutions.TryGetValue(conflict.RelativePath, out SyncResolution resolution)
                || resolution == SyncResolution.Skip)
            {
                continue;
            }

            operations.Add(resolution == SyncResolution.UseLeft
                ? (Path.Combine(leftRoot, conflict.RelativePath), Path.Combine(rightRoot, conflict.RelativePath))
                : (Path.Combine(rightRoot, conflict.RelativePath), Path.Combine(leftRoot, conflict.RelativePath)));
        }

        long totalBytes = 0;
        foreach (var (source, _) in operations)
            totalBytes += new FileInfo(source).Length;

        long copiedBytes = 0;
        onProgress?.Invoke(new CopyProgress(0, totalBytes, operations.Count));

        foreach (var (source, destination) in operations)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await FileCopyService.CopyFileAsync(
                source, destination,
                bytes =>
                {
                    copiedBytes += bytes;
                    onProgress?.Invoke(new CopyProgress(copiedBytes, totalBytes, operations.Count));
                },
                ct);
        }

        return await Task.Run(() => BuildBaseline(leftRoot, rightRoot, ct), ct);
    }

    private static SyncBaseline BuildBaseline(string leftRoot, string rightRoot, CancellationToken ct)
    {
        StringComparer comparer = DirectoryComparisonService.DefaultPathComparer;
        Dictionary<string, FileState> left = RelativeFileSet(leftRoot, comparer, ct);
        Dictionary<string, FileState> right = RelativeFileSet(rightRoot, comparer, ct);

        var baseline = new SyncBaseline
        {
            LeftRoot = leftRoot,
            RightRoot = rightRoot,
            LastSyncUtc = DateTime.UtcNow
        };

        foreach (var (relative, l) in left)
        {
            ct.ThrowIfCancellationRequested();
            if (right.TryGetValue(relative, out FileState r) && SameState(l, r))
                baseline.Entries[l.RelativePath] = new SyncEntry { Size = l.Size, LastWriteUtc = l.LastWriteUtc };
        }

        return baseline;
    }

    private static bool Changed(FileState current, SyncEntry baselineEntry)
        => current.Size != baselineEntry.Size
           || Math.Abs((current.LastWriteUtc - baselineEntry.LastWriteUtc).TotalSeconds) >= MtimeToleranceSeconds;

    private static bool SameState(FileState left, FileState right)
        => left.Size == right.Size
           && Math.Abs((left.LastWriteUtc - right.LastWriteUtc).TotalSeconds) < MtimeToleranceSeconds;

    private static Dictionary<string, FileState> RelativeFileSet(
        string root, StringComparer comparer, CancellationToken ct)
    {
        var map = new Dictionary<string, FileState>(comparer);
        foreach (string file in Directory.EnumerateFiles(root, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, file);
            var info = new FileInfo(file);
            map[relative] = new FileState(info.Length, info.LastWriteTimeUtc, relative);
        }
        return map;
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter BidirectionalSyncServiceTests`
Expected: PASS (16 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/BidirectionalSyncService.cs FileExplorer.Tests/BidirectionalSyncServiceTests.cs
git commit -m "feat(sync): BidirectionalSyncService con piano e conflitti"
```

---

### Task 14: card "Sync bidirezionale" nella tab Confronto

**Model:** sonnet

**Files:**
- Create: `FileExplorer/ViewModels/SyncConflictViewModel.cs`
- Modify: `FileExplorer/ViewModels/ComparisonViewModel.cs`
- Modify: `FileExplorer/Views/ComparisonView.axaml`
- Test: `FileExplorer.Tests/ComparisonViewModelTests.cs` (esteso)

**Interfaces:**
- Consumes: `BidirectionalSyncService.PlanAsync/ApplyAsync`, `SyncPlan`, `SyncConflict`, `SyncConflictKind`, `SyncResolution` (Task 13), `SyncBaselineStore.Load/SaveAsync` (Task 12), `ConfirmDialogHelper.ShowAsync(string title, string message, string confirmLabel)` + seam `internal static Func<string, string, string, Task<bool>>? ConfirmDialogHelper.Override`, `SizeFormatter.Format(long)`.
- Produces:
  - `public class SyncConflictViewModel : ReactiveObject` — ctor `SyncConflictViewModel(SyncConflict conflict)`; `string RelativePath`, `string KindText`, `bool UseLeft`, `bool UseRight`, `bool SkipSelected` (default true, i tre bool si escludono a vicenda nel setter), `SyncResolution ToResolution()`
  - Su `ComparisonViewModel`: `ObservableCollection<SyncConflictViewModel> SyncConflicts`, `bool HasSyncPlan`, `bool IsSyncing`, `bool CanApplySync`, `string SyncStatusText`, `int SyncToRightCount/SyncToLeftCount/SyncConflictCount`, `ReactiveCommand<Unit, Unit> AnalyzeSyncCommand/ApplySyncCommand/CancelSyncCommand`, `public Task AnalyzeSyncAsync()`, `public Task ApplySyncAsync()`

**Nota anchor.** Il blocco Modify di `ComparisonViewModel.cs` e `ComparisonView.axaml` qui sotto è il contenuto completo calcolato sullo stato attuale del repo; dopo il merge della Fase 2 il file conterrà anche i membri/la card del confronto binario — integrarli mantenendo entrambe le feature (aggiunte disgiunte).

**Nota RadioButton.** Niente `GroupName`: in Avalonia i `RadioButton` senza `GroupName` si raggruppano per contenitore logico, quindi i tre radio di una riga (stesso `Grid`) formano già un gruppo isolato per riga; un `GroupName` condiviso invece renderebbe mutuamente esclusive le selezioni di *righe diverse*. In più i tre bool del VM riga si escludono a vicenda nel setter, così lo stato resta coerente anche nei test headless dove il containment visuale non esiste.

- [ ] **Step 1: Write the failing tests (file completo aggiornato)**

```csharp
// FileExplorer.Tests/ComparisonViewModelTests.cs
using System;
using System.IO;
using System.Linq;
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
    private readonly string _originalBaselinesDirectory;
    private readonly Func<string, string, string, Task<bool>>? _originalConfirmOverride;

    public ComparisonViewModelTests()
    {
        _left = Path.Combine(_tempDir, "left");
        _right = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
        _originalBaselinesDirectory = SyncBaselineStore.BaselinesDirectory;
        SyncBaselineStore.BaselinesDirectory = Path.Combine(_tempDir, "baselines");
        _originalConfirmOverride = ConfirmDialogHelper.Override;
    }

    public void Dispose()
    {
        SyncBaselineStore.BaselinesDirectory = _originalBaselinesDirectory;
        ConfirmDialogHelper.Override = _originalConfirmOverride;
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

    // --- Sync bidirezionale ---

    [Fact]
    public async Task AnalyzeSyncAsync_PopulatesCountsAndConflicts()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "solo-sx.txt"), "sx");
        await File.WriteAllTextAsync(Path.Combine(_right, "solo-dx.txt"), "dx");
        await File.WriteAllTextAsync(Path.Combine(_left, "conflitto.txt"), "versione sinistra");
        await File.WriteAllTextAsync(Path.Combine(_right, "conflitto.txt"), "dx");

        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };

        await viewModel.AnalyzeSyncAsync();

        Assert.True(viewModel.HasSyncPlan);
        Assert.Equal(1, viewModel.SyncToRightCount);
        Assert.Equal(1, viewModel.SyncToLeftCount);
        Assert.Equal(1, viewModel.SyncConflictCount);
        var conflict = Assert.Single(viewModel.SyncConflicts);
        Assert.Equal("conflitto.txt", conflict.RelativePath);
        Assert.Equal("Creato da entrambi", conflict.KindText);
        Assert.True(conflict.SkipSelected);
        Assert.False(viewModel.IsSyncing);
        Assert.Contains("1 conflitti", viewModel.SyncStatusText);
    }

    [Fact]
    public async Task AnalyzeSyncAsync_InvalidPaths_SetsErrorStatus()
    {
        using var viewModel = new ComparisonViewModel { LeftPath = null, RightPath = _right };

        await viewModel.AnalyzeSyncAsync();

        Assert.False(viewModel.HasSyncPlan);
        Assert.Contains("Selezionare", viewModel.SyncStatusText);
    }

    [Fact]
    public async Task ApplySyncAsync_WithResolution_CopiesAndSavesBaseline()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "solo-sx.txt"), "sx");
        await File.WriteAllTextAsync(Path.Combine(_left, "conflitto.txt"), "vince sinistra");
        await File.WriteAllTextAsync(Path.Combine(_right, "conflitto.txt"), "dx");
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(true);

        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.AnalyzeSyncAsync();
        viewModel.SyncConflicts.Single().UseLeft = true;

        await viewModel.ApplySyncAsync();

        Assert.Equal("sx", await File.ReadAllTextAsync(Path.Combine(_right, "solo-sx.txt")));
        Assert.Equal("vince sinistra", await File.ReadAllTextAsync(Path.Combine(_right, "conflitto.txt")));
        Assert.False(viewModel.HasSyncPlan);
        Assert.Empty(viewModel.SyncConflicts);
        Assert.Contains("completata", viewModel.SyncStatusText);

        var baseline = SyncBaselineStore.Load(_left, _right);
        Assert.NotNull(baseline);
        Assert.True(baseline!.Entries.ContainsKey("solo-sx.txt"));
        Assert.True(baseline.Entries.ContainsKey("conflitto.txt"));
    }

    [Fact]
    public async Task ApplySyncAsync_NotConfirmed_DoesNothing()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "solo-sx.txt"), "sx");
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);

        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.AnalyzeSyncAsync();

        await viewModel.ApplySyncAsync();

        Assert.False(File.Exists(Path.Combine(_right, "solo-sx.txt")));
        Assert.True(viewModel.HasSyncPlan);
        Assert.Null(SyncBaselineStore.Load(_left, _right));
    }

    [Fact]
    public void SyncConflictViewModel_Selections_AreMutuallyExclusive()
    {
        var conflict = new SyncConflictViewModel(
            new SyncConflict("x.txt", SyncConflictKind.BothModified));

        Assert.True(conflict.SkipSelected);
        Assert.Equal(SyncResolution.Skip, conflict.ToResolution());

        conflict.UseLeft = true;
        Assert.False(conflict.SkipSelected);
        Assert.False(conflict.UseRight);
        Assert.Equal(SyncResolution.UseLeft, conflict.ToResolution());

        conflict.UseRight = true;
        Assert.False(conflict.UseLeft);
        Assert.Equal(SyncResolution.UseRight, conflict.ToResolution());
        Assert.Equal("Modificato da entrambi", conflict.KindText);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter ComparisonViewModelTests`
Expected: FAIL (membri sync non esistenti su `ComparisonViewModel`, tipo `SyncConflictViewModel` mancante → errore di compilazione del progetto test).

- [ ] **Step 3: Implement `SyncConflictViewModel`**

```csharp
// FileExplorer/ViewModels/SyncConflictViewModel.cs
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Riga di conflitto nella card "Sync bidirezionale". Tre bool mutuamente
/// esclusivi al posto di RadioButton con GroupName condiviso: in Avalonia un
/// GroupName è globale al name scope, quindi righe diverse si ruberebbero la
/// selezione a vicenda; i setter garantiscono la coerenza anche headless.
/// </summary>
public class SyncConflictViewModel : ReactiveObject
{
    public SyncConflictViewModel(SyncConflict conflict)
    {
        RelativePath = conflict.RelativePath;
        KindText = conflict.Kind == SyncConflictKind.BothModified
            ? "Modificato da entrambi"
            : "Creato da entrambi";
    }

    public string RelativePath { get; }
    public string KindText { get; }

    private bool _useLeft;
    public bool UseLeft
    {
        get => _useLeft;
        set
        {
            this.RaiseAndSetIfChanged(ref _useLeft, value);
            if (value)
            {
                UseRight = false;
                SkipSelected = false;
            }
        }
    }

    private bool _useRight;
    public bool UseRight
    {
        get => _useRight;
        set
        {
            this.RaiseAndSetIfChanged(ref _useRight, value);
            if (value)
            {
                UseLeft = false;
                SkipSelected = false;
            }
        }
    }

    private bool _skipSelected = true;
    public bool SkipSelected
    {
        get => _skipSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _skipSelected, value);
            if (value)
            {
                UseLeft = false;
                UseRight = false;
            }
        }
    }

    /// <summary>Risoluzione corrente della riga (default: Skip).</summary>
    public SyncResolution ToResolution()
        => UseLeft ? SyncResolution.UseLeft
            : UseRight ? SyncResolution.UseRight
            : SyncResolution.Skip;
}
```

- [ ] **Step 4: Extend `ComparisonViewModel` (file completo)**

```csharp
// FileExplorer/ViewModels/ComparisonViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Confronto": confronta due directory (cascata dimensione → SHA-256),
/// esporta il report in HTML/CSV/JSON e sincronizza in modo bidirezionale
/// (mai eliminazioni; i conflitti li risolve l'utente riga per riga).
/// </summary>
public class ComparisonViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _compareCts;
    private CancellationTokenSource? _syncCts;
    private string? _comparedLeftRoot;
    private string? _comparedRightRoot;
    private string? _syncedLeftRoot;
    private string? _syncedRightRoot;
    private SyncPlan? _syncPlan;

    public ComparisonViewModel()
    {
        BrowseLeftCommand = ReactiveCommand.CreateFromTask(BrowseLeftAsync);
        BrowseRightCommand = ReactiveCommand.CreateFromTask(BrowseRightAsync);
        CompareCommand = ReactiveCommand.CreateFromTask(CompareAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExportHtmlCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Html));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Csv));
        ExportJsonCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Json));
        AnalyzeSyncCommand = ReactiveCommand.CreateFromTask(AnalyzeSyncAsync);
        ApplySyncCommand = ReactiveCommand.CreateFromTask(ApplySyncAsync);
        CancelSyncCommand = ReactiveCommand.Create(CancelSync);
    }

    public ReactiveCommand<Unit, Unit> BrowseLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportHtmlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }
    public ReactiveCommand<Unit, Unit> AnalyzeSyncCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplySyncCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelSyncCommand { get; }

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

    // --- Sync bidirezionale ---

    public ObservableCollection<SyncConflictViewModel> SyncConflicts { get; } = new();

    public bool HasSyncPlan => _syncPlan is not null;
    public int SyncToRightCount => _syncPlan?.CopyToRight.Count ?? 0;
    public int SyncToLeftCount => _syncPlan?.CopyToLeft.Count ?? 0;
    public int SyncConflictCount => _syncPlan?.Conflicts.Count ?? 0;
    public bool CanApplySync => HasSyncPlan && !IsSyncing;

    private bool _isSyncing;
    public bool IsSyncing
    {
        get => _isSyncing;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSyncing, value);
            this.RaisePropertyChanged(nameof(CanApplySync));
        }
    }

    private string _syncStatusText = "Analizzare le due cartelle per pianificare la sync";
    public string SyncStatusText
    {
        get => _syncStatusText;
        private set => this.RaiseAndSetIfChanged(ref _syncStatusText, value);
    }

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
            // Catturati prima dell'await: le TextBox restano editabili durante il confronto,
            // quindi LeftPath/RightPath potrebbero cambiare mentre CompareAsync è in corso.
            string left = LeftPath;
            string right = RightPath;

            int parallelism = Math.Max(2, Environment.ProcessorCount - 1);
            var result = await DirectoryComparisonService.CompareAsync(
                left, right, parallelism,
                progress => StatusText = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            Result = result;
            _comparedLeftRoot = left;
            _comparedRightRoot = right;
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
        if (Result is null || _comparedLeftRoot is null || _comparedRightRoot is null)
            return null;

        try
        {
            DateTime generatedUtc = DateTime.UtcNow;
            string filePath = Path.Combine(
                targetDirectory, ComparisonReportExporter.SuggestFileName(format, generatedUtc));

            await ComparisonReportExporter.ExportAsync(
                filePath, Result, format, _comparedLeftRoot, _comparedRightRoot, generatedUtc, CancellationToken.None);

            StatusText = $"Report esportato: {filePath}";
            return filePath;
        }
        catch (Exception ex)
        {
            StatusText = $"Errore esportazione: {ex.Message}";
            return null;
        }
    }

    /// <summary>Pianifica la sync bidirezionale delle due cartelle. Pubblico per i test.</summary>
    public async Task AnalyzeSyncAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath)
            || !Directory.Exists(LeftPath) || !Directory.Exists(RightPath))
        {
            SyncStatusText = "Selezionare due cartelle esistenti";
            return;
        }

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        IsSyncing = true;
        SetSyncPlan(null);
        SyncConflicts.Clear();
        SyncStatusText = "Analisi in corso…";

        try
        {
            // Stessa cattura pre-await di CompareAsync: i path della sync restano
            // quelli analizzati anche se l'utente edita le TextBox nel frattempo.
            string left = LeftPath;
            string right = RightPath;

            SyncBaseline? baseline = SyncBaselineStore.Load(left, right);
            var plan = await BidirectionalSyncService.PlanAsync(
                left, right, baseline,
                progress => SyncStatusText = $"Analisi in corso… ({progress.Processed}/{progress.Total})",
                ct);

            _syncedLeftRoot = left;
            _syncedRightRoot = right;
            foreach (var conflict in plan.Conflicts)
                SyncConflicts.Add(new SyncConflictViewModel(conflict));
            SetSyncPlan(plan);
            SyncStatusText = $"{plan.CopyToRight.Count} da copiare a destra, " +
                             $"{plan.CopyToLeft.Count} a sinistra, {plan.Conflicts.Count} conflitti";
        }
        catch (OperationCanceledException)
        {
            SyncStatusText = "Analisi annullata";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>Applica il piano di sync con le risoluzioni scelte. Pubblico per i test.</summary>
    public async Task ApplySyncAsync()
    {
        if (_syncPlan is null || _syncedLeftRoot is null || _syncedRightRoot is null)
            return;

        var resolutions = new Dictionary<string, SyncResolution>();
        foreach (var conflict in SyncConflicts)
            resolutions[conflict.RelativePath] = conflict.ToResolution();

        int totalCopies = _syncPlan.TotalOperations
            + resolutions.Count(r => r.Value != SyncResolution.Skip);
        if (totalCopies == 0)
        {
            SyncStatusText = "Niente da copiare";
            return;
        }

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Sincronizzazione bidirezionale",
            $"Applicare la sincronizzazione? Verranno copiati {totalCopies} file.",
            "Applica");
        if (!confirmed)
            return;

        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        IsSyncing = true;
        SyncStatusText = "Sincronizzazione in corso…";

        try
        {
            SyncBaseline baseline = await BidirectionalSyncService.ApplyAsync(
                _syncedLeftRoot, _syncedRightRoot, _syncPlan, resolutions,
                progress => SyncStatusText = "Sincronizzazione in corso… " +
                    $"({SizeFormatter.Format(progress.CopiedBytes)}/{SizeFormatter.Format(progress.TotalBytes)})",
                ct);

            await SyncBaselineStore.SaveAsync(baseline);
            SetSyncPlan(null);
            SyncConflicts.Clear();
            SyncStatusText = $"Sincronizzazione completata: {totalCopies} file copiati";
        }
        catch (OperationCanceledException)
        {
            SyncStatusText = "Sincronizzazione annullata";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void CancelSync() => _syncCts?.Cancel();

    private void SetSyncPlan(SyncPlan? plan)
    {
        _syncPlan = plan;
        this.RaisePropertyChanged(nameof(HasSyncPlan));
        this.RaisePropertyChanged(nameof(SyncToRightCount));
        this.RaisePropertyChanged(nameof(SyncToLeftCount));
        this.RaisePropertyChanged(nameof(SyncConflictCount));
        this.RaisePropertyChanged(nameof(CanApplySync));
    }

    public void Dispose()
    {
        _compareCts?.Cancel();
        _compareCts?.Dispose();
        _compareCts = null;
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = null;
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test --filter "ComparisonViewModelTests"`
Expected: PASS (9 test).

- [ ] **Step 6: Add the third card to `ComparisonView.axaml` (file completo)**

```xml
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

            <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource Brush.TextMuted}" />
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
                <TextBlock Text="Identici" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding DifferentCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Diversi" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding LeftOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a sinistra" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
              </StackPanel>
              <StackPanel Spacing="2">
                <TextBlock Text="{Binding RightOnlyCount}" FontSize="20" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <TextBlock Text="Solo a destra" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />
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

        <Border Classes="card">
          <StackPanel Spacing="10">
            <TextBlock Text="Sync bidirezionale" FontSize="15" FontWeight="SemiBold"
                       Foreground="{DynamicResource Brush.TextPrimary}" />
            <TextBlock Text="Copia le novità in entrambe le direzioni rispetto all'ultima sincronizzazione. Non elimina mai nulla: un file mancante da un lato viene ricopiato dall'altro. I conflitti si risolvono riga per riga."
                       TextWrapping="Wrap" Foreground="{DynamicResource Brush.TextMuted}" FontSize="12" />

            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button Classes="primary" Command="{Binding AnalyzeSyncCommand}" IsEnabled="{Binding !IsSyncing}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <i:Icon Value="fa-solid fa-rotate" />
                  <TextBlock Text="Analizza" />
                </StackPanel>
              </Button>
              <Button Classes="primary" Command="{Binding ApplySyncCommand}" IsEnabled="{Binding CanApplySync}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <i:Icon Value="fa-solid fa-check" />
                  <TextBlock Text="Applica" />
                </StackPanel>
              </Button>
              <Button Classes="secondary" Command="{Binding CancelSyncCommand}" IsEnabled="{Binding IsSyncing}">
                <TextBlock Text="Annulla" />
              </Button>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding HasSyncPlan}">
              <Border Classes="badge">
                <TextBlock Text="{Binding SyncToRightCount, StringFormat='→ destra: {0}'}" />
              </Border>
              <Border Classes="badge">
                <TextBlock Text="{Binding SyncToLeftCount, StringFormat='← sinistra: {0}'}" />
              </Border>
              <Border Classes="badge warning">
                <TextBlock Text="{Binding SyncConflictCount, StringFormat='Conflitti: {0}'}" />
              </Border>
            </StackPanel>

            <ItemsControl ItemsSource="{Binding SyncConflicts}">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <!-- Niente GroupName: i RadioButton si raggruppano per contenitore
                       logico, quindi ogni riga è un gruppo isolato; i tre bool
                       esclusivi del VM tengono coerente lo stato anche headless. -->
                  <Grid ColumnDefinitions="*,Auto,Auto,Auto" Margin="0,2">
                    <StackPanel Grid.Column="0" Spacing="1" VerticalAlignment="Center">
                      <TextBlock Text="{Binding RelativePath}"
                                 Foreground="{DynamicResource Brush.TextPrimary}" />
                      <TextBlock Text="{Binding KindText}"
                                 Foreground="{DynamicResource Brush.TextMuted}" FontSize="11" />
                    </StackPanel>
                    <RadioButton Grid.Column="1" Content="Sinistra" Margin="8,0,0,0"
                                 IsChecked="{Binding UseLeft, Mode=TwoWay}" />
                    <RadioButton Grid.Column="2" Content="Destra" Margin="8,0,0,0"
                                 IsChecked="{Binding UseRight, Mode=TwoWay}" />
                    <RadioButton Grid.Column="3" Content="Salta" Margin="8,0,0,0"
                                 IsChecked="{Binding SkipSelected, Mode=TwoWay}" />
                  </Grid>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="{Binding SyncStatusText}" Foreground="{DynamicResource Brush.TextMuted}" />
          </StackPanel>
        </Border>

      </StackPanel>
    </ScrollViewer>

  </DockPanel>

</UserControl>
```

- [ ] **Step 7: Full build + test + smoke run**

Run: `dotnet build FileExplorer.sln && dotnet test`
Expected: build OK, tutti i test PASS.

Run (solo se `DISPLAY` è impostato): `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project FileExplorer.Desktop`
Expected: tab Confronto mostra la terza card "Sync bidirezionale"; Analizza su due cartelle di prova popola badge e conflitti; nessun errore XAML a runtime.

- [ ] **Step 8: Commit**

```bash
git add FileExplorer/ViewModels/SyncConflictViewModel.cs FileExplorer/ViewModels/ComparisonViewModel.cs FileExplorer/Views/ComparisonView.axaml FileExplorer.Tests/ComparisonViewModelTests.cs
git commit -m "feat(sync): card Sync bidirezionale nella tab Confronto"
```

---

### Task 15: Chiusura Fase 5 (docs + PR)

**Model:** haiku

**Files:**
- Modify: `IDEE.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/plans/2026-08-19-cinque-idee-avanzate.md` (spuntare i checkbox della Fase 5)

**Interfaces:** nessuna.

- [ ] **Step 1: Aggiorna IDEE.md**

Nel file `IDEE.md`, riga del punto 10, cambiare lo stato da proposta a implementata:

```markdown
10. `[x]` **Sync bidirezionale con rilevamento conflitti** — merge a due vie tra i pannelli: rileva file modificati da entrambi i lati dall'ultima sync (stato salvato) e chiede risoluzione per i conflitti invece di sovrascrivere alla cieca. *(A)*
```

- [ ] **Step 2: Aggiorna CLAUDE.md**

Nella sezione dedicata alla persistenza/architettura (dopo il paragrafo sui temi custom), aggiungere una riga:

```markdown
Sync bidirezionale: `SyncBaselineStore` salva una baseline JSON per coppia di cartelle in `AppData/FileExplorer/sync-baselines/` (nome file = hash dei path normalizzati); `BidirectionalSyncService` non propaga mai le eliminazioni e classifica i conflitti (`BothModified`/`BothCreated`) da risolvere in UI.
```

- [ ] **Step 3: Spunta i checkbox della Fase 5 nel piano**

Marcare `[x]` tutti gli step dei Task 12–15 in `docs/superpowers/plans/2026-08-19-cinque-idee-avanzate.md`.

- [ ] **Step 4: Verifica finale**

Run: `dotnet build FileExplorer.sln && dotnet test`
Expected: build OK, tutti i test PASS.

- [ ] **Step 5: Commit + PR**

```bash
git add IDEE.md CLAUDE.md docs/superpowers/plans/2026-08-19-cinque-idee-avanzate.md
git commit -m "docs(sync): chiusura piano cinque idee avanzate"
git push -u origin feature/bidirectional-sync
gh pr create --title "Sync bidirezionale con rilevamento conflitti" --body "IDEE punto 10: pianificazione sync a due vie con baseline persistente, conflitti risolti dall'utente (Sinistra/Destra/Salta), mai eliminazioni. Card dedicata nella tab Confronto."
```

---

---

## Note di esecuzione

- Ordine fasi obbligato: Fase 1 (Task 1–3) → Fase 2 (Task 4–5) → Fase 3 (Task 6–7) → Fase 4 (Task 8–11) → Fase 5 (Task 12–15). Dentro ogni fase i task sono in dipendenza a catena.
- Ogni fase è un branch + PR separato; il branch della fase successiva parte da `main` aggiornato dopo il merge della precedente. Fase 3 dipende dal merge di Fase 1 (entrambe toccano `CopyPairsViewModel`/`CopyPairsView`); Fase 5 dipende dal merge di Fase 2 (entrambe toccano `ComparisonViewModel`/`ComparisonView`). In caso di anchor spostati nei Modify, adattare mantenendo entrambe le feature.
- Rischio noto (Task 6): integrità dati nella scrittura in-place — i test coprono lunghezze diverse, blocchi parziali e cancellazione; non aggiungere ottimizzazioni fuori piano.
- Rischio noto (Task 9): flakiness dei test asincroni su debounce/coalescing — usare i seam (`DebounceDelay`, `SyncOverride`, intervallo di test) e polling con timeout, mai sleep lunghi fissi.
- Rischio noto (Task 13): la matrice di classificazione è il cuore della feature — implementare esattamente i rami documentati; nessuna operazione distruttiva (mai delete).
- Smoke test manuale a fine fase (l'ambiente sandbox richiede `DOTNET_ROLL_FORWARD=LatestMajor` per `dotnet run`).
