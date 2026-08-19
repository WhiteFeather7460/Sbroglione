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
