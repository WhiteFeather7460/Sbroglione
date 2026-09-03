using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Sbroglione.Views;

namespace Sbroglione.Tests;

public class DuplicatesViewLayoutTests
{
    [AvaloniaFact]
    public void CommandWrap_WrapsToMultipleRows_WhenNarrow_AndSingleRow_WhenWide()
    {
        var view = new DuplicatesView();
        var window = new Window { Content = view, Width = 100, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var wrap = view.FindControl<WrapPanel>("CommandWrap")!;
        double narrowHeight = wrap.Bounds.Height;

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        double wideHeight = wrap.Bounds.Height;

        window.Close();

        Assert.True(narrowHeight > wideHeight,
            $"expected command bar to wrap at 100px (height={narrowHeight}) vs single row at 1280px (height={wideHeight})");
    }
}
