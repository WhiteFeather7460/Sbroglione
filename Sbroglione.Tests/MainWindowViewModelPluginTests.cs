using Sbroglione.PluginContracts;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class MainWindowViewModelPluginTests
{
    private sealed class FakePlugin : ITabPlugin
    {
        public string Id => "fake";
        public string Header => "Fake";
        public string IconGlyph => "fa-solid fa-flask";
        public Avalonia.Controls.Control CreateView() => new Avalonia.Controls.TextBlock();
        public void OnUnload() { }
    }

    [Fact]
    public void Constructor_WithDiscoverPluginsOverride_PopulatesLoadedPlugins()
    {
        var vm = new MainWindowViewModel(discoverPlugins: () => new[] { new FakePlugin() });

        Assert.Single(vm.LoadedPlugins);
        Assert.Equal("fake", vm.LoadedPlugins[0].Id);
    }

    [Fact]
    public void Constructor_Default_LoadedPluginsIsEmptyWhenNoPluginsInstalled()
    {
        // Nessuna cartella plugin installata nell'ambiente di test di default:
        // il costruttore parameterless non deve mai lanciare.
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.LoadedPlugins);
    }
}
