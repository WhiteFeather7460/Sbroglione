# Temi personalizzabili — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Temi colore completi creabili/modificabili/importabili dall'utente, con built-in Chiaro/Scuro non modificabili e anteprima live.

**Architecture:** `ColorTheme` (modello JSON) + `ThemeStore` (persistenza AppData) + `ThemeService` (ResourceDictionary registrato come `ThemeVariant("Custom", base)` in `Application.Resources.ThemeDictionaries`, con fallback per ereditarietà a Palette.axaml) + card "Temi" in SettingsView + `ThemeEditorWindow` con ColorPicker e anteprima live.

**Tech Stack:** .NET 8, Avalonia 11.2.8 (+ pacchetto `Avalonia.Controls.ColorPicker`), ReactiveUI, System.Text.Json, xunit.

**Spec:** `docs/superpowers/specs/2026-08-19-user-themes-design.md`

## Global Constraints

- Branch di lavoro: `feature/user-themes` (mai commit su `main`).
- Nessun colore hardcodato nelle view: sempre `{DynamicResource Brush.*}`.
- Niente co-author Claude nei commit.
- Test: `dotnet test` dalla root. Build: `dotnet build FileExplorer.sln`.
- Servizi statici (pattern `AppSettingsStore`/`ProfileStore`), niente DI container.
- Stringhe UI in italiano, come il resto dell'app.
- Ogni task dichiara il modello del subagente esecutore (`Model:`).

---

### Task 1: ColorTheme, ThemeColorKeys, BuiltInThemes

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Models/ColorTheme.cs`
- Create: `FileExplorer/Models/ThemeColorKeys.cs`
- Create: `FileExplorer/Services/BuiltInThemes.cs`
- Test: `FileExplorer.Tests/ColorThemeTests.cs`

**Interfaces:**
- Consumes: nulla (foglia).
- Produces:
  - `ColorTheme { string Id; string Name; string BaseVariant; Dictionary<string,string> Colors; [JsonIgnore] bool IsBuiltIn; ColorTheme Clone(); }`
  - `ThemeColorKeys.All : IReadOnlyList<string>`, costanti `ThemeColorKeys.Accent`, `.AccentGradientStart`, `.AccentGradientEnd`, `.OnAccent`, ecc.
  - `BuiltInThemes.Light : ColorTheme`, `BuiltInThemes.Dark : ColorTheme`, `BuiltInThemes.ForVariant(string) : ColorTheme` (istanze nuove a ogni chiamata, Id fissi `builtin-light`/`builtin-dark`).

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/ColorThemeTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class ColorThemeTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void BuiltIn_themes_cover_all_keys_with_valid_hex(string variant)
    {
        ColorTheme theme = BuiltInThemes.ForVariant(variant);

        Assert.True(theme.IsBuiltIn);
        Assert.Equal(variant, theme.BaseVariant);
        foreach (string key in ThemeColorKeys.All)
        {
            Assert.True(theme.Colors.ContainsKey(key), $"chiave mancante: {key}");
            Assert.True(Avalonia.Media.Color.TryParse(theme.Colors[key], out _), $"hex invalido per {key}: {theme.Colors[key]}");
        }
    }

    [Fact]
    public void ForVariant_returns_fresh_instances()
    {
        ColorTheme a = BuiltInThemes.ForVariant("Light");
        ColorTheme b = BuiltInThemes.ForVariant("Light");
        a.Colors[ThemeColorKeys.Accent] = "#000000";
        Assert.NotEqual("#000000", b.Colors[ThemeColorKeys.Accent]);
    }

    [Fact]
    public void Clone_is_deep_and_gets_same_values()
    {
        ColorTheme original = BuiltInThemes.ForVariant("Dark");
        ColorTheme clone = original.Clone();

        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Colors, clone.Colors);
        clone.Colors[ThemeColorKeys.Surface] = "#123456";
        Assert.NotEqual(original.Colors[ThemeColorKeys.Surface], clone.Colors[ThemeColorKeys.Surface]);
    }

    [Fact]
    public void ForVariant_unknown_falls_back_to_light()
    {
        Assert.Equal("Light", BuiltInThemes.ForVariant("boh").BaseVariant);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter ColorThemeTests`
Expected: FAIL (tipi non esistenti → errore di compilazione del progetto test).

- [ ] **Step 3: Implement the three files**

```csharp
// FileExplorer/Models/ThemeColorKeys.cs
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>
/// Elenco canonico delle chiavi colore di un tema. Mirror delle risorse Brush.* in
/// Styles/Palette.axaml; AccentGradientStart/End compongono Brush.AccentGradient.
/// </summary>
public static class ThemeColorKeys
{
    public const string Accent = "Accent";
    public const string AccentGradientStart = "AccentGradientStart";
    public const string AccentGradientEnd = "AccentGradientEnd";
    public const string OnAccent = "OnAccent";
    public const string Surface = "Surface";
    public const string Card = "Card";
    public const string CardBorder = "CardBorder";
    public const string Field = "Field";
    public const string TextPrimary = "TextPrimary";
    public const string TextMuted = "TextMuted";
    public const string SuccessBg = "SuccessBg";
    public const string SuccessFg = "SuccessFg";
    public const string WarningBg = "WarningBg";
    public const string WarningFg = "WarningFg";
    public const string ErrorBg = "ErrorBg";
    public const string ErrorFg = "ErrorFg";
    public const string ProgressBg = "ProgressBg";
    public const string ProgressFg = "ProgressFg";
    public const string NeutralBg = "NeutralBg";
    public const string NeutralFg = "NeutralFg";
    public const string Treemap1 = "Treemap.1";
    public const string Treemap2 = "Treemap.2";
    public const string Treemap3 = "Treemap.3";
    public const string Treemap4 = "Treemap.4";
    public const string Treemap5 = "Treemap.5";
    public const string Treemap6 = "Treemap.6";
    public const string SparklineLine = "Sparkline.Line";
    public const string SparklineFill = "Sparkline.Fill";

    public static readonly IReadOnlyList<string> All =
    [
        Accent, AccentGradientStart, AccentGradientEnd, OnAccent,
        Surface, Card, CardBorder, Field, TextPrimary, TextMuted,
        SuccessBg, SuccessFg, WarningBg, WarningFg, ErrorBg, ErrorFg,
        ProgressBg, ProgressFg, NeutralBg, NeutralFg,
        Treemap1, Treemap2, Treemap3, Treemap4, Treemap5, Treemap6,
        SparklineLine, SparklineFill
    ];
}
```

```csharp
// FileExplorer/Models/ColorTheme.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FileExplorer.Models;

/// <summary>Tema colore nominato, serializzato in JSON (un file per tema in AppData/themes).</summary>
public class ColorTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>"Light" o "Dark": variante ereditata per i controlli nativi e fallback colori.</summary>
    public string BaseVariant { get; set; } = "Light";

    /// <summary>Chiave logica (<see cref="ThemeColorKeys"/>) → colore hex "#RRGGBB"/"#AARRGGBB".</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>True solo per Chiaro/Scuro generati in codice: non modificabili né eliminabili.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    public ColorTheme Clone() => new()
    {
        Id = Id,
        Name = Name,
        BaseVariant = BaseVariant,
        Colors = new Dictionary<string, string>(Colors),
        IsBuiltIn = IsBuiltIn
    };
}
```

```csharp
// FileExplorer/Services/BuiltInThemes.cs
using System.Collections.Generic;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Temi built-in "Chiaro" e "Scuro": valori identici a Styles/Palette.axaml. Usati come base
/// per duplicazione e come fallback per chiavi mancanti/invalide nei temi custom.
/// </summary>
public static class BuiltInThemes
{
    public const string LightId = "builtin-light";
    public const string DarkId = "builtin-dark";

    public static ColorTheme Light => new()
    {
        Id = LightId,
        Name = "Chiaro",
        BaseVariant = "Light",
        IsBuiltIn = true,
        Colors = new Dictionary<string, string>
        {
            [ThemeColorKeys.Accent] = "#FF5E62",
            [ThemeColorKeys.AccentGradientStart] = "#FF5E62",
            [ThemeColorKeys.AccentGradientEnd] = "#FF9446",
            [ThemeColorKeys.OnAccent] = "#FFFFFF",
            [ThemeColorKeys.Surface] = "#FAF9F7",
            [ThemeColorKeys.Card] = "#FFFFFF",
            [ThemeColorKeys.CardBorder] = "#EEE2DA",
            [ThemeColorKeys.Field] = "#F4EFE9",
            [ThemeColorKeys.TextPrimary] = "#2B2420",
            [ThemeColorKeys.TextMuted] = "#8A7F78",
            [ThemeColorKeys.SuccessBg] = "#E6F6EC",
            [ThemeColorKeys.SuccessFg] = "#1F8A4C",
            [ThemeColorKeys.WarningBg] = "#FBF0D9",
            [ThemeColorKeys.WarningFg] = "#9A6B00",
            [ThemeColorKeys.ErrorBg] = "#FBE5E2",
            [ThemeColorKeys.ErrorFg] = "#C43025",
            [ThemeColorKeys.ProgressBg] = "#FFE9DF",
            [ThemeColorKeys.ProgressFg] = "#D8481F",
            [ThemeColorKeys.NeutralBg] = "#EFEAE5",
            [ThemeColorKeys.NeutralFg] = "#6E635C",
            [ThemeColorKeys.Treemap1] = "#F2C4B3",
            [ThemeColorKeys.Treemap2] = "#F5D8A7",
            [ThemeColorKeys.Treemap3] = "#C9DEC4",
            [ThemeColorKeys.Treemap4] = "#BCD5E3",
            [ThemeColorKeys.Treemap5] = "#D9C6E0",
            [ThemeColorKeys.Treemap6] = "#E3CFC0",
            [ThemeColorKeys.SparklineLine] = "#2563EB",
            [ThemeColorKeys.SparklineFill] = "#332563EB"
        }
    };

    public static ColorTheme Dark => new()
    {
        Id = DarkId,
        Name = "Scuro",
        BaseVariant = "Dark",
        IsBuiltIn = true,
        Colors = new Dictionary<string, string>
        {
            [ThemeColorKeys.Accent] = "#FF5E62",
            [ThemeColorKeys.AccentGradientStart] = "#FF5E62",
            [ThemeColorKeys.AccentGradientEnd] = "#FF9446",
            [ThemeColorKeys.OnAccent] = "#FFFFFF",
            [ThemeColorKeys.Surface] = "#1E1B1A",
            [ThemeColorKeys.Card] = "#2A2624",
            [ThemeColorKeys.CardBorder] = "#3A3430",
            [ThemeColorKeys.Field] = "#35302D",
            [ThemeColorKeys.TextPrimary] = "#F2ECE7",
            [ThemeColorKeys.TextMuted] = "#A79A91",
            [ThemeColorKeys.SuccessBg] = "#22402E",
            [ThemeColorKeys.SuccessFg] = "#7FD8A2",
            [ThemeColorKeys.WarningBg] = "#453A1C",
            [ThemeColorKeys.WarningFg] = "#E8C36A",
            [ThemeColorKeys.ErrorBg] = "#46231F",
            [ThemeColorKeys.ErrorFg] = "#FF9C8F",
            [ThemeColorKeys.ProgressBg] = "#44261C",
            [ThemeColorKeys.ProgressFg] = "#FFA07C",
            [ThemeColorKeys.NeutralBg] = "#35302D",
            [ThemeColorKeys.NeutralFg] = "#B5A89F",
            [ThemeColorKeys.Treemap1] = "#7A4A3C",
            [ThemeColorKeys.Treemap2] = "#7A6236",
            [ThemeColorKeys.Treemap3] = "#46603F",
            [ThemeColorKeys.Treemap4] = "#3B586B",
            [ThemeColorKeys.Treemap5] = "#5C4668",
            [ThemeColorKeys.Treemap6] = "#6B5546",
            [ThemeColorKeys.SparklineLine] = "#60A5FA",
            [ThemeColorKeys.SparklineFill] = "#3360A5FA"
        }
    };

    public static ColorTheme ForVariant(string variant) => variant == "Dark" ? Dark : Light;
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter ColorThemeTests`
Expected: PASS (4 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Models/ColorTheme.cs FileExplorer/Models/ThemeColorKeys.cs FileExplorer/Services/BuiltInThemes.cs FileExplorer.Tests/ColorThemeTests.cs
git commit -m "feat(themes): modello ColorTheme, chiavi canoniche e temi built-in"
```

---

### Task 2: ThemeStore (persistenza, sanitizzazione, import/export)

**Model:** sonnet

**Files:**
- Create: `FileExplorer/Services/ThemeStore.cs`
- Test: `FileExplorer.Tests/ThemeStoreTests.cs`

**Interfaces:**
- Consumes: `ColorTheme`, `ThemeColorKeys`, `BuiltInThemes` (Task 1).
- Produces (tutte statiche su `ThemeStore`):
  - `string ThemesDirectory { get; set; }` (default `AppData/FileExplorer/themes`, sovrascrivibile nei test)
  - `List<ColorTheme> LoadAll()` — sincrona (avvio), file corrotti saltati, ordinata per Name
  - `ColorTheme? Load(string id)`
  - `Task SaveAsync(ColorTheme theme)` — atomica (tmp + move), sanitizza prima di scrivere
  - `void Delete(string id)`
  - `Task ExportAsync(ColorTheme theme, string path)`
  - `ColorTheme? Import(string path)` — null se illeggibile; assegna SEMPRE un nuovo Id
  - `void Sanitize(ColorTheme theme)` — normalizza in-place (vedi test)

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/ThemeStoreTests.cs
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class ThemeStoreTests : IDisposable
{
    private readonly string _dir;

    public ThemeStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-themes-" + Guid.NewGuid().ToString("N"));
        ThemeStore.ThemesDirectory = _dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ColorTheme MakeTheme(string name = "Test")
    {
        ColorTheme theme = BuiltInThemes.ForVariant("Light");
        theme.Id = Guid.NewGuid().ToString("N");
        theme.Name = name;
        theme.IsBuiltIn = false;
        return theme;
    }

    [Fact]
    public async Task Save_then_LoadAll_roundtrips()
    {
        ColorTheme theme = MakeTheme("Il mio tema");
        theme.Colors[ThemeColorKeys.Accent] = "#112233";
        await ThemeStore.SaveAsync(theme);

        List<ColorTheme> all = ThemeStore.LoadAll();

        ColorTheme loaded = Assert.Single(all);
        Assert.Equal(theme.Id, loaded.Id);
        Assert.Equal("Il mio tema", loaded.Name);
        Assert.Equal("#112233", loaded.Colors[ThemeColorKeys.Accent]);
        Assert.False(loaded.IsBuiltIn);
    }

    [Fact]
    public async Task LoadAll_skips_corrupt_files()
    {
        await ThemeStore.SaveAsync(MakeTheme("Valido"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "corrotto.json"), "{ non-json !!");

        List<ColorTheme> all = ThemeStore.LoadAll();

        Assert.Single(all);
    }

    [Fact]
    public void Sanitize_fixes_invalid_and_missing_entries()
    {
        var theme = new ColorTheme
        {
            Name = "",
            BaseVariant = "Boh",
            Colors = new Dictionary<string, string>
            {
                [ThemeColorKeys.Accent] = "non-un-colore",
                ["ChiaveSconosciuta"] = "#FFFFFF"
            }
        };

        ThemeStore.Sanitize(theme);

        Assert.Equal("Tema senza nome", theme.Name);
        Assert.Equal("Light", theme.BaseVariant);
        Assert.False(theme.Colors.ContainsKey("ChiaveSconosciuta"));
        // hex invalido e chiavi mancanti → fallback dal built-in della BaseVariant
        ColorTheme fallback = BuiltInThemes.ForVariant("Light");
        Assert.Equal(fallback.Colors[ThemeColorKeys.Accent], theme.Colors[ThemeColorKeys.Accent]);
        foreach (string key in ThemeColorKeys.All)
            Assert.True(theme.Colors.ContainsKey(key), $"chiave mancante dopo Sanitize: {key}");
    }

    [Fact]
    public async Task Delete_removes_theme_file()
    {
        ColorTheme theme = MakeTheme();
        await ThemeStore.SaveAsync(theme);

        ThemeStore.Delete(theme.Id);

        Assert.Empty(ThemeStore.LoadAll());
    }

    [Fact]
    public async Task Export_then_Import_assigns_new_id()
    {
        ColorTheme theme = MakeTheme("Esportato");
        string path = Path.Combine(_dir, "export.json");
        Directory.CreateDirectory(_dir);
        await ThemeStore.ExportAsync(theme, path);

        ColorTheme? imported = ThemeStore.Import(path);

        Assert.NotNull(imported);
        Assert.NotEqual(theme.Id, imported.Id);
        Assert.Equal("Esportato", imported.Name);
        Assert.Equal(theme.Colors, imported.Colors);
    }

    [Fact]
    public void Import_unreadable_returns_null()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{{{{");

        Assert.Null(ThemeStore.Import(path));
        Assert.Null(ThemeStore.Import(Path.Combine(_dir, "inesistente.json")));
    }

    [Fact]
    public async Task Load_by_id_returns_theme_or_null()
    {
        ColorTheme theme = MakeTheme();
        await ThemeStore.SaveAsync(theme);

        Assert.NotNull(ThemeStore.Load(theme.Id));
        Assert.Null(ThemeStore.Load("id-inesistente"));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter ThemeStoreTests`
Expected: FAIL (ThemeStore non esiste).

- [ ] **Step 3: Implement ThemeStore**

```csharp
// FileExplorer/Services/ThemeStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza dei temi custom: un file JSON per tema in <see cref="ThemesDirectory"/>,
/// scrittura atomica (tmp + move) e load tollerante, stesso pattern di <see cref="AppSettingsStore"/>.
/// I temi built-in NON passano da qui (generati da <see cref="BuiltInThemes"/>).
/// </summary>
public static class ThemeStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Cartella dei temi. Sovrascrivibile nei test per non toccare l'AppData reale.</summary>
    public static string ThemesDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "themes");

    private static string PathFor(string id) => Path.Combine(ThemesDirectory, id + ".json");

    /// <summary>Carica tutti i temi custom; i file corrotti vengono saltati. Ordinati per nome.</summary>
    public static List<ColorTheme> LoadAll()
    {
        var themes = new List<ColorTheme>();
        if (!Directory.Exists(ThemesDirectory))
            return themes;

        foreach (string file in Directory.EnumerateFiles(ThemesDirectory, "*.json"))
        {
            ColorTheme? theme = ReadFile(file);
            if (theme is not null)
                themes.Add(theme);
        }

        return themes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Carica un singolo tema per Id; null se assente o corrotto.</summary>
    public static ColorTheme? Load(string id) => ReadFile(PathFor(id));

    /// <summary>Salva il tema (sanitizzato) con scrittura atomica, creando la cartella se assente.</summary>
    public static async Task SaveAsync(ColorTheme theme)
    {
        Sanitize(theme);
        Directory.CreateDirectory(ThemesDirectory);

        string path = PathFor(theme.Id);
        string tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, theme, Options).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Elimina il file del tema; nessun errore se già assente.</summary>
    public static void Delete(string id)
    {
        try
        {
            File.Delete(PathFor(id));
        }
        catch (Exception)
        {
            // best effort: un file non eliminabile non deve rompere la UI.
        }
    }

    /// <summary>Esporta il tema come file JSON nel percorso indicato.</summary>
    public static async Task ExportAsync(ColorTheme theme, string path)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, theme, Options).ConfigureAwait(false);
    }

    /// <summary>Importa un tema da file: null se illeggibile. Assegna sempre un nuovo Id.</summary>
    public static ColorTheme? Import(string path)
    {
        ColorTheme? theme = ReadFile(path);
        if (theme is null)
            return null;

        theme.Id = Guid.NewGuid().ToString("N");
        return theme;
    }

    /// <summary>
    /// Normalizza il tema in-place: nome non vuoto, BaseVariant valida, chiavi sconosciute
    /// scartate, hex invalidi e chiavi mancanti sostituiti dal built-in della BaseVariant.
    /// </summary>
    public static void Sanitize(ColorTheme theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name))
            theme.Name = "Tema senza nome";
        if (theme.BaseVariant is not ("Light" or "Dark"))
            theme.BaseVariant = "Light";

        ColorTheme fallback = BuiltInThemes.ForVariant(theme.BaseVariant);
        var clean = new Dictionary<string, string>();
        foreach (string key in ThemeColorKeys.All)
        {
            clean[key] = theme.Colors.TryGetValue(key, out string? hex) && Color.TryParse(hex, out _)
                ? hex
                : fallback.Colors[key];
        }

        theme.Colors = clean;
    }

    private static ColorTheme? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            ColorTheme? theme = JsonSerializer.Deserialize<ColorTheme>(json, Options);
            if (theme is null)
                return null;

            Sanitize(theme);
            return theme;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter ThemeStoreTests`
Expected: PASS (8 test).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Services/ThemeStore.cs FileExplorer.Tests/ThemeStoreTests.cs
git commit -m "feat(themes): ThemeStore con sanitizzazione e import/export"
```

---

### Task 3: Palette.axaml (accent nelle ThemeDictionaries) + AppSettings.CustomThemeId

**Model:** haiku

**Files:**
- Modify: `FileExplorer/Styles/Palette.axaml`
- Modify: `FileExplorer/Models/AppSettings.cs`

**Interfaces:**
- Consumes: nulla.
- Produces: `AppSettings.CustomThemeId : string?` (default null); Palette con `Brush.Accent`, `Brush.AccentGradient`, `Brush.OnAccent` dentro ENTRAMBE le varianti (valori identici a oggi).

- [ ] **Step 1: Verify no StaticResource usage on accent brushes**

Run: `grep -rn "StaticResource Brush\." FileExplorer --include="*.axaml" --include="*.cs" | grep -v obj/`
Expected: nessun risultato (tutto DynamicResource/FindResource). Se compaiono risultati, segnalarlo nel report del task PRIMA di procedere.

- [ ] **Step 2: Move accent brushes into both theme dictionaries**

In `Palette.axaml`: eliminare le tre risorse globali in testa (`Brush.Accent`, `Brush.AccentGradient`, `Brush.OnAccent`) e aggiungere in TESTA a ciascuna delle due ResourceDictionary `Light` e `Dark` (stessi valori in entrambe):

```xml
<SolidColorBrush x:Key="Brush.Accent" Color="#FF5E62" />
<LinearGradientBrush x:Key="Brush.AccentGradient" StartPoint="0%,0%" EndPoint="100%,0%">
  <GradientStop Color="#FF5E62" Offset="0" />
  <GradientStop Color="#FF9446" Offset="1" />
</LinearGradientBrush>
<SolidColorBrush x:Key="Brush.OnAccent" Color="White" />
```

Aggiornare il commento in testa al file: le risorse accent sono duplicate nelle due varianti per consentire l'override per-tema (vedi ThemeService).

- [ ] **Step 3: Add CustomThemeId to AppSettings**

In `AppSettings.cs`, dopo `ThemeVariant`:

```csharp
    /// <summary>Id del tema custom attivo (file in AppData/themes); null = usa ThemeVariant.</summary>
    public string? CustomThemeId { get; set; }
```

- [ ] **Step 4: Build and run the app smoke test**

Run: `dotnet build FileExplorer.sln`
Expected: build OK, zero warning nuovi.
Run: `dotnet test`
Expected: PASS (nessuna regressione, `AppSettingsStoreTests` inclusi).

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Styles/Palette.axaml FileExplorer/Models/AppSettings.cs
git commit -m "feat(themes): accent per-variante in Palette e CustomThemeId nelle impostazioni"
```

---

### Task 4: ThemeService + applicazione all'avvio

**Model:** opus

**Files:**
- Create: `FileExplorer/Services/ThemeService.cs`
- Modify: `FileExplorer/App.axaml.cs`
- Test: `FileExplorer.Tests/ThemeServiceTests.cs`

**Interfaces:**
- Consumes: `ColorTheme`, `ThemeColorKeys`, `BuiltInThemes`, `ThemeStore.Load`, `AppSettings.CustomThemeId`.
- Produces (statiche su `ThemeService`):
  - `void Apply(ColorTheme theme)` — registra/aggiorna la variante custom e la attiva
  - `void Revert(string themeVariantSetting)` — rimuove variante custom, torna a Default/Light/Dark
  - `void UpdateColor(string key, Color color)` — anteprima live: muta i brush del tema attivo
  - `internal static ResourceDictionary BuildDictionary(ColorTheme theme)` — testabile headless

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/ThemeServiceTests.cs
using Avalonia.Controls;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public class ThemeServiceTests
{
    [Fact]
    public void BuildDictionary_contains_all_brush_keys()
    {
        ResourceDictionary dict = ThemeService.BuildDictionary(BuiltInThemes.ForVariant("Light"));

        foreach (string key in ThemeColorKeys.All)
        {
            if (key is ThemeColorKeys.AccentGradientStart or ThemeColorKeys.AccentGradientEnd)
                continue;
            Assert.True(dict.ContainsKey("Brush." + key), $"brush mancante: Brush.{key}");
            Assert.IsType<SolidColorBrush>(dict["Brush." + key]);
        }
        Assert.True(dict.ContainsKey("Brush.AccentGradient"));
    }

    [Fact]
    public void BuildDictionary_gradient_has_two_stops_from_theme()
    {
        ColorTheme theme = BuiltInThemes.ForVariant("Light");
        theme.Colors[ThemeColorKeys.AccentGradientStart] = "#111111";
        theme.Colors[ThemeColorKeys.AccentGradientEnd] = "#222222";

        ResourceDictionary dict = ThemeService.BuildDictionary(theme);

        var gradient = Assert.IsType<LinearGradientBrush>(dict["Brush.AccentGradient"]);
        Assert.Equal(2, gradient.GradientStops.Count);
        Assert.Equal(Color.Parse("#111111"), gradient.GradientStops[0].Color);
        Assert.Equal(Color.Parse("#222222"), gradient.GradientStops[1].Color);
    }

    [Fact]
    public void BuildDictionary_invalid_hex_falls_back_to_base_variant()
    {
        ColorTheme theme = BuiltInThemes.ForVariant("Dark");
        theme.Colors[ThemeColorKeys.Surface] = "spazzatura";

        ResourceDictionary dict = ThemeService.BuildDictionary(theme);

        var surface = Assert.IsType<SolidColorBrush>(dict["Brush.Surface"]);
        Assert.Equal(Color.Parse("#1E1B1A"), surface.Color);
    }

    [Fact]
    public void Apply_and_UpdateColor_without_application_do_not_throw()
    {
        // nei test Application.Current è null: le API runtime devono essere no-op sicure.
        ThemeService.Apply(BuiltInThemes.ForVariant("Light"));
        ThemeService.UpdateColor(ThemeColorKeys.Accent, Colors.Red);
        ThemeService.Revert("Default");
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter ThemeServiceTests`
Expected: FAIL (ThemeService non esiste).

- [ ] **Step 3: Implement ThemeService**

```csharp
// FileExplorer/Services/ThemeService.cs
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Applica i temi custom a runtime. Il tema viene tradotto in un ResourceDictionary di brush e
/// registrato in Application.Resources.ThemeDictionaries con chiave ThemeVariant("Custom", base):
/// le chiavi non coperte risalgono per ereditarietà alla variante base di Palette.axaml, e i
/// controlli Fluent nativi seguono la variante ereditata (Light/Dark).
/// </summary>
public static class ThemeService
{
    /// <summary>Dizionario attivo, mantenuto per la mutazione live dei brush (anteprima editor).</summary>
    private static ResourceDictionary? _activeDictionary;

    /// <summary>Registra il tema come variante "Custom" e la attiva.</summary>
    public static void Apply(ColorTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        ThemeVariant baseVariant = theme.BaseVariant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var variant = new ThemeVariant("Custom", baseVariant);
        ResourceDictionary dict = BuildDictionary(theme);

        app.Resources.ThemeDictionaries.Remove(variant);
        app.Resources.ThemeDictionaries.Add(variant, dict);
        _activeDictionary = dict;

        // Doppia assegnazione: forza ActualThemeVariantChanged (e quindi il refresh di
        // DynamicResource e dei controlli custom) anche se la chiave "Custom" era già attiva.
        app.RequestedThemeVariant = baseVariant;
        app.RequestedThemeVariant = variant;
    }

    /// <summary>Rimuove la variante custom e torna a Sistema/Chiaro/Scuro.</summary>
    public static void Revert(string themeVariantSetting)
    {
        _activeDictionary = null;
        if (Application.Current is not { } app)
            return;

        app.Resources.ThemeDictionaries.Remove(new ThemeVariant("Custom", null));
        app.RequestedThemeVariant = themeVariantSetting switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>
    /// Anteprima live dall'editor: muta il colore del brush attivo senza ricostruire il
    /// dizionario (i brush sono AvaloniaObject: la modifica ridisegna subito i controlli).
    /// </summary>
    public static void UpdateColor(string key, Color color)
    {
        if (_activeDictionary is null)
            return;

        if (key is ThemeColorKeys.AccentGradientStart or ThemeColorKeys.AccentGradientEnd)
        {
            if (_activeDictionary["Brush.AccentGradient"] is LinearGradientBrush gradient)
            {
                int index = key == ThemeColorKeys.AccentGradientStart ? 0 : 1;
                gradient.GradientStops[index].Color = color;
            }
            return;
        }

        if (_activeDictionary.TryGetValue("Brush." + key, out object? value) && value is SolidColorBrush brush)
            brush.Color = color;
    }

    /// <summary>Traduce il tema in brush; hex invalidi ripiegano sul built-in della BaseVariant.</summary>
    internal static ResourceDictionary BuildDictionary(ColorTheme theme)
    {
        ColorTheme fallback = BuiltInThemes.ForVariant(theme.BaseVariant);
        var dict = new ResourceDictionary();

        foreach (string key in ThemeColorKeys.All)
        {
            if (key is ThemeColorKeys.AccentGradientStart or ThemeColorKeys.AccentGradientEnd)
                continue;
            dict["Brush." + key] = new SolidColorBrush(GetColor(theme, fallback, key));
        }

        dict["Brush.AccentGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(GetColor(theme, fallback, ThemeColorKeys.AccentGradientStart), 0),
                new GradientStop(GetColor(theme, fallback, ThemeColorKeys.AccentGradientEnd), 1)
            }
        };

        return dict;
    }

    private static Color GetColor(ColorTheme theme, ColorTheme fallback, string key)
    {
        if (theme.Colors.TryGetValue(key, out string? hex) && Color.TryParse(hex, out Color color))
            return color;
        return Color.Parse(fallback.Colors[key]);
    }
}
```

- [ ] **Step 4: Hook startup in App.axaml.cs**

Sostituire la riga `RequestedThemeVariant = ParseThemeVariant(...)` in `OnFrameworkInitializationCompleted` con:

```csharp
            AppSettingsStore.LoadCurrent();
            ColorTheme? customTheme = AppSettingsStore.Current.CustomThemeId is { } themeId
                ? ThemeStore.Load(themeId)
                : null;
            if (customTheme is not null)
                ThemeService.Apply(customTheme);
            else
                RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);
```

Aggiungere `using FileExplorer.Models;` in testa al file.

- [ ] **Step 5: Run tests and build, verify pass**

Run: `dotnet test --filter ThemeServiceTests` → PASS (4 test).
Run: `dotnet build FileExplorer.sln` → OK.

- [ ] **Step 6: Commit**

```bash
git add FileExplorer/Services/ThemeService.cs FileExplorer/App.axaml.cs FileExplorer.Tests/ThemeServiceTests.cs
git commit -m "feat(themes): ThemeService con variante custom e applicazione all'avvio"
```

---

### Task 5: ThemeEditorViewModel + ThemeEditorWindow (editor palette con anteprima live)

**Model:** opus

**Files:**
- Modify: `FileExplorer/FileExplorer.csproj` (PackageReference `Avalonia.Controls.ColorPicker`)
- Modify: `FileExplorer/App.axaml` (StyleInclude tema ColorPicker)
- Create: `FileExplorer/ViewModels/ThemeEditorViewModel.cs`
- Create: `FileExplorer/Views/ThemeEditorWindow.axaml`
- Create: `FileExplorer/Views/ThemeEditorWindow.axaml.cs`
- Test: `FileExplorer.Tests/ThemeEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `ColorTheme.Clone()`, `ThemeColorKeys.All`, `ThemeStore.SaveAsync`, `ThemeService.Apply/UpdateColor`.
- Produces:
  - `ThemeEditorViewModel(ColorTheme themeToEdit)` — lavora su un clone; `Name : string`, `IsDarkBase : bool`, `Groups : IReadOnlyList<ThemeColorGroupViewModel>`, `WorkingTheme : ColorTheme`, `Task<ColorTheme> SaveAsync()` (sanitizza+salva+ritorna il tema), `bool LivePreview { get; set; }` (default true; false nei test: nessuna chiamata a ThemeService)
  - `ThemeColorGroupViewModel { string Title; IReadOnlyList<ThemeColorEntryViewModel> Entries; }`
  - `ThemeColorEntryViewModel { string Key; string Label; Color Color { get; set; } }` — il setter aggiorna `WorkingTheme.Colors[Key]` e, se LivePreview, chiama `ThemeService.UpdateColor`
  - `ThemeEditorWindow(ThemeEditorViewModel vm)` — `ShowDialog<ColorTheme?>`: tema salvato o null se annullato

- [ ] **Step 1: Add ColorPicker package and theme**

In `FileExplorer/FileExplorer.csproj`, accanto agli altri pacchetti Avalonia:

```xml
    <PackageReference Include="Avalonia.Controls.ColorPicker" Version="$(AvaloniaVersion)" />
```

In `FileExplorer/App.axaml`, dopo lo StyleInclude del DataGrid:

```xml
    <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />
```

Run: `dotnet build FileExplorer.sln` → OK.

- [ ] **Step 2: Write the failing tests**

```csharp
// FileExplorer.Tests/ThemeEditorViewModelTests.cs
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public class ThemeEditorViewModelTests : IDisposable
{
    private readonly string _dir;

    public ThemeEditorViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-themeeditor-" + Guid.NewGuid().ToString("N"));
        ThemeStore.ThemesDirectory = _dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ThemeEditorViewModel MakeVm(out ColorTheme original)
    {
        original = BuiltInThemes.ForVariant("Light");
        original.Id = Guid.NewGuid().ToString("N");
        original.Name = "Base";
        original.IsBuiltIn = false;
        return new ThemeEditorViewModel(original) { LivePreview = false };
    }

    [Fact]
    public void Editor_works_on_a_clone_not_the_original()
    {
        ThemeEditorViewModel vm = MakeVm(out ColorTheme original);

        vm.Name = "Modificato";
        ThemeColorEntryViewModel accent = vm.Groups.SelectMany(g => g.Entries).First(e => e.Key == ThemeColorKeys.Accent);
        accent.Color = Colors.Lime;

        Assert.Equal("Base", original.Name);
        Assert.NotEqual("#FF00FF00", original.Colors[ThemeColorKeys.Accent]);
    }

    [Fact]
    public void Entries_cover_all_keys()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        var keys = vm.Groups.SelectMany(g => g.Entries).Select(e => e.Key).ToHashSet();
        foreach (string key in ThemeColorKeys.All)
            Assert.Contains(key, keys);
    }

    [Fact]
    public void Setting_entry_color_updates_working_theme_hex()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        ThemeColorEntryViewModel surface = vm.Groups.SelectMany(g => g.Entries).First(e => e.Key == ThemeColorKeys.Surface);

        surface.Color = Color.Parse("#123456");

        Assert.Equal("#FF123456", vm.WorkingTheme.Colors[ThemeColorKeys.Surface]);
    }

    [Fact]
    public async Task SaveAsync_persists_theme_with_edits()
    {
        ThemeEditorViewModel vm = MakeVm(out ColorTheme original);
        vm.Name = "Salvato";

        ColorTheme saved = await vm.SaveAsync();

        Assert.Equal(original.Id, saved.Id);
        ColorTheme? reloaded = ThemeStore.Load(original.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Salvato", reloaded.Name);
    }

    [Fact]
    public void IsDarkBase_maps_base_variant()
    {
        ThemeEditorViewModel vm = MakeVm(out _);
        Assert.False(vm.IsDarkBase);

        vm.IsDarkBase = true;

        Assert.Equal("Dark", vm.WorkingTheme.BaseVariant);
    }
}
```

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test --filter ThemeEditorViewModelTests`
Expected: FAIL (tipi non esistenti).

- [ ] **Step 4: Implement ThemeEditorViewModel**

```csharp
// FileExplorer/ViewModels/ThemeEditorViewModel.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Editor di un tema custom: lavora su un clone del tema ricevuto, espone i colori raggruppati
/// e (se <see cref="LivePreview"/>) propaga ogni modifica a <see cref="ThemeService.UpdateColor"/>.
/// Il salvataggio persiste via <see cref="ThemeStore"/>; l'annullamento è gestito dal chiamante.
/// </summary>
public class ThemeEditorViewModel : ViewModelBase
{
    public ColorTheme WorkingTheme { get; }

    /// <summary>False nei test: evita di toccare Application.Current.</summary>
    public bool LivePreview { get; set; } = true;

    public IReadOnlyList<ThemeColorGroupViewModel> Groups { get; }

    public ThemeEditorViewModel(ColorTheme themeToEdit)
    {
        WorkingTheme = themeToEdit.Clone();
        Groups =
        [
            new ThemeColorGroupViewModel("Accent", this,
            [
                (ThemeColorKeys.Accent, "Accent"),
                (ThemeColorKeys.AccentGradientStart, "Gradiente: inizio"),
                (ThemeColorKeys.AccentGradientEnd, "Gradiente: fine"),
                (ThemeColorKeys.OnAccent, "Testo su accent")
            ]),
            new ThemeColorGroupViewModel("Base", this,
            [
                (ThemeColorKeys.Surface, "Sfondo"),
                (ThemeColorKeys.Card, "Card"),
                (ThemeColorKeys.CardBorder, "Bordo card"),
                (ThemeColorKeys.Field, "Campi di input")
            ]),
            new ThemeColorGroupViewModel("Testo", this,
            [
                (ThemeColorKeys.TextPrimary, "Testo principale"),
                (ThemeColorKeys.TextMuted, "Testo secondario")
            ]),
            new ThemeColorGroupViewModel("Badge di stato", this,
            [
                (ThemeColorKeys.SuccessBg, "Successo: sfondo"),
                (ThemeColorKeys.SuccessFg, "Successo: testo"),
                (ThemeColorKeys.WarningBg, "Avviso: sfondo"),
                (ThemeColorKeys.WarningFg, "Avviso: testo"),
                (ThemeColorKeys.ErrorBg, "Errore: sfondo"),
                (ThemeColorKeys.ErrorFg, "Errore: testo"),
                (ThemeColorKeys.ProgressBg, "In corso: sfondo"),
                (ThemeColorKeys.ProgressFg, "In corso: testo"),
                (ThemeColorKeys.NeutralBg, "Neutro: sfondo"),
                (ThemeColorKeys.NeutralFg, "Neutro: testo")
            ]),
            new ThemeColorGroupViewModel("Treemap", this,
            [
                (ThemeColorKeys.Treemap1, "Colore 1"),
                (ThemeColorKeys.Treemap2, "Colore 2"),
                (ThemeColorKeys.Treemap3, "Colore 3"),
                (ThemeColorKeys.Treemap4, "Colore 4"),
                (ThemeColorKeys.Treemap5, "Colore 5"),
                (ThemeColorKeys.Treemap6, "Colore 6")
            ]),
            new ThemeColorGroupViewModel("Grafico velocità", this,
            [
                (ThemeColorKeys.SparklineLine, "Linea"),
                (ThemeColorKeys.SparklineFill, "Riempimento")
            ])
        ];
    }

    public string Name
    {
        get => WorkingTheme.Name;
        set
        {
            if (WorkingTheme.Name == value)
                return;
            WorkingTheme.Name = value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Base scura: cambia la variante ereditata (controlli nativi e fallback).</summary>
    public bool IsDarkBase
    {
        get => WorkingTheme.BaseVariant == "Dark";
        set
        {
            string variant = value ? "Dark" : "Light";
            if (WorkingTheme.BaseVariant == variant)
                return;
            WorkingTheme.BaseVariant = variant;
            this.RaisePropertyChanged();
            if (LivePreview)
                ThemeService.Apply(WorkingTheme);
        }
    }

    /// <summary>Chiamata dalle entry a ogni modifica colore.</summary>
    internal void OnColorChanged(string key, Color color)
    {
        WorkingTheme.Colors[key] = color.ToString().ToUpperInvariant();
        if (LivePreview)
            ThemeService.UpdateColor(key, color);
    }

    internal Color CurrentColor(string key) =>
        Color.TryParse(WorkingTheme.Colors.GetValueOrDefault(key), out Color color)
            ? color
            : Color.Parse(BuiltInThemes.ForVariant(WorkingTheme.BaseVariant).Colors[key]);

    /// <summary>Sanitizza e persiste il tema; ritorna il tema salvato.</summary>
    public async Task<ColorTheme> SaveAsync()
    {
        await ThemeStore.SaveAsync(WorkingTheme);
        return WorkingTheme;
    }
}

/// <summary>Gruppo di colori nell'editor (titolo + righe).</summary>
public class ThemeColorGroupViewModel
{
    public string Title { get; }
    public IReadOnlyList<ThemeColorEntryViewModel> Entries { get; }

    internal ThemeColorGroupViewModel(string title, ThemeEditorViewModel owner, (string Key, string Label)[] entries)
    {
        Title = title;
        var list = new List<ThemeColorEntryViewModel>();
        foreach ((string key, string label) in entries)
            list.Add(new ThemeColorEntryViewModel(owner, key, label));
        Entries = list;
    }
}

/// <summary>Riga dell'editor: una chiave colore con etichetta e valore corrente.</summary>
public class ThemeColorEntryViewModel : ViewModelBase
{
    private readonly ThemeEditorViewModel _owner;
    private Color _color;

    public string Key { get; }
    public string Label { get; }

    internal ThemeColorEntryViewModel(ThemeEditorViewModel owner, string key, string label)
    {
        _owner = owner;
        Key = key;
        Label = label;
        _color = owner.CurrentColor(key);
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value)
                return;
            _color = value;
            this.RaisePropertyChanged();
            _owner.OnColorChanged(Key, value);
        }
    }
}
```

Nota per il test `Setting_entry_color_updates_working_theme_hex`: `Color.ToString()` in Avalonia produce `#AARRGGBB` (es. `#FF123456`) — il test lo riflette.

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test --filter ThemeEditorViewModelTests`
Expected: PASS (5 test).

- [ ] **Step 6: Implement the window**

```xml
<!-- FileExplorer/Views/ThemeEditorWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:FileExplorer.ViewModels"
        x:Class="FileExplorer.Views.ThemeEditorWindow"
        x:DataType="vm:ThemeEditorViewModel"
        Title="Modifica tema"
        Width="560" Height="680"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface}">

  <DockPanel Margin="16">

    <StackPanel DockPanel.Dock="Top" Spacing="10">
      <Grid ColumnDefinitions="Auto,*" VerticalAlignment="Center">
        <TextBlock Grid.Column="0" Text="Nome" VerticalAlignment="Center" Margin="0,0,10,0"
                   Foreground="{DynamicResource Brush.TextPrimary}" />
        <TextBox Grid.Column="1" Text="{Binding Name}" />
      </Grid>
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Grid.Column="0" Text="Base scura (controlli nativi e fallback)"
                   VerticalAlignment="Center" Foreground="{DynamicResource Brush.TextPrimary}" />
        <ToggleSwitch Grid.Column="1" IsChecked="{Binding IsDarkBase}" />
      </Grid>
    </StackPanel>

    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right"
                Spacing="10" Margin="0,12,0,0">
      <Button Classes="secondary" Content="Annulla" Click="OnCancelClick" />
      <Button Classes="primary" Content="Salva" Click="OnSaveClick" />
    </StackPanel>

    <ScrollViewer Margin="0,12,0,0">
      <ItemsControl ItemsSource="{Binding Groups}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="vm:ThemeColorGroupViewModel">
            <Border Classes="card" Margin="0,0,0,10">
              <StackPanel Spacing="8">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold"
                           Foreground="{DynamicResource Brush.TextPrimary}" />
                <ItemsControl ItemsSource="{Binding Entries}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:ThemeColorEntryViewModel">
                      <Grid ColumnDefinitions="*,Auto" Margin="0,2">
                        <TextBlock Grid.Column="0" Text="{Binding Label}" VerticalAlignment="Center"
                                   Foreground="{DynamicResource Brush.TextPrimary}" />
                        <ColorPicker Grid.Column="1" Width="130" Color="{Binding Color}" />
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

  </DockPanel>

</Window>
```

```csharp
// FileExplorer/Views/ThemeEditorWindow.axaml.cs
using Avalonia.Controls;
using Avalonia.Interactivity;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Finestra editor tema. Chiusa con ShowDialog&lt;ColorTheme?&gt;: il tema salvato, oppure
/// null se annullata (il ripristino dell'anteprima è a carico del chiamante).
/// </summary>
public partial class ThemeEditorWindow : Window
{
    private readonly ThemeEditorViewModel _viewModel;

    public ThemeEditorWindow(ThemeEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var saved = await _viewModel.SaveAsync();
        Close(saved);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
```

- [ ] **Step 7: Build, verify OK**

Run: `dotnet build FileExplorer.sln` → OK. `dotnet test` → PASS completo.

- [ ] **Step 8: Commit**

```bash
git add FileExplorer/FileExplorer.csproj FileExplorer/App.axaml FileExplorer/ViewModels/ThemeEditorViewModel.cs FileExplorer/Views/ThemeEditorWindow.axaml FileExplorer/Views/ThemeEditorWindow.axaml.cs FileExplorer.Tests/ThemeEditorViewModelTests.cs
git commit -m "feat(themes): editor tema con ColorPicker e anteprima live"
```

---

### Task 6: SettingsViewModel — gestione temi

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/ViewModels/SettingsViewModel.cs`
- Test: `FileExplorer.Tests/SettingsViewModelTests.cs` (aggiunta di una classe `SettingsViewModelThemeTests` nello stesso file o file nuovo `SettingsViewModelThemeTests.cs` — preferire file nuovo)

**Interfaces:**
- Consumes: `ThemeStore`, `ThemeService`, `BuiltInThemes`, `AppSettings.CustomThemeId`, pattern auto-save esistente (`SaveCurrent`/`LastSaveTask`).
- Produces (su `SettingsViewModel`):
  - `ObservableCollection<ColorTheme> CustomThemes` — caricata nel costruttore da `ThemeStore.LoadAll()`
  - `ColorTheme? ActiveCustomTheme { get; }` — risolto da `CustomThemeId`
  - `bool HasCustomThemes => CustomThemes.Count > 0`
  - `void ApplyCustomTheme(ColorTheme theme)` — set `CustomThemeId`, `ThemeService.Apply`, save, aggiorna radio (nessuna selezionata)
  - `ColorTheme CreateThemeFrom(ColorTheme source)` — clone con nuovo Id, nome `"<Name> (copia)"`, NON ancora persistito (lo persiste l'editor al Salva)
  - `Task DeleteThemeAsync(ColorTheme theme)` — rimuove da store e lista; se era attivo → `CustomThemeId = null` + `ThemeService.Revert(ThemeVariant)` + save
  - `void OnThemeSaved(ColorTheme theme)` — upsert nella lista (per Id) + se è il tema attivo o nessun custom attivo era selezionato non fa altro; chiamata dal code-behind dopo l'editor
  - `Task ExportThemeAsync(ColorTheme theme, string path)` / `Task<ColorTheme?> ImportThemeAsync(string path)` (import: salva subito via `ThemeStore.SaveAsync` e aggiunge alla lista)
  - I setter dei radio `IsThemeDefault/Light/Dark` (via `ThemeVariant`) DEVONO azzerare `CustomThemeId` e chiamare `ThemeService.Revert(value)` al posto dell'attuale `ApplyThemeVariant`
  - `internal bool ApplyThemesToApplication { get; set; } = true` — false nei test: salta le chiamate a `ThemeService` (che comunque sono no-op senza Application, ma il flag rende l'intento esplicito e i test deterministici)

- [ ] **Step 1: Write the failing tests**

```csharp
// FileExplorer.Tests/SettingsViewModelThemeTests.cs
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public class SettingsViewModelThemeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;

    public SettingsViewModelThemeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-vmthemes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ThemeStore.ThemesDirectory = Path.Combine(_dir, "themes");
        _settingsPath = Path.Combine(_dir, "settings.json");
        AppSettingsStore.CurrentPath = _settingsPath;
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.CurrentPath = AppSettingsStore.DefaultPath;
        AppSettingsStore.Current = new AppSettings();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static SettingsViewModel MakeVm() => new() { ApplyThemesToApplication = false };

    private static ColorTheme MakeCustom(string name)
    {
        ColorTheme theme = BuiltInThemes.ForVariant("Light");
        theme.Id = Guid.NewGuid().ToString("N");
        theme.Name = name;
        theme.IsBuiltIn = false;
        return theme;
    }

    [Fact]
    public async Task Constructor_loads_saved_custom_themes()
    {
        await ThemeStore.SaveAsync(MakeCustom("Mio"));

        SettingsViewModel vm = MakeVm();

        ColorTheme loaded = Assert.Single(vm.CustomThemes);
        Assert.Equal("Mio", loaded.Name);
    }

    [Fact]
    public async Task ApplyCustomTheme_sets_id_and_saves()
    {
        ColorTheme theme = MakeCustom("Attivo");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();

        vm.ApplyCustomTheme(vm.CustomThemes[0]);
        if (vm.LastSaveTask is not null)
            await vm.LastSaveTask;

        Assert.Equal(theme.Id, AppSettingsStore.Current.CustomThemeId);
        Assert.Equal(theme.Id, vm.ActiveCustomTheme?.Id);
    }

    [Fact]
    public async Task Selecting_base_variant_clears_custom_theme()
    {
        ColorTheme theme = MakeCustom("Attivo");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();
        vm.ApplyCustomTheme(vm.CustomThemes[0]);

        vm.IsThemeDark = true;
        if (vm.LastSaveTask is not null)
            await vm.LastSaveTask;

        Assert.Null(AppSettingsStore.Current.CustomThemeId);
        Assert.Equal("Dark", AppSettingsStore.Current.ThemeVariant);
    }

    [Fact]
    public void CreateThemeFrom_builtin_gives_editable_copy()
    {
        SettingsViewModel vm = MakeVm();

        ColorTheme copy = vm.CreateThemeFrom(BuiltInThemes.ForVariant("Dark"));

        Assert.False(copy.IsBuiltIn);
        Assert.NotEqual(BuiltInThemes.DarkId, copy.Id);
        Assert.Equal("Scuro (copia)", copy.Name);
    }

    [Fact]
    public async Task DeleteThemeAsync_active_theme_reverts_to_variant()
    {
        ColorTheme theme = MakeCustom("DaEliminare");
        await ThemeStore.SaveAsync(theme);
        SettingsViewModel vm = MakeVm();
        vm.ApplyCustomTheme(vm.CustomThemes[0]);

        await vm.DeleteThemeAsync(vm.CustomThemes[0]);

        Assert.Null(AppSettingsStore.Current.CustomThemeId);
        Assert.Empty(vm.CustomThemes);
        Assert.Null(ThemeStore.Load(theme.Id));
    }

    [Fact]
    public async Task ImportThemeAsync_adds_and_persists()
    {
        ColorTheme theme = MakeCustom("DaImportare");
        string exportPath = Path.Combine(_dir, "tema.json");
        await ThemeStore.ExportAsync(theme, exportPath);
        SettingsViewModel vm = MakeVm();

        ColorTheme? imported = await vm.ImportThemeAsync(exportPath);

        Assert.NotNull(imported);
        Assert.Single(vm.CustomThemes);
        Assert.NotNull(ThemeStore.Load(imported.Id));
    }

    [Fact]
    public void OnThemeSaved_upserts_list_by_id()
    {
        SettingsViewModel vm = MakeVm();
        ColorTheme theme = MakeCustom("Nuovo");

        vm.OnThemeSaved(theme);
        Assert.Single(vm.CustomThemes);

        ColorTheme renamed = theme.Clone();
        renamed.Name = "Rinominato";
        vm.OnThemeSaved(renamed);

        ColorTheme only = Assert.Single(vm.CustomThemes);
        Assert.Equal("Rinominato", only.Name);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test --filter SettingsViewModelThemeTests`
Expected: FAIL (membri non esistenti).

- [ ] **Step 3: Implement in SettingsViewModel**

Aggiungere `using System.Collections.ObjectModel;`, `using System.Linq;`, `using FileExplorer.Models;`. Nel costruttore, dopo la sottoscrizione ThrottleChanged:

```csharp
        foreach (ColorTheme theme in ThemeStore.LoadAll())
            CustomThemes.Add(theme);
```

Nuovi membri:

```csharp
    /// <summary>Temi custom salvati su disco, in ordine alfabetico.</summary>
    public ObservableCollection<ColorTheme> CustomThemes { get; } = new();

    public bool HasCustomThemes => CustomThemes.Count > 0;

    /// <summary>False nei test: evita di toccare Application.Current tramite ThemeService.</summary>
    internal bool ApplyThemesToApplication { get; set; } = true;

    /// <summary>Tema custom attivo risolto da CustomThemeId, o null.</summary>
    public ColorTheme? ActiveCustomTheme =>
        CustomThemes.FirstOrDefault(t => t.Id == AppSettingsStore.Current.CustomThemeId);

    /// <summary>Attiva un tema custom: persiste l'id e applica i colori.</summary>
    public void ApplyCustomTheme(ColorTheme theme)
    {
        AppSettingsStore.Current.CustomThemeId = theme.Id;
        if (ApplyThemesToApplication)
            ThemeService.Apply(theme);
        this.RaisePropertyChanged(nameof(ActiveCustomTheme));
        this.RaisePropertyChanged(nameof(IsThemeDefault));
        this.RaisePropertyChanged(nameof(IsThemeLight));
        this.RaisePropertyChanged(nameof(IsThemeDark));
        SaveCurrent();
    }

    /// <summary>Copia modificabile di un tema (anche built-in): nuovo Id, nome "(copia)". Non persistita.</summary>
    public ColorTheme CreateThemeFrom(ColorTheme source)
    {
        ColorTheme copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " (copia)";
        copy.IsBuiltIn = false;
        return copy;
    }

    /// <summary>Elimina un tema custom; se era attivo torna alla variante base corrente.</summary>
    public async Task DeleteThemeAsync(ColorTheme theme)
    {
        bool wasActive = AppSettingsStore.Current.CustomThemeId == theme.Id;
        ThemeStore.Delete(theme.Id);
        CustomThemes.Remove(theme);
        this.RaisePropertyChanged(nameof(HasCustomThemes));

        if (wasActive)
        {
            AppSettingsStore.Current.CustomThemeId = null;
            if (ApplyThemesToApplication)
                ThemeService.Revert(AppSettingsStore.Current.ThemeVariant);
            this.RaisePropertyChanged(nameof(ActiveCustomTheme));
            SaveCurrent();
        }

        if (LastSaveTask is not null)
            await LastSaveTask;
    }

    /// <summary>Upsert nella lista dopo un salvataggio dall'editor (match per Id).</summary>
    public void OnThemeSaved(ColorTheme theme)
    {
        ColorTheme? existing = CustomThemes.FirstOrDefault(t => t.Id == theme.Id);
        if (existing is not null)
            CustomThemes.Remove(existing);
        CustomThemes.Add(theme);
        this.RaisePropertyChanged(nameof(HasCustomThemes));
        this.RaisePropertyChanged(nameof(ActiveCustomTheme));
    }

    public Task ExportThemeAsync(ColorTheme theme, string path) => ThemeStore.ExportAsync(theme, path);

    /// <summary>Importa da file: sanitizza, persiste e aggiunge alla lista. Null se illeggibile.</summary>
    public async Task<ColorTheme?> ImportThemeAsync(string path)
    {
        ColorTheme? theme = ThemeStore.Import(path);
        if (theme is null)
            return null;

        await ThemeStore.SaveAsync(theme);
        OnThemeSaved(theme);
        return theme;
    }
```

Modificare il setter di `ThemeVariant` (radio): azzerare il tema custom e usare ThemeService:

```csharp
    public string ThemeVariant
    {
        get => AppSettingsStore.Current.ThemeVariant;
        set
        {
            bool hadCustom = AppSettingsStore.Current.CustomThemeId is not null;
            if (AppSettingsStore.Current.ThemeVariant == value && !hadCustom)
                return;

            AppSettingsStore.Current.ThemeVariant = value;
            AppSettingsStore.Current.CustomThemeId = null;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsThemeDefault));
            this.RaisePropertyChanged(nameof(IsThemeLight));
            this.RaisePropertyChanged(nameof(IsThemeDark));
            this.RaisePropertyChanged(nameof(ActiveCustomTheme));
            if (ApplyThemesToApplication)
                ThemeService.Revert(value);
            SaveCurrent();
        }
    }
```

Eliminare il metodo privato `ApplyThemeVariant` (sostituito da `ThemeService.Revert`, che ha lo stesso null-guard su `Application.Current`). I getter dei radio (`IsThemeDefault/Light/Dark`) devono risultare falsi quando un tema custom è attivo:

```csharp
    public bool IsThemeDefault
    {
        get => ThemeVariant == "Default" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Default"; }
    }

    public bool IsThemeLight
    {
        get => ThemeVariant == "Light" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Light"; }
    }

    public bool IsThemeDark
    {
        get => ThemeVariant == "Dark" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Dark"; }
    }
```

- [ ] **Step 4: Run ALL tests, verify pass (incl. pre-existing SettingsViewModelTests)**

Run: `dotnet test`
Expected: PASS. Se `SettingsViewModelTests` esistenti falliscono per il nuovo comportamento del setter (`!hadCustom` early-return), adeguare SOLO le asserzioni rese obsolete, documentando il perché nel commit.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/ViewModels/SettingsViewModel.cs FileExplorer.Tests/SettingsViewModelThemeTests.cs
git commit -m "feat(themes): gestione temi custom in SettingsViewModel"
```

---

### Task 7: SettingsView — card Temi + wiring dialoghi

**Model:** sonnet

**Files:**
- Modify: `FileExplorer/Views/SettingsView.axaml`
- Modify: `FileExplorer/Views/SettingsView.axaml.cs`

**Interfaces:**
- Consumes: tutti i membri di Task 6, `ThemeEditorWindow`/`ThemeEditorViewModel` (Task 5), `BuiltInThemes`, `ThemeService`, pattern `TopLevel.GetTopLevel(this)` + `StorageProvider` per i file picker.
- Produces: UI finale. Nessun consumatore successivo.

- [ ] **Step 1: Replace the "Aspetto" card**

Sostituire l'intera card "Aspetto" in `SettingsView.axaml` con:

```xml
        <Border Classes="card">
          <StackPanel Spacing="14">
            <TextBlock Text="Aspetto" FontSize="15" FontWeight="SemiBold" Foreground="{DynamicResource Brush.TextPrimary}" />

            <StackPanel Orientation="Horizontal" Spacing="16">
              <RadioButton GroupName="Theme" Content="Sistema" IsChecked="{Binding IsThemeDefault}" />
              <RadioButton GroupName="Theme" Content="Chiaro" IsChecked="{Binding IsThemeLight}" />
              <RadioButton GroupName="Theme" Content="Scuro" IsChecked="{Binding IsThemeDark}" />
            </StackPanel>

            <TextBlock Text="Temi personalizzati" FontWeight="SemiBold" Foreground="{DynamicResource Brush.TextPrimary}" />

            <ItemsControl ItemsSource="{Binding CustomThemes}" IsVisible="{Binding HasCustomThemes}">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto" Margin="0,2">
                    <TextBlock Grid.Column="0" Text="{Binding Name}" VerticalAlignment="Center"
                               Foreground="{DynamicResource Brush.TextPrimary}" />
                    <Button Grid.Column="1" Classes="iconbtn" i:Attached.Icon="fa-solid fa-check"
                            ToolTip.Tip="Applica" Click="OnApplyThemeClick" />
                    <Button Grid.Column="2" Classes="iconbtn" i:Attached.Icon="fa-solid fa-pen"
                            ToolTip.Tip="Modifica" Click="OnEditThemeClick" />
                    <Button Grid.Column="3" Classes="iconbtn" i:Attached.Icon="fa-solid fa-file-export"
                            ToolTip.Tip="Esporta" Click="OnExportThemeClick" />
                    <Button Grid.Column="4" Classes="iconbtn" i:Attached.Icon="fa-solid fa-trash"
                            ToolTip.Tip="Elimina" Click="OnDeleteThemeClick" />
                  </Grid>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="Nessun tema personalizzato: creane uno partendo da Chiaro o Scuro."
                       IsVisible="{Binding !HasCustomThemes}"
                       Foreground="{DynamicResource Brush.TextMuted}" TextWrapping="Wrap" />

            <StackPanel Orientation="Horizontal" Spacing="10">
              <Button Classes="secondary" Content="Nuovo da Chiaro" Click="OnNewFromLightClick" />
              <Button Classes="secondary" Content="Nuovo da Scuro" Click="OnNewFromDarkClick" />
              <Button Classes="secondary" Content="Importa…" Click="OnImportThemeClick" />
            </StackPanel>
          </StackPanel>
        </Border>
```

Nota: `x:DataType` del template item è `ColorTheme` — aggiungere `xmlns:m="using:FileExplorer.Models"` sulla root e `x:DataType="m:ColorTheme"` sul DataTemplate.

- [ ] **Step 2: Implement code-behind handlers**

In `SettingsView.axaml.cs` (creare i metodi; il file oggi contiene solo InitializeComponent):

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private static ColorTheme? ThemeOf(object? sender) =>
        (sender as Control)?.DataContext as ColorTheme;

    private void OnApplyThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            vm.ApplyCustomTheme(theme);
    }

    private async void OnEditThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            await OpenEditorAsync(vm, theme);
    }

    private async void OnNewFromLightClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await OpenEditorAsync(vm, vm.CreateThemeFrom(BuiltInThemes.Light));
    }

    private async void OnNewFromDarkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await OpenEditorAsync(vm, vm.CreateThemeFrom(BuiltInThemes.Dark));
    }

    private async void OnDeleteThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && ThemeOf(sender) is { } theme)
            await vm.DeleteThemeAsync(theme);
    }

    private async void OnExportThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || ThemeOf(sender) is not { } theme)
            return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Esporta tema",
            SuggestedFileName = theme.Name + ".json",
            FileTypeChoices = [new FilePickerFileType("Tema JSON") { Patterns = ["*.json"] }]
        });
        if (file?.TryGetLocalPath() is { } path)
            await vm.ExportThemeAsync(theme, path);
    }

    private async void OnImportThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importa tema",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Tema JSON") { Patterns = ["*.json"] }]
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await vm.ImportThemeAsync(path);
    }

    /// <summary>
    /// Apre l'editor con anteprima live sul tema indicato. All'annullamento ripristina lo
    /// stato precedente (tema custom attivo o variante base); al salvataggio attiva il tema.
    /// </summary>
    private async Task OpenEditorAsync(SettingsViewModel vm, ColorTheme theme)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        ColorTheme? previousActive = vm.ActiveCustomTheme;
        var editorVm = new ThemeEditorViewModel(theme);
        ThemeService.Apply(editorVm.WorkingTheme);

        var editor = new ThemeEditorWindow(editorVm);
        ColorTheme? saved = await editor.ShowDialog<ColorTheme?>(owner);

        if (saved is not null)
        {
            vm.OnThemeSaved(saved);
            vm.ApplyCustomTheme(saved);
        }
        else if (previousActive is not null)
        {
            ThemeService.Apply(previousActive);
        }
        else
        {
            ThemeService.Revert(AppSettingsStore.Current.ThemeVariant);
        }
    }
}
```

Nota: verificare il contenuto reale attuale di `SettingsView.axaml.cs` prima di sovrascrivere (deve restare solo l'aggiunta degli handler).

- [ ] **Step 3: Build + full test run**

Run: `dotnet build FileExplorer.sln` → OK. `dotnet test` → PASS completo.

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project FileExplorer.Desktop`
Verifiche manuali (elencarle nel report del task):
1. Impostazioni → "Nuovo da Chiaro" apre l'editor; cambiare Sfondo → la finestra dietro cambia colore live.
2. Salva → tema in lista e attivo; radio Sistema/Chiaro/Scuro deselezionati.
3. Riavvio app → tema custom ancora attivo.
4. Radio "Scuro" → torna il tema scuro standard, CustomThemeId azzerato.
5. Esporta → file JSON; Importa → nuovo tema in lista.
6. Elimina tema attivo → fallback alla variante base.
7. Annulla dall'editor → colori precedenti ripristinati.

- [ ] **Step 5: Commit**

```bash
git add FileExplorer/Views/SettingsView.axaml FileExplorer/Views/SettingsView.axaml.cs
git commit -m "feat(themes): card Temi in Impostazioni con editor, import/export"
```

---

### Task 8: Documentazione e chiusura

**Model:** haiku

**Files:**
- Modify: `CLAUDE.md` (sezione Styling)
- Modify: `docs/superpowers/plans/2026-08-19-user-themes.md` (spuntare i task)

**Interfaces:** nessuna.

- [ ] **Step 1: Update CLAUDE.md styling section**

Nella sezione Styling di `CLAUDE.md`, aggiungere in coda:

```markdown
Temi custom: `ThemeService` registra un ResourceDictionary per-tema come `ThemeVariant("Custom", base)` in `Application.Resources.ThemeDictionaries`; i valori built-in restano in `Palette.axaml` e fanno da fallback. Nuove chiavi colore vanno aggiunte in TUTTI e tre i posti: `Palette.axaml` (entrambe le varianti), `ThemeColorKeys`, `BuiltInThemes`.
```

- [ ] **Step 2: Final verification**

Run: `dotnet build FileExplorer.sln && dotnet test`
Expected: build OK, tutti i test PASS.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/superpowers/plans/2026-08-19-user-themes.md
git commit -m "docs(themes): documentazione meccanismo temi custom"
```

---

## Note di esecuzione

- Ordine obbligato: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 (dipendenze a catena).
- Rischio noto (Task 5): il nome esatto dello StyleInclude del ColorPicker per Avalonia 11.2.8 va verificato al primo build (`avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml`); in caso di errore consultare la documentazione del pacchetto per la versione 11.2.x.
- Rischio noto (Task 4): la doppia assegnazione di `RequestedThemeVariant` serve a forzare il refresh dei controlli custom (`TreemapControl`/`SparklineControl` ridisegnano solo su `ActualThemeVariantChanged`). Se in smoke test si nota un flash sgradevole, alternativa: invalidare i controlli via evento dedicato — decisione rimandata all'evidenza.
