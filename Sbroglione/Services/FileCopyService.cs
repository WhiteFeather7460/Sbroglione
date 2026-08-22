using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Sbroglione.Services;

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
    /// Enumerazione tollerante, identica a quella della simulazione: ignora i file
    /// inaccessibili e non segue i reparse point (symlink), evitando i loop.
    /// </summary>
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

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

        var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (input.ConfigureAwait(false))
        {
            var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[bufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await IoThrottleService.WaitAsync(read, ct).ConfigureAwait(false);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    onBytesCopied?.Invoke(read);
                }

                await output.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato.
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    private const int DestinationChannelCapacity = 8;

    /// <summary>
    /// Risultato di una copia verso più destinazioni: quali sono riuscite e, per quelle
    /// fallite, l'eccezione che le ha fatte fallire.
    /// </summary>
    public readonly record struct CopyToManyResult(
        IReadOnlyList<string> SucceededDestinations,
        IReadOnlyDictionary<string, Exception> FailedDestinations);

    /// <summary>
    /// Copia un file verso più destinazioni con una sola lettura della sorgente: un task
    /// legge la sorgente e distribuisce ogni blocco su un <see cref="Channel{T}"/> bounded
    /// per destinazione; un task scrittore per destinazione consuma il proprio canale al
    /// proprio ritmo, così una destinazione lenta non blocca le altre (solo, tramite il
    /// backpressure del canale, rallenta la lettura una volta piena la coda di quella
    /// destinazione). Se una destinazione fallisce, le altre proseguono; se falliscono
    /// tutte, la prima eccezione viene rilanciata.
    /// <paramref name="onBytesCopied"/> riceve (percorso destinazione, byte scritti) per
    /// ogni blocco effettivamente scritto su quella destinazione.
    /// </summary>
    public static async Task<CopyToManyResult> CopyFileToManyAsync(
        string sourcePath,
        IReadOnlyList<string> destinationPaths,
        Action<string, long>? onBytesCopied,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        if (destinationPaths.Count == 0)
            return new CopyToManyResult(Array.Empty<string>(), new Dictionary<string, Exception>());

        var channels = destinationPaths.ToDictionary(
            d => d,
            _ => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(DestinationChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true
            }));
        var failed = new ConcurrentDictionary<string, Exception>();

        var writerTasks = destinationPaths.Select(destination => Task.Run(async () =>
        {
            ChannelReader<byte[]> reader = channels[destination].Reader;
            try
            {
                var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await using (output.ConfigureAwait(false))
                {
                    await foreach (byte[] chunk in reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        await output.WriteAsync(chunk, ct).ConfigureAwait(false);
                        onBytesCopied?.Invoke(destination, chunk.Length);
                    }

                    await output.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed[destination] = ex;
                // Smaltisce il resto del canale: il reader potrebbe essere bloccato in
                // WriteAsync per backpressure e deve poter continuare con le altre destinazioni.
                try
                {
                    await foreach (var _ in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) { }
                }
                catch { /* canale già completato o cancellato: nulla da smaltire */ }
            }
        })).ToList();

        var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (input.ConfigureAwait(false))
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            {
                await IoThrottleService.WaitAsync(read, ct).ConfigureAwait(false);

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);

                foreach (var destination in destinationPaths)
                {
                    if (failed.ContainsKey(destination))
                        continue;
                    try
                    {
                        await channels[destination].Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        // Il writer di questa destinazione ha già fallito e chiuso il canale.
                    }
                }
            }
        }

        foreach (var channel in channels.Values)
            channel.Writer.TryComplete();

        await Task.WhenAll(writerTasks).ConfigureAwait(false);

        var succeeded = destinationPaths.Where(d => !failed.ContainsKey(d)).ToList();
        if (succeeded.Count == 0)
            throw failed.Values.First();

        // La ripresa (skipUnchanged) confronta dimensione + mtime: il timestamp va preservato
        // solo sulle destinazioni riuscite.
        DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        foreach (var destination in succeeded)
            File.SetLastWriteTimeUtc(destination, sourceTime);

        return new CopyToManyResult(succeeded, failed);
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
        bool skipUnchanged = false,
        Action<string>? onFileStarted = null,
        Action<string>? onFileCompleted = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        (List<string> files, long totalBytes) = await Task.Run(() =>
        {
            var list = new List<string>();
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
            {
                ct.ThrowIfCancellationRequested();
                list.Add(file);
                total += new FileInfo(file).Length;
            }
            return (list, total);
        }, ct).ConfigureAwait(false);

        onProgress?.Invoke(new CopyProgress(0, totalBytes, files.Count));
        if (files.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytes = 0;

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationFile = Path.Combine(destinationRoot, relative);

                if (skipUnchanged && IsUnchanged(sourceFile, destinationFile))
                {
                    long skippedTotal = Interlocked.Add(ref copiedBytes, new FileInfo(sourceFile).Length);
                    onProgress?.Invoke(new CopyProgress(skippedTotal, totalBytes, files.Count));
                    onFileCompleted?.Invoke(sourceFile);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                onFileStarted?.Invoke(sourceFile);
                await CopyFileAsync(sourceFile, destinationFile, deltaBytes =>
                {
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize).ConfigureAwait(false);
                onFileCompleted?.Invoke(sourceFile);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
        bool skipUnchanged = false,
        Action<string>? onFileStarted = null,
        Action<string>? onFileCompleted = null)
    {
        if (bufferSize <= 0)
            bufferSize = DefaultBufferSize;

        (List<string> files, long totalBytes) = await Task.Run(() =>
        {
            var list = new List<string>();
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
            {
                ct.ThrowIfCancellationRequested();
                list.Add(file);
                total += new FileInfo(file).Length;
            }
            return (list, total);
        }, ct).ConfigureAwait(false);

        onProgress?.Invoke(new CopyProgress(0, totalBytes, files.Count));
        if (files.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytes = 0;

        var tasks = files.Select(async sourceFile =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
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
                    onFileCompleted?.Invoke(sourceFile);
                    return;
                }

                foreach (var destinationFile in destinationFiles)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                onFileStarted?.Invoke(sourceFile);
                // onBytesCopied ora spara per-destinazione: per contare i byte sorgente una sola
                // volta (invariato rispetto a prima) usiamo solo i callback della prima
                // destinazione, che riceve gli stessi blocchi, nello stesso ordine, delle altre.
                string firstDestination = destinationFiles[0];
                await CopyFileToManyAsync(sourceFile, destinationFiles, (destination, deltaBytes) =>
                {
                    if (destination != firstDestination)
                        return;
                    long newTotal = Interlocked.Add(ref copiedBytes, deltaBytes);
                    onProgress?.Invoke(new CopyProgress(newTotal, totalBytes, files.Count));
                }, ct, bufferSize).ConfigureAwait(false);
                onFileCompleted?.Invoke(sourceFile);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancella tutto il contenuto (file e sottocartelle) di <paramref name="directory"/>,
    /// lasciando intatta la cartella stessa. No-op se non esiste.
    /// </summary>
    public static Task ClearDirectoryContentsAsync(string directory, CancellationToken ct) => Task.Run(() =>
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string file in Directory.EnumerateFiles(directory, "*", SafeEnumeration))
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(file);
        }

        foreach (string dir in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            Directory.Delete(dir, recursive: true);
        }
    }, ct);

    /// <summary>
    /// True se la destinazione esiste con la stessa dimensione della sorgente e
    /// LastWriteTimeUtc entro 2 secondi (tolleranza per filesystem a granularità grossa).
    /// </summary>
    internal static bool IsUnchanged(string sourceFile, string destinationFile) =>
        IsUnchanged(new FileInfo(sourceFile), new FileInfo(destinationFile));

    /// <summary>
    /// Overload su <see cref="FileInfo"/>: evita di ricostruirli quando il chiamante li ha già
    /// (es. la simulazione a passata unica), stessa regola dell'overload string-based.
    /// </summary>
    internal static bool IsUnchanged(FileInfo source, FileInfo destination)
    {
        if (!destination.Exists)
            return false;

        return destination.Length == source.Length
               && Math.Abs((destination.LastWriteTimeUtc - source.LastWriteTimeUtc).TotalSeconds) < 2;
    }
}
