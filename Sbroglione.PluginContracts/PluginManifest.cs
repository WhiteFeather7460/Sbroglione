namespace Sbroglione.PluginContracts;

/// <summary>Contenuto di <c>plugin.json</c>, nella cartella del plugin accanto alla DLL.</summary>
public sealed record PluginManifest(
    string Id,
    string DisplayName,
    string Version,
    string ContractVersion,
    string MainAssemblyFileName);
