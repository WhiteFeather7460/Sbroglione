using Sbroglione.PluginContracts;
using Sbroglione.Services;
using Sbroglione.TestPluginFixture;

namespace Sbroglione.Tests;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalRoot;

    public PluginLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalRoot = PluginLoader.PluginsRootPath;
        PluginLoader.PluginsRootPath = _root;
    }

    public void Dispose()
    {
        PluginLoader.PluginsRootPath = _originalRoot;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Copia la DLL della fixture (già compilata, referenziata da questo progetto) in
    /// <c>{_root}/{pluginId}/</c> insieme a un manifest coerente, come farebbe un utente reale.</summary>
    private void InstallFixturePlugin(string pluginId, string contractVersion = "1.0.0")
    {
        string pluginDir = Path.Combine(_root, pluginId);
        Directory.CreateDirectory(pluginDir);

        string fixtureDllSource = typeof(FixtureTabPlugin).Assembly.Location;
        string fixtureDllName = Path.GetFileName(fixtureDllSource);
        File.Copy(fixtureDllSource, Path.Combine(pluginDir, fixtureDllName), overwrite: true);

        // Il ProjectReference porta con sé anche la DLL di Sbroglione.PluginContracts nella
        // stessa output directory della fixture: va copiata accanto, altrimenti il load fallisce
        // per dipendenza mancante.
        string contractsDllSource = Path.Combine(
            Path.GetDirectoryName(fixtureDllSource)!,
            "Sbroglione.PluginContracts.dll");
        File.Copy(contractsDllSource, Path.Combine(pluginDir, "Sbroglione.PluginContracts.dll"), overwrite: true);

        string manifestJson = $$"""
            {
              "id": "{{pluginId}}",
              "displayName": "Fixture Plugin",
              "version": "1.0.0",
              "contractVersion": "{{contractVersion}}",
              "mainAssemblyFileName": "{{fixtureDllName}}"
            }
            """;
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), manifestJson);
    }

    [Fact]
    public void Discover_ValidPlugin_ReturnsInstantiatedPlugin()
    {
        InstallFixturePlugin("fixture-plugin");

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Single(plugins);
        Assert.Equal("fixture-plugin", plugins[0].Id);
        Assert.Equal("Fixture", plugins[0].Header);
    }

    [Fact]
    public void Discover_NoPluginsFolder_ReturnsEmpty()
    {
        Directory.Delete(_root, recursive: true);

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_IncompatibleContractVersion_SkipsPlugin()
    {
        InstallFixturePlugin("fixture-plugin", contractVersion: "99.0.0");

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_MissingManifest_SkipsFolder()
    {
        string pluginDir = Path.Combine(_root, "broken-plugin");
        Directory.CreateDirectory(pluginDir);
        // nessun plugin.json

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_ManifestPointsToMissingDll_SkipsPluginWithoutThrowing()
    {
        string pluginDir = Path.Combine(_root, "broken-plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """
            {
              "id": "broken-plugin",
              "displayName": "Broken",
              "version": "1.0.0",
              "contractVersion": "1.0.0",
              "mainAssemblyFileName": "DoesNotExist.dll"
            }
            """);

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_OneValidOneBroken_LoadsOnlyValid()
    {
        InstallFixturePlugin("good-plugin");
        string brokenDir = Path.Combine(_root, "broken-plugin");
        Directory.CreateDirectory(brokenDir);
        // nessun plugin.json in broken-plugin

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        // L'Id esposto è quello dell'istanza caricata (la fixture lo hardcoda a "fixture-plugin"),
        // non quello del manifest/cartella: qui conta solo che l'unico plugin caricato sia il valido.
        Assert.Single(plugins);
        Assert.Equal("fixture-plugin", plugins[0].Id);
    }
}
