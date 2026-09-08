using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sbroglione.Services;

public sealed record RenamedPair(string OldPath, string NewPath);

public sealed record RenameBatchRecord(Guid Id, DateTime Timestamp, IReadOnlyList<RenamedPair> Renames);

/// <summary>
/// Journal dell'ultimo batch di rinomina eseguito (JSON in AppData, stesso pattern
/// di <see cref="CopyJournalStore"/>): permette un unico livello di undo.
/// </summary>
public static class RenameJournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sbroglione",
            "rename-journal.json");

    public static string CurrentPath { get; set; } = DefaultPath;

    public static async Task<RenameBatchRecord?> LoadLastBatchAsync()
    {
        try
        {
            if (!File.Exists(CurrentPath))
                return null;

            await using var stream = File.OpenRead(CurrentPath);
            return await JsonSerializer.DeserializeAsync<RenameBatchRecord>(stream, Options).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task SaveLastBatchAsync(RenameBatchRecord batch)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = CurrentPath + ".tmp";
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, batch, Options).ConfigureAwait(false);

            File.Move(tempPath, CurrentPath, overwrite: true);
        }
        finally
        {
            Lock.Release();
        }
    }

    public static Task ClearAsync()
    {
        try
        {
            if (File.Exists(CurrentPath))
                File.Delete(CurrentPath);
        }
        catch (Exception)
        {
            // best effort, come CopyJournalStore
        }

        return Task.CompletedTask;
    }
}
