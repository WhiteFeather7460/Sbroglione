using Avalonia.Controls;

namespace Sbroglione.Views;

public partial class StorageAccessBanner : UserControl
{
    // Nessun ViewModel proprio: eredita il DataContext (MainWindowViewModel) dalla tab
    // che lo ospita, perche' il comando e il flag di permesso vivono nella shell.
    public StorageAccessBanner()
    {
        InitializeComponent();
    }
}
