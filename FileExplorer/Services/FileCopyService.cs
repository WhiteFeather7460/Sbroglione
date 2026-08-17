using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>
/// Stato di avanzamento della copia di una cartella.
/// </summary>
/// <param name="CopiedBytes">Byte copiati finora.</param>
/// <param name="TotalBytes">Byte totali da copiare.</param>
/// <param name="TotalFiles">Numero di file da copiare.</param>
public readonly record struct CopyProgress(long CopiedBytes, long TotalBytes, int TotalFiles)
{
    /// <summary>Avanzamento nell'intervallo 0..1.</summary>
    public double Fraction => TotalBytes > 0 ? (double)CopiedBytes / TotalBytes : 1.0;
}

/// <summary>
/// Copia di file e cartelle con avanzamento e supporto all'annullamento.
/// </summary>
public static class FileCopyService
{
    private const int DefaultBufferSize = 1024 * 1024; // 1 MB

    /// <summary>
    /// Copia un singolo file a blocchi, segnalando i byte copiati a ogni blocco.
    /// </summary>
    public static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                onBytesCopied?.Invoke(read);
            }

            await output.FlushAsync(ct);
        }

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato.
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    /// <summary>
    /// Copia un file verso più destinazioni con una sola lettura della sorgente:
    /// ogni blocco letto viene scritto in parallelo su tutte le destinazioni.
    /// <paramref name="onBytesCopied"/> conta i byte letti (una volta sola, non per destinazione).
    /// </summary>
    public static async Task CopyFileToManyAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        Action<long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var outputs = new List<FileStream>(destinationPaths.Count);
        try
        {
            foreach (var destination in destinationPaths)
                outputs.Add(new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None));

            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                await Task.WhenAll(outputs.Select(o => o.WriteAsync(buffer.AsMemory(0, read), ct).AsTask()));
                onBytesCopied?.Invoke(read);
            }

            foreach (var output in outputs)
                await output.FlushAsync(ct);
        }
        finally
        {
            foreach (var output in outputs)
                await output.DisposeAsync();
        }

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato
        // su tutte le destinazioni, ma solo se la copia è andata a buon fine.
        DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        foreach (var destination in destinationPaths)
            File.SetLastWriteTimeUtc(destination, sourceTime);
    }

    /// <summary>
    /// Copia ricorsivamente una cartella (più file in parallelo), replicando la struttura
    /// di <paramref name="sourceRoot"/> sotto <paramref name="destinationRoot"/>.
    /// Il primo evento di avanzamento comunica il totale di file e byte da copiare.
    /// </summary>
    public static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        long totalBytes = files.Sum(file => new FileInfo(file).Length);

        onProgress?.Invoke(new CopyProgress(0, totalBytes, files.Count));
        if (files.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytes = 0;

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationFile = Path.Combine(destinationRoot, relative);

                if (skipUnchanged && IsUnchanged(sourceFile, destinationFile))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                await CopyFileAsync(sourceFile, destinationFile, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Copia ricorsivamente una cartella verso più destinazioni (più file in parallelo),
    /// leggendo ogni file sorgente una sola volta. L'avanzamento conta i byte della sorgente.
    /// </summary>
    public static async Task CopyDirectoryToManyAsync(
        string sourceRoot,
        IReadOnlyList<string> destinationRoots,
        int maxDegreeOfParallelism,
        Action<CopyProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        bool skipUnchanged = false)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        List<string> files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        long totalBytes = files.Sum(file => new FileInfo(file).Length);

        onProgress?.Invoke(new CopyProgress(0, totalBytes, files.Count));
        if (files.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytes = 0;

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                var destinationFiles = destinationRoots
                    .Select(root => Path.Combine(root, relative))
                    .ToList();

                if (skipUnchanged && destinationFiles.All(destination => IsUnchanged(sourceFile, destination)))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    return;
                }

                foreach (var destinationFile in destinationFiles)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                await CopyFileToManyAsync(sourceFile, destinationFiles, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// True se la destinazione esiste con la stessa dimensione della sorgente e
    /// LastWriteTimeUtc entro 2 secondi (tolleranza per filesystem a granularità grossa).
    /// </summary>
    private static bool IsUnchanged(string sourceFile, string destinationFile)
    {
        var destinationInfo = new FileInfo(destinationFile);
        if (!destinationInfo.Exists)
            return false;

        var sourceInfo = new FileInfo(sourceFile);
        return destinationInfo.Length == sourceInfo.Length
               && Math.Abs((destinationInfo.LastWriteTimeUtc - sourceInfo.LastWriteTimeUtc).TotalSeconds) < 2;
    }
}
