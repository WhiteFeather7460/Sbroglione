using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public class ThemeStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _originalThemesDirectory;

    public ThemeStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fe-themes-" + Guid.NewGuid().ToString("N"));
        _originalThemesDirectory = ThemeStore.ThemesDirectory;
        ThemeStore.ThemesDirectory = _dir;
    }

    public void Dispose()
    {
        ThemeStore.ThemesDirectory = _originalThemesDirectory;
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
