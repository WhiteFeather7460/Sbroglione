using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Sbroglione.Models;
using Sbroglione.ViewModels;
using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo dell'editor tema, indipendente dall'host: segnala il tema salvato, o <c>null</c>
/// se annullato — stesso contratto di <see cref="ThemeEditorWindow"/> prima dell'estrazione.
/// </summary>
public partial class ThemeEditorContent : UserControl, IDialogContent<ColorTheme?>
{
    public ThemeEditorContent()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public event Action<ColorTheme?>? Completed;

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeEditorViewModel vm)
            return;

        var saved = await vm.SaveAsync();
        Completed?.Invoke(saved);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(null);
}
