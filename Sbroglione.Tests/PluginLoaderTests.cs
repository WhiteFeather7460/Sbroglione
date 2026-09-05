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
    public void Discover_UnreadableManifest_SkipsPluginWithoutThrowing()
    {
        // File.ReadAllText dentro PluginManifestReader.TryRead lancia UnauthorizedAccessException
        // su un plugin.json esistente ma non leggibile: TryRead intercetta solo JsonException, quindi
        // deve essere PluginLoader a contenere l'errore.
        if (OperatingSystem.IsWindows())
            return; // i permessi POSIX non si applicano

        string pluginDir = Path.Combine(_root, "unreadable-plugin");
        Directory.CreateDirectory(pluginDir);
        string manifestPath = Path.Combine(pluginDir, "plugin.json");
        File.WriteAllText(manifestPath, "{}");
        File.SetUnixFileMode(manifestPath, UnixFileMode.None);

        try
        {
            IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

            Assert.Empty(plugins);
        }
        finally
        {
            // ripristina i permessi, altrimenti Dispose non riesce a cancellare la temp dir
            File.SetUnixFileMode(manifestPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Discover_CorruptDll_SkipsPluginWithoutThrowing()
    {
        string pluginDir = Path.Combine(_root, "corrupt-plugin");
        Directory.CreateDirectory(pluginDir);
        // File con estensione .dll ma che non è affatto un PE valido: il load deve fallire
        // dentro PluginLoader, non propagare.
        File.WriteAllText(Path.Combine(pluginDir, "Corrupt.dll"), "not a valid PE file");
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """
            {
              "id": "corrupt-plugin",
              "displayName": "Corrupt",
              "version": "1.0.0",
              "contractVersion": "1.0.0",
              "mainAssemblyFileName": "Corrupt.dll"
            }
            """);

        IReadOnlyList<ITabPlugin> plugins = PluginLoader.Discover();

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_AssemblyWithoutTabPluginType_SkipsPluginWithoutThrowing()
    {
        string pluginDir = Path.Combine(_root, "no-plugin-type");
        Directory.CreateDirectory(pluginDir);

        // Sbroglione.PluginContracts è un assembly PE perfettamente valido che però non contiene
        // nessun tipo concreto che implementa ITabPlugin (solo l'interfaccia e il record manifest).
        string contractsDll = typeof(PluginManifest).Assembly.Location;
        File.Copy(contractsDll, Path.Combine(pluginDir, "Sbroglione.PluginContracts.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), """
            {
              "id": "no-plugin-type",
              "displayName": "No Plugin Type",
              "version": "1.0.0",
              "contractVersion": "1.0.0",
              "mainAssemblyFileName": "Sbroglione.PluginContracts.dll"
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
