using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class ComparisonView : UserControl
{
    public ComparisonView()
    {
        InitializeComponent();
        DataContext = new ComparisonViewModel();
    }
}
