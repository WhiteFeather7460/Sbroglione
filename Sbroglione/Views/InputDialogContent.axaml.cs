using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Sbroglione.ViewModels;
using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo del dialogo di input testo, indipendente dall'host: restituisce il testo (trimmato)
/// su OK, <c>null</c> su annulla.
/// </summary>
public partial class InputDialogContent : UserControl, IDialogContent<string?>
{
    public InputDialogContent()
    {
        InitializeComponent();

        // Focus iniziale sul campo: sull'host overlay non c'è un evento Opened di finestra,
        // quindi il contenuto se ne occupa da sé all'aggancio all'albero visuale.
        AttachedToVisualTree += (_, _) => FocusInput();
    }

    /// <inheritdoc />
    public event Action<string?>? Completed;

    /// <summary>Focus sul campo di testo; idempotente, invocabile anche dall'host che ospita il contenuto.</summary>
    public void FocusInput() => InputBox.Focus();

    public void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CompleteWithText();
    }

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => CompleteWithText();

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(null);

    private void CompleteWithText()
    {
        if (DataContext is InputDialogViewModel vm && vm.CanConfirm)
            Completed?.Invoke(vm.Text.Trim());
    }
}
