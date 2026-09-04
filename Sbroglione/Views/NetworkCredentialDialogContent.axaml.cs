using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Sbroglione.Models;
using Sbroglione.ViewModels;
using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo del dialogo credenziali di rete, indipendente dall'host: restituisce il risultato
/// su Connetti, <c>null</c> su annulla.
/// </summary>
public partial class NetworkCredentialDialogContent : UserControl, IDialogContent<NetworkCredentialResult?>
{
    public NetworkCredentialDialogContent()
    {
        InitializeComponent();

        // Focus iniziale sull'utente: sull'host overlay non c'è un evento Opened di finestra.
        AttachedToVisualTree += (_, _) => FocusInput();
    }

    /// <inheritdoc />
    public event Action<NetworkCredentialResult?>? Completed;

    /// <summary>Focus sul campo utente; idempotente, invocabile anche dall'host che ospita il contenuto.</summary>
    public void FocusInput() => UsernameBox.Focus();

    public void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CompleteWithResult();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => CompleteWithResult();

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(null);

    private void CompleteWithResult()
    {
        if (DataContext is NetworkCredentialDialogViewModel vm && vm.CanConfirm)
            Completed?.Invoke(new NetworkCredentialResult(vm.Username.Trim(), vm.Password, vm.Remember));
    }
}
