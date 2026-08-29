using Avalonia.Controls;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class DiskUsageView : UserControl
{
    public DiskUsageView()
    {
        InitializeComponent();
        var viewModel = new DiskUsageViewModel();
        DataContext = viewModel;

        var hierarchyList = this.FindControl<HierarchyListControl>("HierarchyList")!;
        hierarchyList.NodeActivated += viewModel.DrillDown;
        viewModel.StructureUpdated += () => hierarchyList.Refresh();
    }
}
