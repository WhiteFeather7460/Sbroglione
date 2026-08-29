using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sbroglione.Models;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>Dialog modale credenziali di rete: restituisce il risultato su Connetti, null su annulla/chiusura.</summary>
public partial class NetworkCredentialDialog : Window
{
    public NetworkCredentialDialog()
    {
        InitializeComponent();
        Opened += (_, _) => UsernameBox.Focus();
    }

    public void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CloseWithResult();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => CloseWithResult();

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void CloseWithResult()
    {
        if (DataContext is NetworkCredentialDialogViewModel vm && vm.CanConfirm)
            Close(new NetworkCredentialResult(vm.Username.Trim(), vm.Password, vm.Remember));
    }
}
