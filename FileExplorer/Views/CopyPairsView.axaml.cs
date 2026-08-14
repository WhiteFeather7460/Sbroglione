using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Scheda "Copia file": lista di coppie sorgente/destinazione da copiare e verificare.
/// </summary>
public partial class CopyPairsView : UserControl
{
    public CopyPairsView()
    {
        InitializeComponent();
        DataContext = new CopyPairsViewModel();
    }
}
