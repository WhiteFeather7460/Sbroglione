using System.Collections.Generic;
using Sbroglione.Models;

namespace Sbroglione.Services;

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
            [ThemeColorKeys.Accent] = "#0EA5A0",
            [ThemeColorKeys.AccentGradientStart] = "#0EA5A0",
            [ThemeColorKeys.AccentGradientEnd] = "#0EA5A0",
            [ThemeColorKeys.OnAccent] = "#FFFFFF",
            [ThemeColorKeys.Surface] = "#F7F8F8",
            [ThemeColorKeys.Card] = "#FFFFFF",
            [ThemeColorKeys.CardBorder] = "#E4E7E9",
            [ThemeColorKeys.Field] = "#EFF1F2",
            [ThemeColorKeys.TextPrimary] = "#26292C",
            [ThemeColorKeys.TextMuted] = "#6D7278",
            [ThemeColorKeys.SuccessBg] = "#E3F2EB",
            [ThemeColorKeys.SuccessFg] = "#1D7F56",
            [ThemeColorKeys.WarningBg] = "#F6EDD8",
            [ThemeColorKeys.WarningFg] = "#8F6400",
            [ThemeColorKeys.ErrorBg] = "#F9E3E1",
            [ThemeColorKeys.ErrorFg] = "#C23B2E",
            [ThemeColorKeys.ProgressBg] = "#DEF0EF",
            [ThemeColorKeys.ProgressFg] = "#0B7D79",
            [ThemeColorKeys.NeutralBg] = "#EBEDEE",
            [ThemeColorKeys.NeutralFg] = "#64696E",
            [ThemeColorKeys.Treemap1] = "#9DC3BC",
            [ThemeColorKeys.Treemap2] = "#D3C089",
            [ThemeColorKeys.Treemap3] = "#A9C29A",
            [ThemeColorKeys.Treemap4] = "#96B4CC",
            [ThemeColorKeys.Treemap5] = "#B5A7C9",
            [ThemeColorKeys.Treemap6] = "#C4AC9A",
            [ThemeColorKeys.SparklineLine] = "#0B7D79",
            [ThemeColorKeys.SparklineFill] = "#330EA5A0"
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
            [ThemeColorKeys.Accent] = "#0EA5A0",
            [ThemeColorKeys.AccentGradientStart] = "#0EA5A0",
            [ThemeColorKeys.AccentGradientEnd] = "#0EA5A0",
            [ThemeColorKeys.OnAccent] = "#FFFFFF",
            [ThemeColorKeys.Surface] = "#191B1E",
            [ThemeColorKeys.Card] = "#212428",
            [ThemeColorKeys.CardBorder] = "#2E3237",
            [ThemeColorKeys.Field] = "#2C2F34",
            [ThemeColorKeys.TextPrimary] = "#E8EAEC",
            [ThemeColorKeys.TextMuted] = "#9AA0A6",
            [ThemeColorKeys.SuccessBg] = "#20362C",
            [ThemeColorKeys.SuccessFg] = "#34B27D",
            [ThemeColorKeys.WarningBg] = "#3A3122",
            [ThemeColorKeys.WarningFg] = "#E0B25C",
            [ThemeColorKeys.ErrorBg] = "#3D2624",
            [ThemeColorKeys.ErrorFg] = "#F08080",
            [ThemeColorKeys.ProgressBg] = "#1E3534",
            [ThemeColorKeys.ProgressFg] = "#2DD4CD",
            [ThemeColorKeys.NeutralBg] = "#2C2F34",
            [ThemeColorKeys.NeutralFg] = "#A6ACB2",
            [ThemeColorKeys.Treemap1] = "#3E5A54",
            [ThemeColorKeys.Treemap2] = "#5A5133",
            [ThemeColorKeys.Treemap3] = "#45543C",
            [ThemeColorKeys.Treemap4] = "#3B4C5E",
            [ThemeColorKeys.Treemap5] = "#4C4258",
            [ThemeColorKeys.Treemap6] = "#574838",
            [ThemeColorKeys.SparklineLine] = "#2DD4CD",
            [ThemeColorKeys.SparklineFill] = "#332DD4CD"
        }
    };

    public static ColorTheme ForVariant(string variant) => variant == "Dark" ? Dark : Light;
}
