using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Sbroglione.PluginContracts;

namespace Sbroglione.Services;

/// <summary>
/// Scopre e carica i plugin <see cref="ITabPlugin"/> da <see cref="PluginsRootPath"/>. Ogni
/// plugin viene caricato in un <see cref="AssemblyLoadContext"/> dedicato per isolare le sue
/// dipendenze da quelle di altri plugin. Nessun errore su un singolo plugin (manifest invalido,
/// versione di contratto incompatibile, DLL mancante/corrotta, eccezione nel costruttore) deve
/// propagarsi: quel plugin viene semplicemente escluso dal risultato.
/// </summary>
public static class PluginLoader
{
    /// <summary>Major della <c>ContractVersion</c> di <see cref="ITabPlugin"/> supportato da questo host.</summary>
    public const int SupportedContractMajor = 1;

    /// <summary>Sovrascrivibile nei test per non toccare l'AppData reale.</summary>
    public static string PluginsRootPath { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sbroglione",
            "plugins");

    public static IReadOnlyList<ITabPlugin> Discover()
    {
        var result = new List<ITabPlugin>();

        if (!Directory.Exists(PluginsRootPath))
            return result;

        foreach (string pluginDir in Directory.EnumerateDirectories(PluginsRootPath))
        {
            if (TryLoadPlugin(pluginDir, out ITabPlugin? plugin))
                result.Add(plugin!);
        }

        return result;
    }

    private static bool TryLoadPlugin(string pluginDir, out ITabPlugin? plugin)
    {
        plugin = null;

        string manifestPath = Path.Combine(pluginDir, "plugin.json");
        if (!PluginManifestReader.TryRead(manifestPath, out PluginManifest? manifest))
            return false;

        if (!ContractVersionCompatibility.IsCompatible(manifest!.ContractVersion, SupportedContractMajor))
            return false;

        string dllPath = Path.Combine(pluginDir, manifest.MainAssemblyFileName);
        if (!File.Exists(dllPath))
            return false;

        try
        {
            var context = new AssemblyLoadContext($"plugin-{manifest.Id}", isCollectible: true);
            Assembly assembly = context.LoadFromAssemblyPath(dllPath);

            Type? pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(ITabPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (pluginType is null)
                return false;

            if (Activator.CreateInstance(pluginType) is not ITabPlugin instance)
                return false;

            plugin = instance;
            return true;
        }
#pragma warning disable CA1031 // un plugin di terze parti può fallire in qualunque modo: va isolato, non diagnosticato
        catch (Exception)
#pragma warning restore CA1031
        {
            // DLL corrotta, dipendenza mancante, eccezione nel costruttore del plugin, ecc.:
            // un plugin rotto non deve impedire l'avvio dell'app né il caricamento degli altri.
            return false;
        }
    }
}
