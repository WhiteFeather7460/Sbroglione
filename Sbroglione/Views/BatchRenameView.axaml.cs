using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class BatchRenameView : UserControl
{
    public BatchRenameView()
    {
        InitializeComponent();
        DataContext = new BatchRenameViewModel();
    }
}
