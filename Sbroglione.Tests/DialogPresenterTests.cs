using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Sbroglione.Services;
using Sbroglione.Views.Controls;

using Xunit;

namespace Sbroglione.Tests;

/// <summary>Contenuto di dialogo fittizio: completa su comando del test.</summary>
file sealed class FakeDialogContent : UserControl, IDialogContent<string?>
{
    public event Action<string?>? Completed;

    public void Complete(string? result) => Completed?.Invoke(result);
}

// Stessa tecnica di AppLifetimeBranchTests: i lifetime Avalonia sono [NotClientImplementable],
// quindi l'implementazione va emessa a runtime da DispatchProxy invece che compilata da Roslyn.
[SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "Must stay unsealed: DispatchProxy.Create<T,TProxy> requires an unsealed TProxy to subclass at runtime.")]
file class FakeSingleViewLifetime : DispatchProxy
{
    public Control? MainView { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_MainView")
            return MainView;

        if (targetMethod?.Name == "set_MainView")
        {
            MainView = (Control?)args![0];
            return null;
        }

        return null;
    }

    public static ISingleViewApplicationLifetime Create(Control? mainView)
    {
        object proxy = DispatchProxy.Create<ISingleViewApplicationLifetime, FakeSingleViewLifetime>()!;
        ((FakeSingleViewLifetime)proxy).MainView = mainView;
        return (ISingleViewApplicationLifetime)proxy;
    }
}

[SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "Must stay unsealed: DispatchProxy.Create<T,TProxy> requires an unsealed TProxy to subclass at runtime.")]
file class FakeDesktopLifetime : DispatchProxy
{
    public Window? MainWindow { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_MainWindow")
            return MainWindow;

        if (targetMethod?.Name == "set_MainWindow")
        {
            MainWindow = (Window?)args![0];
            return null;
        }

        return null;
    }

    public static IClassicDesktopStyleApplicationLifetime Create(Window? mainWindow)
    {
        object proxy = DispatchProxy.Create<IClassicDesktopStyleApplicationLifetime, FakeDesktopLifetime>()!;
        ((FakeDesktopLifetime)proxy).MainWindow = mainWindow;
        return (IClassicDesktopStyleApplicationLifetime)proxy;
    }
}

/// <summary>
/// Sostituisce temporaneamente il lifetime visto dal presenter.
/// (<c>Application.ApplicationLifetime</c> non è scrivibile dopo l'inizializzazione dell'app
/// headless, da qui il seam <c>DialogPresenter.LifetimeOverride</c>.)
/// </summary>
file sealed class LifetimeScope : IDisposable
{
    private readonly Func<IApplicationLifetime?>? _previous;

    public LifetimeScope(IApplicationLifetime? lifetime)
    {
        _previous = DialogPresenter.LifetimeOverride;
        DialogPresenter.LifetimeOverride = () => lifetime;
    }

    public void Dispose() => DialogPresenter.LifetimeOverride = _previous;
}

public class DialogPresenterTests
{
    [AvaloniaFact]
    public async Task ShowAsync_WithoutLifetime_ReturnsDefault_AndBuildsNoHost()
    {
        using var scope = new LifetimeScope(null);
        bool windowBuilt = false;
        bool contentBuilt = false;

        string? result = await DialogPresenter.ShowAsync<FakeDialogContent, string?>(
            () => { windowBuilt = true; return new Window(); },
            () => { contentBuilt = true; return new FakeDialogContent(); },
            new object());

        Assert.Null(result);
        Assert.False(windowBuilt);
        Assert.False(contentBuilt);
    }

    [AvaloniaFact]
    public async Task ShowAsync_WithSingleViewLifetime_WithoutTopLevel_ReturnsDefault()
    {
        // MainView non agganciata a nessun TopLevel: nessun OverlayLayer disponibile.
        var detachedRoot = new ContentControl();
        using var scope = new LifetimeScope(FakeSingleViewLifetime.Create(detachedRoot));

        string? result = await DialogPresenter.ShowAsync<FakeDialogContent, string?>(
            () => new Window(),
            () => new FakeDialogContent(),
            new object());

        Assert.Null(result);
    }

    [AvaloniaFact]
    public async Task ShowAsync_WithSingleViewLifetime_MountsContentInOverlay_AndReturnsItsResult()
    {
        var root = new ContentControl();
        var window = new Window { Content = root, Width = 400, Height = 300 };
        window.Show();

        using var scope = new LifetimeScope(FakeSingleViewLifetime.Create(root));

        FakeDialogContent? content = null;
        Task<string?> pending = DialogPresenter.ShowAsync<FakeDialogContent, string?>(
            () => throw new InvalidOperationException("Il ramo desktop non deve essere usato su single-view."),
            () => content = new FakeDialogContent(),
            new object());

        Dispatcher.UIThread.RunJobs();

        OverlayLayer layer = Assert.IsType<OverlayLayer>(OverlayLayer.GetOverlayLayer(root));
        Assert.NotEmpty(layer.Children);
        Assert.NotNull(content);
        Assert.False(pending.IsCompleted);

        content!.Complete("scelto");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("scelto", await pending);
        // A dialogo chiuso l'overlay torna vuoto: nessun residuo che blocchi l'input.
        Assert.Empty(layer.Children);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ShowAsync_WithSingleViewLifetime_PassesDataContextToContent()
    {
        var root = new ContentControl();
        var window = new Window { Content = root, Width = 400, Height = 300 };
        window.Show();

        using var scope = new LifetimeScope(FakeSingleViewLifetime.Create(root));

        var dataContext = new object();
        FakeDialogContent? content = null;
        Task<string?> pending = DialogPresenter.ShowAsync<FakeDialogContent, string?>(
            () => new Window(),
            () => content = new FakeDialogContent(),
            dataContext);

        Dispatcher.UIThread.RunJobs();

        Assert.Same(dataContext, content!.DataContext);

        content.Complete(null);
        Dispatcher.UIThread.RunJobs();
        await pending;

        window.Close();
    }

    [AvaloniaFact]
    public async Task ShowAsync_WithDesktopLifetime_ShowsWindowAsDialog_AndReturnsItsResult()
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();

        using var scope = new LifetimeScope(FakeDesktopLifetime.Create(owner));

        var content = new FakeDialogContent();
        var dialog = new Window { Content = content, Width = 200, Height = 100 };
        content.Completed += result => dialog.Close(result);

        Task<string?> pending = DialogPresenter.ShowAsync<FakeDialogContent, string?>(
            () => dialog,
            () => throw new InvalidOperationException("Il ramo overlay non deve essere usato su desktop."),
            new object());

        Dispatcher.UIThread.RunJobs();

        content.Complete("da finestra");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("da finestra", await pending);

        owner.Close();
    }
}
