using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Avanzamento del confronto (file comuni processati sul totale dei comuni).</summary>
public readonly record struct CompareProgress(int Processed, int Total);

/// <summary>
/// Esito del confronto di due alberi: path relativi alle radici,
/// classificati in quattro categorie, ordinati.
/// </summary>
public sealed record DirectoryComparisonResult(
    IReadOnlyList<string> LeftOnly,
    IReadOnlyList<string> RightOnly,
    IReadOnlyList<string> Different,
    IReadOnlyList<string> Identical);

/// <summary>
/// Confronto directory a cascata: presenza → dimensione → SHA-256 (solo a parità
/// di dimensione), più file in parallelo. Enumerazione tollerante (symlink e
/// file inaccessibili saltati).
/// </summary>
public static class DirectoryComparisonService
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>
    /// Comparer di default per i path relativi: case-insensitive sui filesystem
    /// tipicamente case-insensitive (Windows, macOS), byte-exact altrove.
    /// </summary>
    internal static StringComparer DefaultPathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot,
        string rightRoot,
        int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress,
        CancellationToken ct)
        => CompareAsync(leftRoot, rightRoot, maxDegreeOfParallelism, onProgress, DefaultPathComparer, ct);

    public static async Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot,
        string rightRoot,
        int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress,
        StringComparer pathComparer,
        CancellationToken ct)
    {
        var leftFiles = await Task.Run(() => RelativeFileSet(leftRoot, pathComparer, ct), ct);
        var rightFiles = await Task.Run(() => RelativeFileSet(rightRoot, pathComparer, ct), ct);

        var leftOnly = leftFiles.Keys.Where(k => !rightFiles.ContainsKey(k)).OrderBy(p => p, pathComparer).ToList();
        var rightOnly = rightFiles.Keys.Where(k => !leftFiles.ContainsKey(k)).OrderBy(p => p, pathComparer).ToList();
        var common = leftFiles.Keys.Where(rightFiles.ContainsKey).ToList();

        var different = new ConcurrentBag<string>();
        var identical = new ConcurrentBag<string>();
        int processed = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = common.Select(async relative =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                var leftEntry = leftFiles[relative];
                var rightEntry = rightFiles[relative];
                string leftPath = Path.Combine(leftRoot, leftEntry.RelativePath);
                string rightPath = Path.Combine(rightRoot, rightEntry.RelativePath);

                if (leftEntry.Size != rightEntry.Size)
                {
                    different.Add(relative);
                    return;
                }

                string leftHash = await ChecksumService.ComputeSha256Async(leftPath, ct);
                string rightHash = await ChecksumService.ComputeSha256Async(rightPath, ct);
                if (string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase))
                    identical.Add(relative);
                else
                    different.Add(relative);
            }
            finally
            {
                semaphore.Release();
                onProgress?.Invoke(new CompareProgress(Interlocked.Increment(ref processed), common.Count));
            }
        });

        await Task.WhenAll(tasks);

        return new DirectoryComparisonResult(
            leftOnly,
            rightOnly,
            different.OrderBy(p => p, pathComparer).ToList(),
            identical.OrderBy(p => p, pathComparer).ToList());
    }

    /// <summary>Voce di un file relativo a una radice: dimensione e path relativo con il casing reale su disco.</summary>
    private readonly record struct FileEntry(long Size, string RelativePath);

    /// <summary>Mappa path relativo (chiave normalizzata secondo il comparer) → voce file.</summary>
    private static Dictionary<string, FileEntry> RelativeFileSet(string root, StringComparer pathComparer, CancellationToken ct)
    {
        var map = new Dictionary<string, FileEntry>(pathComparer);
        foreach (var file in Directory.EnumerateFiles(root, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, file);
            map[relative] = new FileEntry(new FileInfo(file).Length, relative);
        }
        return map;
    }
}
