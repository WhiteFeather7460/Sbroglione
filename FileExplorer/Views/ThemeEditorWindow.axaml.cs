using Avalonia.Controls;
using Avalonia.Interactivity;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Finestra editor tema. Chiusa con ShowDialog&lt;ColorTheme?&gt;: il tema salvato, oppure
/// null se annullata (il ripristino dell'anteprima è a carico del chiamante).
/// </summary>
public partial class ThemeEditorWindow : Window
{
    private readonly ThemeEditorViewModel _viewModel;

    // Costruttore senza parametri richiesto dal designer Avalonia (e dal loader XAML runtime).
    public ThemeEditorWindow()
        : this(new ThemeEditorViewModel(BuiltInThemes.ForVariant("Light")) { LivePreview = false })
    {
    }

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
