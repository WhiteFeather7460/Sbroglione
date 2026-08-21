using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView()
    {
        InitializeComponent();
        DataContext = new DuplicatesViewModel();
    }
}
