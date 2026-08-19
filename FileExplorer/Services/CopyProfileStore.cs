using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Archivio dei profili di copia (JSON in AppData, pattern <see cref="CopyJournalStore"/>):
/// scrittura atomica e accessi serializzati.
/// </summary>
public static class CopyProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>Percorso predefinito del file profili.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "copy-profiles.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>
    /// Carica i profili ordinati per nome (case-insensitive); lista vuota se il file
    /// manca o è illeggibile.
    /// </summary>
    public static async Task<List<CopyProfile>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<CopyProfile>();

            await using var stream = File.OpenRead(CurrentPath);
            List<CopyProfile> profiles =
                await JsonSerializer.DeserializeAsync<List<CopyProfile>>(stream, Options).ConfigureAwait(false)
                ?? new List<CopyProfile>();

            foreach (var profile in profiles)
                Sanitize(profile);

            return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception)
        {
            return new List<CopyProfile>();
        }
    }

    /// <summary>Salva l'intera lista di profili (scrittura atomica tmp + move).</summary>
    public static async Task SaveAsync(IReadOnlyList<CopyProfile> profiles)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = CurrentPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, Options).ConfigureAwait(false);
            }

            File.Move(tempPath, CurrentPath, overwrite: true);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Normalizza un profilo in-place: nome vuoto → "Profilo senza nome"; le coppie con
    /// sorgente e destinazione entrambe vuote vengono scartate.
    /// </summary>
    public static void Sanitize(CopyProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = "Profilo senza nome";

        profile.Pairs.RemoveAll(pair =>
            string.IsNullOrWhiteSpace(pair.SourcePath) && string.IsNullOrWhiteSpace(pair.DestinationPath));
    }
}
