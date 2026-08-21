using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>Dialog modale di input testo: restituisce il testo (trimmato) su OK, null su annulla/chiusura.</summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Opened += (_, _) => InputBox.Focus();
    }

    public void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CloseWithText();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => CloseWithText();

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void CloseWithText()
    {
        if (DataContext is InputDialogViewModel vm && vm.CanConfirm)
            Close(vm.Text.Trim());
    }
}
