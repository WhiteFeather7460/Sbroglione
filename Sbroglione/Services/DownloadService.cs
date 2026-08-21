using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Orchestrazione dei download remoti: check di esistenza locale, filtri, batch e report.
/// </summary>
public static class DownloadService
{
    /// <summary>Tolleranza sul confronto delle date di modifica (timestamp FTP poco precisi).</summary>
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Suffisso del file temporaneo usato durante il trasferimento.</summary>
    private const string PartialSuffix = ".part";

    /// <summary>
    /// Stato del file remoto rispetto a <paramref name="localPath"/>:
    /// Present se dimensione uguale e data entro la tolleranza, Different altrimenti.
    /// </summary>
    public static LocalFileStatus GetLocalStatus(RemoteItem item, string localPath)
    {
        var info = new FileInfo(localPath);
        if (!info.Exists)
            return LocalFileStatus.Missing;

        bool sameSize = info.Length == item.Size;
        bool sameDate = (info.LastWriteTime - item.Modified).Duration() <= DateTolerance;

        return sameSize && sameDate ? LocalFileStatus.Present : LocalFileStatus.Different;
    }

    /// <summary>
    /// Percorso locale relativo del file: FullPath meno il prefisso <paramref name="remoteBasePath"/>,
    /// con separatori convertiti. Se il file è fuori dalla base, solo il nome.
    /// </summary>
    public static string GetRelativeLocalPath(RemoteItem item, string remoteBasePath)
    {
        string basePrefix = remoteBasePath.TrimEnd('/') + "/";
        if (!item.FullPath.StartsWith(basePrefix, StringComparison.Ordinal))
            return item.Name;

        string relative = item.FullPath[basePrefix.Length..];
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Scarica in sequenza i file della lista applicando filtro e check di esistenza.
    /// Un errore su un file non interrompe il batch; l'annullamento sì (file parziale rimosso).
    /// </summary>
    public static async Task<DownloadReport> DownloadAsync(
        IRemoteFileClient client,
        IReadOnlyList<RemoteItem> files,
        string remoteBasePath,
        string destinationFolder,
        DownloadFilter filter,
        bool overwriteAlways,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var downloaded = new List<RemoteItem>();
        var skipped = new List<RemoteItem>();
        var failed = new List<DownloadFailure>();

        var candidates = new List<RemoteItem>();
        foreach (var item in files)
        {
            if (item.IsDirectory)
                continue;
            candidates.Add(item);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = candidates[i];
            progress?.Report(new DownloadProgress(i + 1, candidates.Count, item.Name, 0));

            if (!filter.Matches(item))
            {
                skipped.Add(item);
                continue;
            }

            string localPath = Path.Combine(destinationFolder, GetRelativeLocalPath(item, remoteBasePath));
            var status = GetLocalStatus(item, localPath);

            if (filter.OnlyMissing && status != LocalFileStatus.Missing)
            {
                skipped.Add(item);
                continue;
            }

            if (!overwriteAlways && status == LocalFileStatus.Present)
            {
                skipped.Add(item);
                continue;
            }

            string? directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            int index = i;
            var byteProgress = progress is null
                ? null
                : new Progress<long>(bytes =>
                    progress.Report(new DownloadProgress(index + 1, candidates.Count, item.Name, bytes)));

            // Il trasferimento scrive su un file temporaneo nella stessa cartella (stesso volume:
            // la move finale non attraversa filesystem). Un eventuale file locale già presente
            // viene sostituito solo a trasferimento riuscito: un errore remoto non lo distrugge.
            string tempPath = localPath + PartialSuffix;
            DeletePartialFile(tempPath);

            try
            {
                var error = await client.DownloadFileAsync(item, tempPath, byteProgress, ct);
                if (error is null)
                {
                    // La sostituzione del file locale è I/O locale: se fallisce (destinazione
                    // bloccata o non scrivibile) è un errore del singolo file, non del batch.
                    try
                    {
                        File.Move(tempPath, localPath, overwrite: true);
                        downloaded.Add(item);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        DeletePartialFile(tempPath);
                        failed.Add(new DownloadFailure(
                            item, RemoteErrorMessageKeys.LocalReplaceFailed, ex.Message));
                    }
                }
                else
                {
                    DeletePartialFile(tempPath);
                    failed.Add(new DownloadFailure(item, error.MessageKey, error.Detail));
                }
            }
            catch (OperationCanceledException)
            {
                DeletePartialFile(tempPath);
                throw;
            }
        }

        return new DownloadReport(downloaded, skipped, failed);
    }

    /// <summary>Rimuove il file temporaneo di un trasferimento non completato.</summary>
    private static void DeletePartialFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pulizia best effort: un parziale non eliminabile (file in uso o permessi negati)
            // non deve mascherare l'errore originale né interrompere il batch.
        }
    }
}
