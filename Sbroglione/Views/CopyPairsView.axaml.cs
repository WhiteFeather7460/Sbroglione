using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

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
