using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

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
        CancellationToken ct,
        ExtensionFilter? extensionFilter = null)
    {
        return Task.Run(() => Simulate(sourcePath, destinationRoots, skipUnchanged, extensionFilter, ct), ct);
    }

    private static CopySimulationResult Simulate(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        bool skipUnchanged,
        ExtensionFilter? extensionFilter,
        CancellationToken ct)
    {
        bool isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
            // Testo diagnostico, non mostrato: CopyPairsViewModel intercetta questo tipo
            // specifico e traduce con il percorso, non con ex.Message (confine Service→ViewModel).
            throw new FileNotFoundException("Missing simulation source", sourcePath);

        // Sorgente file singolo: la copia reale (CopySingleFileAsync) risolve la destinazione in modo
        // diverso da una directory e non applica mai SkipUnchanged (ricopia sempre), né il filtro
        // per estensione (un file singolo selezionato esplicitamente si copia sempre). La simulazione
        // deve rispecchiare questo comportamento, non quello del ramo directory.
        if (!isDirectory)
            return SimulateSingleFile(sourcePath, destinationRoots, ct);

        // Coppie (path sorgente, path relativo) da esaminare.
        List<(string Source, string Relative)> files = Directory.EnumerateFiles(sourcePath, "*", SafeEnumeration)
            .Where(f => extensionFilter is null || extensionFilter.Matches(f))
            .Select(f => (f, Path.GetRelativePath(sourcePath, f)))
            .ToList();

        // Passata unica: totale, overwrite per destinazione e skip-se-invariato costruiscono i
        // FileInfo di sorgente e destinazione una volta sola per file (rispettivamente per
        // coppia file/destinazione), invece di tre passate separate sugli stessi file.
        // Overwrite indicizzati per POSIZIONE (non per Dictionary keyed sul path): la copia reale
        // tollera destinationRoots con radici duplicate (es. AddExtraDestinationAsync propone come
        // default lo stesso DestinationPath e non deduplica), un Dictionary<string,int> ci
        // lancerebbe un ArgumentException su "chiave duplicata".
        long totalBytes = 0;
        int skipped = 0;
        long skippedBytes = 0;
        var overwrites = new int[destinationRoots.Count];

        foreach (var (source, relative) in files)
        {
            ct.ThrowIfCancellationRequested();
            var sourceInfo = new FileInfo(source);
            totalBytes += sourceInfo.Length;

            bool unchangedEverywhere = skipUnchanged;
            for (int i = 0; i < destinationRoots.Count; i++)
            {
                var destInfo = new FileInfo(Path.Combine(destinationRoots[i], relative));
                if (destInfo.Exists)
                    overwrites[i]++;
                if (skipUnchanged)
                    unchangedEverywhere &= FileCopyService.IsUnchanged(sourceInfo, destInfo);
            }

            if (skipUnchanged && unchangedEverywhere)
            {
                skipped++;
                skippedBytes += sourceInfo.Length;
            }
        }

        long bytesToWrite = totalBytes - skippedBytes;

        var destinations = new List<DestinationSimulation>(destinationRoots.Count);
        for (int i = 0; i < destinationRoots.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            destinations.Add(BuildDestinationSimulation(destinationRoots[i], overwrites[i], bytesToWrite));
        }

        return new CopySimulationResult(files.Count, totalBytes, skipped, destinations);
    }

    private static CopySimulationResult SimulateSingleFile(
        string sourcePath,
        IReadOnlyList<string> destinationRoots,
        CancellationToken ct)
    {
        long totalBytes = new FileInfo(sourcePath).Length;
        string fileName = Path.GetFileName(sourcePath);

        var destinations = new List<DestinationSimulation>(destinationRoots.Count);
        foreach (var root in destinationRoots)
        {
            ct.ThrowIfCancellationRequested();

            // Stessa risoluzione della copia reale: se la destinazione è una cartella esistente il file
            // finisce dentro, altrimenti la destinazione è già il path del file (esista o no).
            string resolvedDestination = Directory.Exists(root)
                ? Path.Combine(root, fileName)
                : root;

            int overwrites = File.Exists(resolvedDestination) ? 1 : 0;

            destinations.Add(BuildDestinationSimulation(root, overwrites, totalBytes));
        }

        // SkipUnchanged non è applicato: la copia reale di un file singolo ricopia sempre.
        return new CopySimulationResult(TotalFiles: 1, totalBytes, SkippedFiles: 0, destinations);
    }

    private static DestinationSimulation BuildDestinationSimulation(string root, int overwrites, long bytesToWrite)
    {
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

        return new DestinationSimulation(
            root,
            overwrites,
            freeBytes,
            freeBytes is null ? null : freeBytes >= bytesToWrite);
    }
}
