using System;
using System.IO;
using System.Text.Json;
using System.Threading;
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

    /// <summary>Limiti di validazione per ManualParallelism, mirror di quelli in SettingsViewModel.</summary>
    private const int MinManualParallelism = 1;
    private const int MaxManualParallelism = 32;

    /// <summary>Limiti di validazione per BufferSizeBytes (256KB-16MB), mirror di quelli in SettingsViewModel.</summary>
    private const int MinBufferSizeBytes = 262144;
    private const int MaxBufferSizeBytes = 16777216;

    /// <summary>Limiti di validazione per ThrottleMBps, mirror di quelli in SettingsViewModel/CopyPairsViewModel.</summary>
    private const int MinThrottleMBps = 1;
    private const int MaxThrottleMBps = 1000;

    /// <summary>Serializza gli accessi concorrenti a <see cref="SaveCurrentAsync"/> per evitare scritture sovrapposte.</summary>
    private static readonly SemaphoreSlim SaveLock = new(1, 1);

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
        Current = await LoadAsync(CurrentPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Carica le impostazioni da <see cref="CurrentPath"/> in <see cref="Current"/> in modo
    /// sincrono. Da usare solo all'avvio dell'applicazione, prima che il dispatcher Avalonia
    /// sia attivo (dove un await asincrono causerebbe un deadlock sync-over-async).
    /// </summary>
    public static void LoadCurrent()
    {
        Current = Load(CurrentPath);
    }

    /// <summary>Salva <see cref="Current"/> su <see cref="CurrentPath"/>, serializzando i salvataggi concorrenti.</summary>
    public static async Task SaveCurrentAsync()
    {
        await SaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(CurrentPath, Current).ConfigureAwait(false);
        }
        finally
        {
            SaveLock.Release();
        }
    }

    /// <summary>Carica le impostazioni; default se il file manca o è illeggibile.</summary>
    public static async Task<AppSettings> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            await using var stream = File.OpenRead(path);
            AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options).ConfigureAwait(false)
                   ?? new AppSettings();
            Clamp(settings);
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Carica le impostazioni in modo sincrono; default se il file manca o è illeggibile.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            string json = File.ReadAllText(path);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            Clamp(settings);
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Riporta i valori numerici nei range validi, in caso di file impostazioni manomesso/corrotto.</summary>
    private static void Clamp(AppSettings settings)
    {
        settings.ManualParallelism = Math.Clamp(settings.ManualParallelism, MinManualParallelism, MaxManualParallelism);
        settings.BufferSizeBytes = Math.Clamp(settings.BufferSizeBytes, MinBufferSizeBytes, MaxBufferSizeBytes);
        settings.ThrottleMBps = Math.Clamp(settings.ThrottleMBps, MinThrottleMBps, MaxThrottleMBps);
    }

    /// <summary>Salva le impostazioni creando la cartella se assente, con scrittura atomica (file temporaneo + move).</summary>
    public static async Task SaveAsync(string path, AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
