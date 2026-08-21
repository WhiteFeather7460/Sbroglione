using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Persistenza dei profili di connessione in JSON (AppData). Il file non contiene
/// mai password: quelle vivono nel keyring del sistema operativo.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Percorso predefinito del file profili.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sbroglione",
            "profiles.json");

    /// <summary>Carica i profili; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<ConnectionProfile>> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new List<ConnectionProfile>();

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(stream, Options)
                   ?? new List<ConnectionProfile>();
        }
        catch (Exception)
        {
            // File corrotto o inaccessibile: si riparte da zero, i profili sono ricreabili.
            return new List<ConnectionProfile>();
        }
    }

    /// <summary>Salva i profili creando la cartella se assente.</summary>
    public static async Task SaveAsync(string path, IReadOnlyList<ConnectionProfile> profiles)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profiles, Options);
    }
}
