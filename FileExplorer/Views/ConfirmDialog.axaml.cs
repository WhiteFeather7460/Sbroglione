using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileExplorer.Views;

/// <summary>Dialog modale di conferma: restituisce true su conferma, false su annulla/chiusura.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
