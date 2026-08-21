using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

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
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.Accent"), this,
            [
                (ThemeColorKeys.Accent, LocalizationService.Tr("Str.ThemeEditor.Accent")),
                (ThemeColorKeys.AccentGradientStart, LocalizationService.Tr("Str.ThemeEditor.GradientStart")),
                (ThemeColorKeys.AccentGradientEnd, LocalizationService.Tr("Str.ThemeEditor.GradientEnd")),
                (ThemeColorKeys.OnAccent, LocalizationService.Tr("Str.ThemeEditor.TextOnAccent"))
            ]),
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.SectionBase"), this,
            [
                (ThemeColorKeys.Surface, LocalizationService.Tr("Str.ThemeEditor.Background")),
                (ThemeColorKeys.Card, LocalizationService.Tr("Str.ThemeEditor.Card")),
                (ThemeColorKeys.CardBorder, LocalizationService.Tr("Str.ThemeEditor.CardBorder")),
                (ThemeColorKeys.Field, LocalizationService.Tr("Str.ThemeEditor.InputFields"))
            ]),
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.SectionText"), this,
            [
                (ThemeColorKeys.TextPrimary, LocalizationService.Tr("Str.ThemeEditor.TextPrimary")),
                (ThemeColorKeys.TextMuted, LocalizationService.Tr("Str.ThemeEditor.TextSecondary"))
            ]),
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.SectionBadges"), this,
            [
                (ThemeColorKeys.SuccessBg, LocalizationService.Tr("Str.ThemeEditor.SuccessBg")),
                (ThemeColorKeys.SuccessFg, LocalizationService.Tr("Str.ThemeEditor.SuccessFg")),
                (ThemeColorKeys.WarningBg, LocalizationService.Tr("Str.ThemeEditor.WarningBg")),
                (ThemeColorKeys.WarningFg, LocalizationService.Tr("Str.ThemeEditor.WarningFg")),
                (ThemeColorKeys.ErrorBg, LocalizationService.Tr("Str.ThemeEditor.ErrorBg")),
                (ThemeColorKeys.ErrorFg, LocalizationService.Tr("Str.ThemeEditor.ErrorFg")),
                (ThemeColorKeys.ProgressBg, LocalizationService.Tr("Str.ThemeEditor.ProgressBg")),
                (ThemeColorKeys.ProgressFg, LocalizationService.Tr("Str.ThemeEditor.ProgressFg")),
                (ThemeColorKeys.NeutralBg, LocalizationService.Tr("Str.ThemeEditor.NeutralBg")),
                (ThemeColorKeys.NeutralFg, LocalizationService.Tr("Str.ThemeEditor.NeutralFg"))
            ]),
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.SectionTreemap"), this,
            [
                (ThemeColorKeys.Treemap1, LocalizationService.Tr("Str.ThemeEditor.Color1")),
                (ThemeColorKeys.Treemap2, LocalizationService.Tr("Str.ThemeEditor.Color2")),
                (ThemeColorKeys.Treemap3, LocalizationService.Tr("Str.ThemeEditor.Color3")),
                (ThemeColorKeys.Treemap4, LocalizationService.Tr("Str.ThemeEditor.Color4")),
                (ThemeColorKeys.Treemap5, LocalizationService.Tr("Str.ThemeEditor.Color5")),
                (ThemeColorKeys.Treemap6, LocalizationService.Tr("Str.ThemeEditor.Color6"))
            ]),
            new ThemeColorGroupViewModel(LocalizationService.Tr("Str.ThemeEditor.SectionSpeedChart"), this,
            [
                (ThemeColorKeys.SparklineLine, LocalizationService.Tr("Str.ThemeEditor.Line")),
                (ThemeColorKeys.SparklineFill, LocalizationService.Tr("Str.ThemeEditor.Fill"))
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
