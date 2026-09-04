using Avalonia.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Host desktop del dialogo di input testo: restituisce il testo (trimmato) su OK,
/// null su annulla/chiusura. Il corpo vive in <see cref="InputDialogContent"/>,
/// condiviso con l'host overlay single-view.
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        DialogContent.Completed += result => Close(result);

        // Il focus iniziale resta agganciato a Opened come prima: sul desktop la finestra
        // deve essere attiva perché Focus() attecchisca.
        Opened += (_, _) => DialogContent.FocusInput();
    }
}
