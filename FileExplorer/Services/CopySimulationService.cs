using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Esito della simulazione per una singola destinazione.</summary>
/// <param name="Root">Radice della destinazione.</param>
/// <param name="OverwriteCount">File che verrebbero sovrascritti.</param>
/// <param name="FreeBytes">Spazio libero sul volume, null se non determinabile (es. percorsi di rete).</param>
/// <param name="Fits">True se lo spazio libero copre i byte da scrivere (totale meno i byte dei file
/// saltati perché invariati); null se FreeBytes è null.</param>
public sealed record DestinationSimulation(string Root, int OverwriteCount, long? FreeBytes, bool? Fits);

/// <summary>Esito complessivo della simulazione (dry-run) di una copia.</summary>
public sealed record CopySimulationResult(
    int TotalFiles,
    long TotalBytes,
    int SkippedFiles,
    IReadOnlyList<DestinationSimulation> Destinations);

/// <summary>
/// Dry-run di una copia: enumera cosa verrebbe copiato/sovrascritto/saltato e
/// verifica lo spazio disponibile per destinazione, senza scrivere nulla.
/// </summary>
public static class CopySimulationService
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static Task<CopySimulationResult> SimulateAsync(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        CancellationToken ct)
    {
        return Task.Run(() => Simulate(sourcePath, destinationRoots, skipUnchanged, ct), ct);
    }

    private static CopySimulationResult Simulate(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        CancellationToken ct)
    {
        bool isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
            throw new FileNotFoundException("Sorgente inesistente", sourcePath);

        // Coppie (path sorgente, path relativo) da esaminare.
        List<(string Source, string Relative)> files = isDirectory
            ? Directory.EnumerateFiles(sourcePath, "*", SafeEnumeration)
                .Select(f => (f, Path.GetRelativePath(sourcePath, f)))
                .ToList()
            : new List<(string, string)> { (sourcePath, Path.GetFileName(sourcePath)) };

        long totalBytes = 0;
        foreach (var (source, _) in files)
        {
            ct.ThrowIfCancellationRequested();
            totalBytes += new FileInfo(source).Length;
        }

        int skipped = 0;
        long skippedBytes = 0;
        if (skipUnchanged)
        {
            foreach (var (source, relative) in files)
            {
                ct.ThrowIfCancellationRequested();
                if (destinationRoots.All(root => FileCopyService.IsUnchanged(source, Path.Combine(root, relative))))
                {
                    skipped++;
                    skippedBytes += new FileInfo(source).Length;
                }
            }
        }

        long bytesToWrite = totalBytes - skippedBytes;

        var destinations = new List<DestinationSimulation>(destinationRoots.Count);
        foreach (var root in destinationRoots)
        {
            ct.ThrowIfCancellationRequested();

            int overwrites = files.Count(pair => File.Exists(Path.Combine(root, pair.Relative)));

            long? freeBytes = null;
            try
            {
                string? volumeRoot = Path.GetPathRoot(Path.GetFullPath(root));
                if (!string.IsNullOrEmpty(volumeRoot))
                    freeBytes = new DriveInfo(volumeRoot).AvailableFreeSpace;
            }
            catch (Exception)
            {
                // spazio non determinabile (percorso di rete, volume rimosso): resta null.
            }

            destinations.Add(new DestinationSimulation(
                root,
                overwrites,
                freeBytes,
                freeBytes is null ? null : freeBytes >= bytesToWrite));
        }

        return new CopySimulationResult(files.Count, totalBytes, skipped, destinations);
    }
}
