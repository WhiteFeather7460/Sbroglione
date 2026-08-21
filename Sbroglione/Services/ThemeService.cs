using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Sbroglione.Models;

namespace Sbroglione.Services;

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
