using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Persistenza delle regole watch-folder (JSON in AppData, pattern
/// <see cref="CopyJournalStore"/>): scrittura atomica e salvataggi serializzati.
/// Le regole senza sorgente o destinazione non vengono persistite.
/// </summary>
public static class WatchRuleStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    internal const int MinIntervalMinutes = 1;
    internal const int MaxIntervalMinutes = 1440;

    /// <summary>Percorso predefinito del file regole.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sbroglione",
            "watch-rules.json");

    /// <summary>Percorso corrente; sovrascrivibile nei test.</summary>
    public static string CurrentPath { get; set; } = DefaultPath;

    /// <summary>
    /// Carica le regole in modo sincrono. Solo per l'avvio dell'app
    /// (pattern <see cref="AppSettingsStore.LoadCurrent"/>): evita sync-over-async
    /// prima che il dispatcher sia attivo.
    /// </summary>
    public static List<WatchRule> Load()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<WatchRule>();

            string json = File.ReadAllText(CurrentPath);
            var rules = JsonSerializer.Deserialize<List<WatchRule>>(json, Options) ?? new List<WatchRule>();
            return Sanitize(rules);
        }
        catch (Exception)
        {
            return new List<WatchRule>();
        }
    }

    /// <summary>Carica le regole; lista vuota se il file manca o è illeggibile.</summary>
    public static async Task<List<WatchRule>> LoadAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return new List<WatchRule>();

            await using var stream = File.OpenRead(CurrentPath);
            var rules = await JsonSerializer.DeserializeAsync<List<WatchRule>>(stream, Options).ConfigureAwait(false)
                        ?? new List<WatchRule>();
            return Sanitize(rules);
        }
        catch (Exception)
        {
            return new List<WatchRule>();
        }
    }

    /// <summary>Salva l'intera lista (atomico: tmp + move).</summary>
    public static async Task SaveAsync(IReadOnlyList<WatchRule> rules)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<WatchRule> sanitized = Sanitize(rules);

            string? directory = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = CurrentPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, sanitized, Options).ConfigureAwait(false);
            }

            File.Move(tempPath, CurrentPath, overwrite: true);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Normalizza: clamp dell'intervallo, id garantito, scarto delle regole senza percorsi.</summary>
    internal static List<WatchRule> Sanitize(IEnumerable<WatchRule> rules)
    {
        var result = new List<WatchRule>();
        foreach (WatchRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.SourcePath) || string.IsNullOrWhiteSpace(rule.DestinationPath))
                continue;

            rule.IntervalMinutes = Math.Clamp(rule.IntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes);
            if (string.IsNullOrWhiteSpace(rule.Id))
                rule.Id = Guid.NewGuid().ToString("N");
            result.Add(rule);
        }

        return result;
    }
}
