using Avalonia.Controls;

using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class WatchFoldersView : UserControl
{
    public WatchFoldersView()
    {
        InitializeComponent();
        // Pattern del progetto: la tab crea il proprio ViewModel. Come le altre
        // view non dispone il VM IDisposable (la tab vive quanto la finestra).
        DataContext = new WatchFoldersViewModel();
    }
}
