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
