using System.Reflection;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Sbroglione.Views;

namespace Sbroglione.Tests;

public class RemoteBrowserViewLayoutTests
{
    [AvaloniaFact]
    public void ConnectionWrap_WrapsToMultipleRows_WhenNarrow_AndSingleRow_WhenWide()
    {
        var view = new RemoteBrowserView();
        var window = new Window { Content = view, Width = 360, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var wrap = view.FindControl<WrapPanel>("ConnectionWrap")!;
        double narrowHeight = wrap.Bounds.Height;

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        double wideHeight = wrap.Bounds.Height;

        window.Close();

        Assert.True(narrowHeight > wideHeight,
            $"expected connection bar to wrap at 360px (height={narrowHeight}) vs single row at 1280px (height={wideHeight})");
    }

    [AvaloniaFact]
    public void TransferWrap_WrapsToMultipleRows_WhenNarrow_AndSingleRow_WhenWide()
    {
        var view = new RemoteBrowserView();
        var vm = new Sbroglione.ViewModels.RemoteBrowserViewModel(
            Sbroglione.Services.RemoteClientFactory.Create,
            Sbroglione.Services.CredentialStoreFactory.Create(),
            Sbroglione.Services.ProfileStore.DefaultPath);
        // IsConnected/IsDownloading have private setters (set internally by connect/download
        // flows); set them via reflection so the transfer bar and its progress bar are in the
        // visual tree without driving a real connection/transfer.
        SetPrivateProperty(vm, nameof(vm.IsConnected), true);
        SetPrivateProperty(vm, nameof(vm.IsDownloading), true);
        view.DataContext = vm;
        var window = new Window { Content = view, Width = 360, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var wrap = view.FindControl<WrapPanel>("TransferWrap")!;
        double narrowHeight = wrap.Bounds.Height;

        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        double wideHeight = wrap.Bounds.Height;

        window.Close();

        Assert.True(narrowHeight > wideHeight,
            $"expected transfer bar to wrap at 360px (height={narrowHeight}) vs single row at 1280px (height={wideHeight})");
    }

    private static void SetPrivateProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;
        property.SetValue(target, value);
    }
}
