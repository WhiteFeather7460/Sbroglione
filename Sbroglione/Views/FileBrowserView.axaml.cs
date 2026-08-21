using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

/// <summary>
/// Scheda "Esplora": navigazione del file system (in sviluppo).
/// </summary>
public partial class FileBrowserView : UserControl
{
    public FileBrowserView()
    {
        InitializeComponent();
        DataContext = new FileBrowserViewModel();
    }
}
