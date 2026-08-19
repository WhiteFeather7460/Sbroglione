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
