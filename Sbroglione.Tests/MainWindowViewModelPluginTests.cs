using Sbroglione.PluginContracts;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class MainWindowViewModelPluginTests : IDisposable
{
    private readonly string _originalPluginsRootPath = PluginLoader.PluginsRootPath;
    private readonly string _emptyRoot =
        Path.Combine(Path.GetTempPath(), "fe-vm-plugins-" + Guid.NewGuid().ToString("N"));

    public MainWindowViewModelPluginTests()
    {
        // Il costruttore parameterless di MainWindowViewModel chiama PluginLoader.Discover senza
        // override: isoliamo da ~/.config/Sbroglione/plugins reale (che sulla macchina di uno
        // sviluppatore potrebbe contenere plugin installati) puntando a una cartella vuota dedicata.
        PluginLoader.PluginsRootPath = _emptyRoot;
    }

    public void Dispose() => PluginLoader.PluginsRootPath = _originalPluginsRootPath;

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
