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
    private const int BufferSize = 1024 * 1024; // 1 MB

    /// <summary>
    /// Copia un singolo file a blocchi, segnalando i byte copiati a ogni blocco.
    /// </summary>
    public static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        Action<long>? onBytesCopied,
        CancellationToken ct)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytesCopied?.Invoke(read);
        }

        await output.FlushAsync(ct);
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
        CancellationToken ct)
    {
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

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                await CopyFileAsync(sourceFile, destinationFile, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
