using Avalonia.Headless.XUnit;

using Sbroglione.Views;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public class MainViewTests
{
    [AvaloniaFact]
    public void MainView_ConstructsWithoutWindow_AndAcceptsViewModel()
    {
        var view = new MainView
        {
            DataContext = new MainWindowViewModel()
        };

        Assert.NotNull(view.DataContext);
        Assert.IsType<MainWindowViewModel>(view.DataContext);
    }
}
