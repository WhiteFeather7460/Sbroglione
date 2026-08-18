using Avalonia.Controls;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

public partial class ComparisonView : UserControl
{
    public ComparisonView()
    {
        InitializeComponent();
        DataContext = new ComparisonViewModel();
    }
}
