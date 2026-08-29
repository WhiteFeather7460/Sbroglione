using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Costruzione dell'albero di occupazione disco: scansione ricorsiva con somma
/// delle dimensioni; le cartelle inaccessibili vengono ignorate.
/// </summary>
public static class DiskUsageService
{
    /// <summary>Contatore mutabile condiviso dalla ricorsione (evita ref nei metodi async).</summary>
    private sealed class ScanCounter
    {
        public int Files;
    }

    public static Task<DiskUsageNode> BuildTreeAsync(
        string rootPath,
        Action<int>? onFilesScanned,
        CancellationToken ct) =>
        Task.Run(() => BuildNode(new DirectoryInfo(rootPath), new ScanCounter(), onFilesScanned, ct), ct);

    private static DiskUsageNode BuildNode(
        DirectoryInfo directory,
        ScanCounter counter,
        Action<int>? onFilesScanned,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new DiskUsageNode
        {
            Name = directory.Name,
            FullPath = directory.FullName,
            IsDirectory = true
        };

        try
        {
            foreach (var subDirectory in directory.EnumerateDirectories())
            {
                // Symlink/junction: saltati per evitare cicli di ricorsione e conteggi doppi.
                if ((subDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var child = BuildNode(subDirectory, counter, onFilesScanned, ct);
                node.Children.Add(child);
                node.SizeBytes += child.SizeBytes;
            }

            foreach (var file in directory.EnumerateFiles())
            {
                // Coerenza con la scansione duplicati: i reparse point non vengono conteggiati.
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                node.Children.Add(new DiskUsageNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    SizeBytes = file.Length,
                    IsDirectory = false
                });
                node.SizeBytes += file.Length;

                counter.Files++;
                if (counter.Files % 256 == 0)
                    onFilesScanned?.Invoke(counter.Files);
            }
        }
        catch (UnauthorizedAccessException) { /* cartella non leggibile: esclusa */ }
        catch (IOException) { /* percorso irraggiungibile: escluso */ }

        return node;
    }

    /// <summary>
    /// Scansione a strati: enumera un intero livello dell'albero in parallelo (bounded da
    /// <see cref="Environment.ProcessorCount"/>) prima di passare al livello successivo, così la
    /// struttura è visibile dopo il primo strato invece di attendere la scansione completa.
    /// <paramref name="onLayerComplete"/> viene atteso prima di proseguire al livello successivo:
    /// questo impedisce che lo strato N+1 modifichi le liste <see cref="DiskUsageNode.Children"/>
    /// mentre la UI sta ancora leggendo lo stato dello strato N.
    /// </summary>
    public static async Task<DiskUsageNode> BuildTreeLayeredAsync(
        string rootPath,
        Func<DiskUsageNode, Task>? onLayerComplete,
        CancellationToken ct)
    {
        var root = new DiskUsageNode
        {
            Name = new DirectoryInfo(rootPath).Name,
            FullPath = rootPath,
            IsDirectory = true,
            IsPending = true
        };

        var frontier = new List<DiskUsageNode> { root };
        using var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));

        while (frontier.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            await Task.WhenAll(frontier.Select(dir => ScanOwnChildrenBoundedAsync(dir, gate, ct)));

            if (onLayerComplete is not null)
                await onLayerComplete(root);

            frontier = frontier
                .SelectMany(dir => dir.Children)
                .Where(child => child.IsDirectory)
                .ToList();
        }

        return root;
    }

    private static async Task ScanOwnChildrenBoundedAsync(DiskUsageNode dir, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Task.Run(() => ScanOwnChildren(dir, ct), ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Enumera solo i figli diretti di <paramref name="dir"/> (un solo livello, non ricorsivo).</summary>
    private static void ScanOwnChildren(DiskUsageNode dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var directory = new DirectoryInfo(dir.FullPath);

            foreach (var subDirectory in directory.EnumerateDirectories())
            {
                if ((subDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                dir.Children.Add(new DiskUsageNode
                {
                    Name = subDirectory.Name,
                    FullPath = subDirectory.FullName,
                    IsDirectory = true,
                    IsPending = true,
                    Parent = dir
                });
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                dir.Children.Add(new DiskUsageNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    SizeBytes = file.Length,
                    IsDirectory = false,
                    Parent = dir
                });
                dir.PropagateSizeIncrease(file.Length);
            }
        }
        catch (UnauthorizedAccessException) { /* cartella non leggibile: esclusa */ }
        catch (IOException) { /* percorso irraggiungibile: escluso */ }
        finally
        {
            dir.IsPending = false;
        }
    }
}
