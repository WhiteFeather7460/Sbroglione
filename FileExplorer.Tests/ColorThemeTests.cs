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
