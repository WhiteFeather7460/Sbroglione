using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Gruppo di file identici (stessa dimensione e stesso SHA-256).</summary>
public sealed record DuplicateGroup(long FileSize, string Sha256, IReadOnlyList<string> FilePaths);

/// <summary>Avanzamento della scansione duplicati, per fase.</summary>
public readonly record struct DuplicateScanProgress(string Stage, int Processed, int Total);

/// <summary>
/// Ricerca duplicati a cascata: raggruppamento per dimensione, poi hash parziale
/// (primi 64 KB) dei soli candidati, poi hash completo. Ogni fase scarta i gruppi
/// rimasti con un solo file, così l'hash completo tocca il minimo indispensabile.
/// </summary>
public static class DuplicateFinderService
{
    private const long PartialHashBytes = 64 * 1024;

    public static async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        string rootPath,
        int maxDegreeOfParallelism,
        Action<DuplicateScanProgress>? onProgress,
        CancellationToken ct)
    {
        // Fase 1: enumerazione e raggruppamento per dimensione (file vuoti e illeggibili esclusi).
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Esplicito: il default salterebbe Hidden/System; qui si escludono solo i reparse point
            // (symlink/junction), per evitare cicli e duplicati apparenti dello stesso file.
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        List<(string Path, long Length)> files = await Task.Run(() =>
            Directory.EnumerateFiles(rootPath, "*", enumerationOptions)
                .Select(path =>
                {
                    ct.ThrowIfCancellationRequested();
                    try { return (Path: path, Length: new FileInfo(path).Length); }
                    catch (IOException) { return (Path: path, Length: -1L); }
                    catch (UnauthorizedAccessException) { return (Path: path, Length: -1L); }
                })
                .Where(file => file.Length > 0)
                .ToList(), ct).ConfigureAwait(false);

        var partialCandidates = files
            .GroupBy(file => file.Length)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        // Fase 2: hash parziale dei candidati.
        var partialHashes = await HashAllAsync(
            partialCandidates, PartialHashBytes, maxDegreeOfParallelism,
            processed => onProgress?.Invoke(new DuplicateScanProgress(LocalizationService.Tr("Str.Duplicates.PartialHash"), processed, partialCandidates.Count)),
            ct).ConfigureAwait(false);

        var partialGroups = partialHashes
            .GroupBy(file => (file.Length, file.Hash))
            .Where(group => group.Count() > 1)
            .ToList();

        // Fase 3: hash completo; per i file entro i 64 KB l'hash parziale è già completo.
        var results = new List<DuplicateGroup>();
        var fullCandidates = new List<(string Path, long Length)>();

        foreach (var group in partialGroups)
        {
            if (group.Key.Length <= PartialHashBytes)
                results.Add(new DuplicateGroup(
                    group.Key.Length,
                    group.Key.Hash,
                    group.Select(file => file.Path).OrderBy(p => p, StringComparer.Ordinal).ToList()));
            else
                fullCandidates.AddRange(group.Select(file => (file.Path, file.Length)));
        }

        var fullHashes = await HashAllAsync(
            fullCandidates, long.MaxValue, maxDegreeOfParallelism,
            processed => onProgress?.Invoke(new DuplicateScanProgress(LocalizationService.Tr("Str.Duplicates.FullHash"), processed, fullCandidates.Count)),
            ct).ConfigureAwait(false);

        results.AddRange(fullHashes
            .GroupBy(file => (file.Length, file.Hash))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.Key.Length,
                group.Key.Hash,
                group.Select(file => file.Path).OrderBy(p => p, StringComparer.Ordinal).ToList())));

        return results
            .OrderByDescending(group => group.FileSize * (group.FilePaths.Count - 1))
            .ToList();
    }

    private static async Task<List<(string Path, long Length, string Hash)>> HashAllAsync(
        List<(string Path, long Length)> files,
        long maxBytes,
        int maxDegreeOfParallelism,
        Action<int>? onProcessed,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<(string Path, long Length, string Hash)>();
        int processed = 0;

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxDegreeOfParallelism));

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string hash = await ChecksumService.ComputeSha256Async(file.Path, maxBytes, ct).ConfigureAwait(false);
                results.Add((file.Path, file.Length, hash));
            }
            catch (IOException) { /* file sparito o bloccato: escluso dai risultati */ }
            catch (UnauthorizedAccessException) { /* idem */ }
            finally
            {
                semaphore.Release();
                onProcessed?.Invoke(Interlocked.Increment(ref processed));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }
}
