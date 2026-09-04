using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>
/// Scheda "Copia file": lista di coppie sorgente/destinazione da copiare e verificare.
/// </summary>
public partial class CopyPairsView : UserControl
{
    // Colonne fisse (icona 44 + stato 34 + size 110 + modified 170 = 358) che sotto questa
    // larghezza non lascerebbero più spazio alla colonna nome ("*"): si nasconde "Data
    // modifica" per ridare respiro al nome file. Ogni coppia sorgente/destinazione ha la sua
    // DataGrid (item template), quindi l'handler è condiviso e distingue l'istanza da sender.
    private const double NarrowGridBreakpoint = 460;

    public CopyPairsView()
    {
        InitializeComponent();
        DataContext = new CopyPairsViewModel();
    }

    private void OnFilesGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.Columns.Count < 5)
            return;
        grid.Columns[4].IsVisible = e.NewSize.Width >= NarrowGridBreakpoint;
    }
}
