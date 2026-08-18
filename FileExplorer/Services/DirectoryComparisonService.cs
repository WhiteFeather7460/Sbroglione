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

    public static async Task<DirectoryComparisonResult> CompareAsync(
        string leftRoot,
        string rightRoot,
        int maxDegreeOfParallelism,
        Action<CompareProgress>? onProgress,
        CancellationToken ct)
    {
        var leftFiles = await Task.Run(() => RelativeFileSet(leftRoot, ct), ct);
        var rightFiles = await Task.Run(() => RelativeFileSet(rightRoot, ct), ct);

        var leftOnly = leftFiles.Keys.Where(k => !rightFiles.ContainsKey(k)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var rightOnly = rightFiles.Keys.Where(k => !leftFiles.ContainsKey(k)).OrderBy(p => p, StringComparer.Ordinal).ToList();
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
                string leftPath = Path.Combine(leftRoot, relative);
                string rightPath = Path.Combine(rightRoot, relative);

                if (leftFiles[relative] != rightFiles[relative])
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
            different.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            identical.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }

    /// <summary>Mappa path relativo → dimensione file.</summary>
    private static Dictionary<string, long> RelativeFileSet(string root, CancellationToken ct)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            map[Path.GetRelativePath(root, file)] = new FileInfo(file).Length;
        }
        return map;
    }
}
