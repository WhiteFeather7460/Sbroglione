using Avalonia.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Host desktop del dialogo credenziali di rete: restituisce il risultato su Connetti,
/// null su annulla/chiusura. Il corpo vive in <see cref="NetworkCredentialDialogContent"/>,
/// condiviso con l'host overlay single-view.
/// </summary>
public partial class NetworkCredentialDialog : Window
{
    public NetworkCredentialDialog()
    {
        InitializeComponent();
        DialogContent.Completed += result => Close(result);

        // Il focus iniziale resta agganciato a Opened come prima: sul desktop la finestra
        // deve essere attiva perché Focus() attecchisca.
        Opened += (_, _) => DialogContent.FocusInput();
    }
}
