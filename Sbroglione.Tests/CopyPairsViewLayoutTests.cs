using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Sbroglione.ViewModels;
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

    [AvaloniaFact]
    public async System.Threading.Tasks.Task FilesToProcessGrid_HidesModifiedColumn_WhenNarrow_AndShowsIt_WhenWide()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a-fairly-long-file-name.txt"), "x");

            var view = new CopyPairsView();
            var vm = (CopyPairsViewModel)view.DataContext!;
            var pair = new FolderFilePairViewModel { SourcePath = tempDir };
            vm.PathPairs.Add(pair);
            pair.IsFilesExpanded = true;
            await pair.FilesLoad;

            var window = new Window { Content = view, Width = 360, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var grid = view.GetVisualDescendants().OfType<DataGrid>()
                .First(g => g.Columns.Count == 5);

            Assert.False(grid.Columns[4].IsVisible);

            window.Width = 1280;
            Dispatcher.UIThread.RunJobs();

            Assert.True(grid.Columns[4].IsVisible);

            window.Close();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
