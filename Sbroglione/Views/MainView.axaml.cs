using Avalonia.Controls;
using Avalonia.Layout;
using Projektanker.Icons.Avalonia;
using Sbroglione.PluginContracts;
using Sbroglione.ViewModels;

namespace Sbroglione.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AppendPluginTabs();
    }

    /// <summary>
    /// Aggiunge una <see cref="TabItem"/> per ciascun plugin caricato, in coda alle 7 tab
    /// built-in dichiarate staticamente in XAML. Chiamato una volta sola: se il DataContext
    /// cambia di nuovo (non previsto in produzione, ma capita nei test), le tab già aggiunte
    /// non vengono duplicate.
    /// </summary>
    private bool _pluginTabsAppended;

    private void AppendPluginTabs()
    {
        if (_pluginTabsAppended || DataContext is not MainWindowViewModel vm || vm.LoadedPlugins.Count == 0)
            return;

        _pluginTabsAppended = true;

        foreach (ITabPlugin plugin in vm.LoadedPlugins)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new Icon { Value = plugin.IconGlyph });
            header.Children.Add(new TextBlock { Text = plugin.Header });

            var tabItem = new TabItem
            {
                Header = header,
                Content = plugin.CreateView()
            };
            NavTabControl.Items.Add(tabItem);
        }
    }
}
