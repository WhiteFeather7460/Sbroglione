using System;
using System.IO;
using System.Text.Json;
using Sbroglione.PluginContracts;

namespace Sbroglione.Services;

/// <summary>Legge e valida <c>plugin.json</c> dalla cartella di un plugin.</summary>
public static class PluginManifestReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// <c>true</c> con <paramref name="manifest"/> valorizzato se il file esiste, è JSON valido
    /// e tutti i campi obbligatori sono presenti e non vuoti; <c>false</c> altrimenti (nessuna
    /// eccezione propagata: un manifest invalido è un caso atteso, non un errore di programma).
    /// </summary>
    public static bool TryRead(string path, out PluginManifest? manifest)
    {
        manifest = null;

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            PluginManifest? parsed = JsonSerializer.Deserialize<PluginManifest>(json, Options);

            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.Id)
                || string.IsNullOrWhiteSpace(parsed.DisplayName)
                || string.IsNullOrWhiteSpace(parsed.Version)
                || string.IsNullOrWhiteSpace(parsed.ContractVersion)
                || string.IsNullOrWhiteSpace(parsed.MainAssemblyFileName))
                return false;

            manifest = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
