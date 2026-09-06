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

        // Directory.EnumerateDirectories è lazy: un errore di I/O può emergere a metà iterazione,
        // non solo alla chiamata. Un fallimento di enumerazione interrompe la scoperta ma non deve
        // mai impedire l'avvio dell'app: si restituisce quanto raccolto fino a quel punto.
        //
        // L'ordine di enumerazione del filesystem non è garantito (dipende da OS/filesystem):
        // si ordina per nome cartella così la scoperta è deterministica a parità di installazione
        // (stessa app, stesso set di plugin => stesso ordine di tab ad ogni avvio).
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            IEnumerable<string> pluginDirs = Directory.EnumerateDirectories(PluginsRootPath)
                .OrderBy(dir => Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase);

            foreach (string pluginDir in pluginDirs)
            {
                if (TryLoadPlugin(pluginDir, out ITabPlugin? plugin, out string? pluginId))
                {
                    // Due plugin non possono dichiarare lo stesso Id manifest: si tiene solo il
                    // primo trovato (ordine deterministico sopra) e si scarta il duplicato, invece
                    // di caricarli entrambi (due tab con lo stesso Id confonderebbero qualunque
                    // logica futura che indicizzi i plugin per Id).
                    if (pluginId is not null && !seenIds.Add(pluginId))
                        continue;

                    result.Add(plugin!);
                }
            }
        }
#pragma warning disable CA1031 // nessuna eccezione deve propagarsi da Discover: vedi doc di classe
        catch (Exception)
#pragma warning restore CA1031
        {
            // Root cancellata/smontata sotto i piedi, permessi negati, I/O error: si smette di
            // scoprire, senza propagare.
        }

        return result;
    }

    private static bool TryLoadPlugin(string pluginDir, out ITabPlugin? plugin, out string? pluginId)
    {
        plugin = null;
        pluginId = null;

        // L'intero corpo è protetto: anche la lettura del manifest può fallire con IOException o
        // UnauthorizedAccessException (plugin.json esistente ma illeggibile, lockato, o in realtà
        // una directory), casi che PluginManifestReader.TryRead non intercetta.
        try
        {
            string manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!PluginManifestReader.TryRead(manifestPath, out PluginManifest? manifest))
                return false;

            if (!ContractVersionCompatibility.IsCompatible(manifest!.ContractVersion, SupportedContractMajor))
                return false;

            // Guardia anti path-traversal: MainAssemblyFileName arriva da un plugin.json scritto
            // da terzi. Deve essere un semplice nome file dentro pluginDir, mai un path assoluto
            // o relativo che scappi dalla cartella del plugin (es. "..\..\evil.dll" o
            // "/etc/passwd"): altrimenti un manifest malevolo potrebbe far caricare (e
            // Activator.CreateInstance-are) una DLL arbitraria sul filesystem.
            if (!IsSafeRelativeFileName(manifest.MainAssemblyFileName))
                return false;

            string dllPath = Path.Combine(pluginDir, manifest.MainAssemblyFileName);
            if (!File.Exists(dllPath))
                return false;

            var resolver = new AssemblyDependencyResolver(dllPath);
            var context = new PluginLoadContext($"plugin-{manifest.Id}", resolver);
            Assembly assembly = context.LoadFromAssemblyPath(dllPath);

            Type? pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(ITabPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (pluginType is null)
                return false;

            if (Activator.CreateInstance(pluginType) is not ITabPlugin instance)
                return false;

            plugin = instance;
            pluginId = manifest.Id;
            return true;
        }
#pragma warning disable CA1031 // un plugin di terze parti può fallire in qualunque modo: va isolato, non diagnosticato
        catch (Exception)
#pragma warning restore CA1031
        {
            // Manifest illeggibile, DLL corrotta, dipendenza mancante, eccezione nel costruttore
            // del plugin, ecc.: un plugin rotto non deve impedire l'avvio dell'app né il
            // caricamento degli altri.
            return false;
        }
    }

    /// <summary>
    /// <c>true</c> se <paramref name="fileName"/> è un semplice nome file (nessun separatore di
    /// percorso, nessun segmento <c>..</c>, non un path assoluto): l'unica forma ammessa per
    /// <c>MainAssemblyFileName</c>, che deve sempre risolvere dentro la cartella del plugin.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> PathSeparators =
        System.Buffers.SearchValues.Create(['/', '\\']);

    private static bool IsSafeRelativeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !Path.IsPathRooted(fileName)
        && fileName.AsSpan().IndexOfAny(PathSeparators) < 0
        && fileName != ".."
        && fileName != ".";

    /// <summary>
    /// <see cref="AssemblyLoadContext"/> dedicato a un singolo plugin: risolve le dipendenze del
    /// plugin (NuGet/transitive DLL copiate nella stessa cartella dell'assembly principale) tramite
    /// <see cref="AssemblyDependencyResolver"/> prima di ricadere sul comportamento di default,
    /// così un plugin può portare le proprie librerie senza collidere con quelle di altri plugin
    /// o dell'host.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string name, AssemblyDependencyResolver resolver)
            : base(name, isCollectible: true)
        {
            _resolver = resolver;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Sbroglione.PluginContracts (che dichiara ITabPlugin) è già caricata dall'host in
            // AssemblyLoadContext.Default: va SEMPRE condivisa da lì, mai ricaricata in questo
            // contesto privato. Due copie della stessa assembly userebbero due identità di tipo
            // distinte per ITabPlugin, e "Activator.CreateInstance(...) is ITabPlugin" fallirebbe
            // sempre nonostante il plugin sia corretto. Restituire null qui fa ricadere la
            // risoluzione sul contesto Default, che la trova già caricata.
            if (assemblyName.Name == ContractAssemblyName)
                return null;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }

        private static readonly string? ContractAssemblyName = typeof(ITabPlugin).Assembly.GetName().Name;
    }
}
