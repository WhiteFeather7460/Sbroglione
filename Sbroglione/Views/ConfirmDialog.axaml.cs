using Avalonia.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Host desktop del dialogo di conferma: restituisce true su conferma, false su annulla/chiusura.
/// Il corpo vive in <see cref="ConfirmDialogContent"/>, condiviso con l'host overlay single-view.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        DialogContent.Completed += result => Close(result);
    }
}
