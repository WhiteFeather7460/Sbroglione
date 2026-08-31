using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo del dialogo di conferma, indipendente dall'host: <c>true</c> su conferma,
/// <c>false</c> su annulla.
/// </summary>
public partial class ConfirmDialogContent : UserControl, IDialogContent<bool?>
{
    public ConfirmDialogContent()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public event Action<bool?>? Completed;

    public void OnConfirmClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(true);

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(false);
}
