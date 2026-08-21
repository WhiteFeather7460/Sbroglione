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

        var treemap = this.FindControl<TreemapControl>("Treemap")!;
        treemap.NodeActivated += viewModel.DrillDown;

        var hierarchyList = this.FindControl<HierarchyListControl>("HierarchyList")!;
        hierarchyList.NodeActivated += viewModel.DrillDown;
    }
}
