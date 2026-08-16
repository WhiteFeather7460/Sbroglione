using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Persistenza delle impostazioni applicative in JSON (AppData), stesso pattern di
/// <see cref="ProfileStore"/>. Espone anche <see cref="Current"/>, l'istanza in memoria
/// caricata all'avvio e usata da tutto il resto dell'app.
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Percorso predefinito del file impostazioni.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "settings.json");

    /// <summary>
    /// Percorso usato da <see cref="LoadCurrentAsync"/> e <see cref="SaveCurrentAsync"/>.
    /// Sovrascrivibile nei test per non toccare l'AppData reale.
    /// </summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>Istanza in memoria delle impostazioni correnti.</summary>
    public static AppSettings Current { get; set; } = new();

    /// <summary>Carica le impostazioni da <see cref="CurrentPath"/> in <see cref="Current"/>.</summary>
    public static async Task LoadCurrentAsync()
    {
        Current = await LoadAsync(CurrentPath);
    }

    /// <summary>Salva <see cref="Current"/> su <see cref="CurrentPath"/>.</summary>
    public static Task SaveCurrentAsync() => SaveAsync(CurrentPath, Current);

    /// <summary>Carica le impostazioni; default se il file manca o è illeggibile.</summary>
    public static async Task<AppSettings> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options)
                   ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Salva le impostazioni creando la cartella se assente.</summary>
    public static async Task SaveAsync(string path, AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, Options);
    }
}
