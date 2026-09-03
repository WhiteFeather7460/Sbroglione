using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sbroglione.Views;

namespace Sbroglione.Tests;

public class LocalPaneViewLayoutTests
{
    static LocalPaneViewLayoutTests()
    {
        AvaloniaTestHelper.Initialize();
    }

    [AvaloniaFact]
    public void FilterFlyout_Content_ShrinksInsteadOfForcingFixedWidth()
    {
        var view = new LocalPaneView();
        var filterButton = view.FindControl<Button>("FilterButton")!;
        var flyout = (Flyout)filterButton.Flyout!;
        var flyoutContent = (StackPanel)flyout.Content!;

        Assert.True(double.IsNaN(flyoutContent.Width),
            "Width should be unset (NaN) so the popup can shrink on narrow screens");
        Assert.Equal(360, flyoutContent.MaxWidth);
    }
}
