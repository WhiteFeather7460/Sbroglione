using Avalonia.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Host desktop della selezione di un file o di una cartella: il percorso scelto viene
/// restituito come risultato del dialogo. Il corpo vive in
/// <see cref="SelectPathDialogContent"/>, condiviso con l'host overlay single-view.
/// </summary>
public partial class SelectPathDialog : Window
{
    public SelectPathDialog()
    {
        InitializeComponent();
        DialogContent.Completed += result => Close(result);

        // Il primo caricamento resta agganciato a Opened come prima (il contenuto lo avvia
        // comunque all'aggancio all'albero visuale; InitializeAsync è idempotente).
        Opened += async (_, _) => await DialogContent.InitializeAsync();
    }
}
