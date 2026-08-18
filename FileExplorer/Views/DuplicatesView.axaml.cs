using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView()
    {
        InitializeComponent();
        DataContext = new DuplicatesViewModel();
    }
}
