using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Journal persistente delle copie in corso (JSON in AppData, pattern
/// <see cref="AppSettingsStore"/>): scrittura atomica e accessi serializzati.
/// </summary>
public static class CopyJournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>Percorso predefinito del file journal.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sbroglione",
            "copy-journal.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>Carica il journal; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<CopyJobRecord>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<CopyJobRecord>();

            await using var stream = File.OpenRead(CurrentPath);
            return await JsonSerializer.DeserializeAsync<List<CopyJobRecord>>(stream, Options).ConfigureAwait(false)
                   ?? new List<CopyJobRecord>();
        }
        catch (Exception)
        {
            return new List<CopyJobRecord>();
        }
    }

    /// <summary>Aggiunge una voce e salva.</summary>
    public static async Task AddAsync(CopyJobRecord record)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<CopyJobRecord> jobs = await LoadAsync().ConfigureAwait(false);
            jobs.Add(record);
            await SaveAsync(jobs).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Rimuove la voce con l'id indicato (no-op se assente) e salva.</summary>
    public static async Task RemoveAsync(Guid id)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<CopyJobRecord> jobs = await LoadAsync().ConfigureAwait(false);
            jobs.RemoveAll(job => job.Id == id);
            await SaveAsync(jobs).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Svuota il journal.</summary>
    public static async Task ClearAsync()
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(new List<CopyJobRecord>()).ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task SaveAsync(List<CopyJobRecord> jobs)
    {
        string? directory = Path.GetDirectoryName(CurrentPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = CurrentPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, jobs, Options).ConfigureAwait(false);
        }

        File.Move(tempPath, CurrentPath, overwrite: true);
    }
}
