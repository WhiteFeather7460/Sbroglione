using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

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
                var child = BuildNode(subDirectory, counter, onFilesScanned, ct);
                node.Children.Add(child);
                node.SizeBytes += child.SizeBytes;
            }

            foreach (var file in directory.EnumerateFiles())
            {
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
}
