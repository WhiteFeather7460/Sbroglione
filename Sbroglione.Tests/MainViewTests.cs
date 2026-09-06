using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sbroglione.PluginContracts;
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

    private sealed class ThrowingCreateViewPlugin : ITabPlugin
    {
        public string Id => "throwing";
        public string Header => "Throwing";
        public string IconGlyph => "fa-solid fa-bug";
        public Control CreateView() => throw new InvalidOperationException("plugin di test rotto");
        public void OnUnload() { }
    }

    private sealed class WorkingPlugin : ITabPlugin
    {
        public string Id => "working";
        public string Header => "Working";
        public string IconGlyph => "fa-solid fa-check";
        public Control CreateView() => new TextBlock { Text = "ok" };
        public void OnUnload() { }
    }

    /// <summary>
    /// Regressione per il fix C1: un plugin la cui CreateView() lancia non deve impedire
    /// l'aggiunta delle tab degli altri plugin né far fallire la costruzione di MainView.
    /// </summary>
    [AvaloniaFact]
    public void MainView_PluginThrowsInCreateView_OtherPluginTabsStillAppended()
    {
        var vm = new MainWindowViewModel(
            discoverPlugins: () => new ITabPlugin[] { new ThrowingCreateViewPlugin(), new WorkingPlugin() });

        var view = new MainView { DataContext = vm };

        var tabControl = view.FindControl<TabControl>("NavTabControl");
        Assert.NotNull(tabControl);
        Assert.Contains(
            tabControl!.Items.OfType<TabItem>(),
            item => item.Content is TextBlock textBlock && textBlock.Text == "ok");
    }
}
