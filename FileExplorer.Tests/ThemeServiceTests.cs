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
        Assert.Equal(Color.Parse("#191B1E"), surface.Color);
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
