using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Sbroglione.Views;

namespace Sbroglione.Tests;

public class CopyPairsViewLayoutTests
{
    [AvaloniaFact]
    public void HeaderWrap_WrapsToMultipleRows_WhenNarrow_AndSingleRow_WhenWide()
    {
        var view = new CopyPairsView();
        // Il layout headless applica gli stili/template solo su controlli agganciati a un
        // TopLevel: Measure/Arrange su una view non attaccata restituisce sempre 0,0.
        var window = new Window { Content = view, Width = 360, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var wrap = view.FindControl<WrapPanel>("HeaderWrap")!;
        double narrowHeight = wrap.Bounds.Height;

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        double wideHeight = wrap.Bounds.Height;

        window.Close();

        Assert.True(narrowHeight > wideHeight,
            $"expected wrap at 360px (height={narrowHeight}) to be taller than single row at 1280px (height={wideHeight})");
    }
}
