using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Orchestrazione degli upload remoti: check di esistenza sul server, skip/overwrite e report batch.
/// </summary>
public static class UploadService
{
    /// <summary>Tolleranza sul confronto delle date di modifica (timestamp FTP poco precisi).</summary>
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Combina la cartella remota di destinazione con un percorso relativo già normalizzato con '/'
    /// dal chiamante. I backslash restano tali: su Linux sono caratteri legittimi di un nome file
    /// e convertirli creerebbe sottocartelle remote inesistenti.
    /// </summary>
    public static string CombineRemotePath(string remoteBasePath, string relativePath) =>
        remoteBasePath.TrimEnd('/') + "/" + relativePath.TrimStart('/');

    /// <summary>
    /// Carica in sequenza le voci indicate. Una voce già presente sul server con stessa
    /// dimensione e data viene saltata a meno di <paramref name="overwriteAlways"/>.
    /// Un errore su un file non interrompe il batch; l'annullamento sì.
    /// </summary>
    public static async Task<UploadReport> UploadAsync(
        IRemoteFileClient client,
        IReadOnlyList<UploadEntry> entries,
        string remoteBasePath,
        bool overwriteAlways,
        IProgress<UploadProgress>? progress,
        CancellationToken ct)
    {
        var uploaded = new List<UploadEntry>();
        var skipped = new List<UploadEntry>();
        var failed = new List<UploadFailure>();

        // Con overwriteAlways la mappa non viene mai consultata: evitiamo l'elenco ricorsivo.
        var existing = overwriteAlways
            ? new Dictionary<string, RemoteItem>()
            : await BuildExistingRemoteMapAsync(client, remoteBasePath, ct);

        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[i];
            progress?.Report(new UploadProgress(i + 1, entries.Count, Path.GetFileName(entry.LocalPath), 0));

            string remotePath = CombineRemotePath(remoteBasePath, entry.RemoteRelativePath);

            if (!overwriteAlways && existing.TryGetValue(remotePath, out var remoteItem)
                && IsSameAsLocal(entry.LocalPath, remoteItem))
            {
                skipped.Add(entry);
                continue;
            }

            int index = i;
            var byteProgress = progress is null
                ? null
                : new Progress<long>(bytes =>
                    progress.Report(new UploadProgress(index + 1, entries.Count, Path.GetFileName(entry.LocalPath), bytes)));

            var error = await client.UploadFileAsync(entry.LocalPath, remotePath, byteProgress, ct);
            if (error is null)
                uploaded.Add(entry);
            else
                failed.Add(new UploadFailure(entry, error.Message));
        }

        return new UploadReport(uploaded, skipped, failed);
    }

    /// <summary>
    /// Elenco ricorsivo del server sotto la cartella di destinazione, indicizzato per percorso
    /// completo. Un errore di listing (es. cartella non ancora esistente) non blocca l'upload:
    /// semplicemente nessun file viene considerato già presente.
    /// </summary>
    private static async Task<Dictionary<string, RemoteItem>> BuildExistingRemoteMapAsync(
        IRemoteFileClient client, string remoteBasePath, CancellationToken ct)
    {
        var result = await client.ListRecursiveAsync(remoteBasePath, ct);
        if (result.Error is not null)
            return new Dictionary<string, RemoteItem>();

        return result.Items.ToDictionary(i => i.FullPath, i => i);
    }

    private static bool IsSameAsLocal(string localPath, RemoteItem remote)
    {
        var info = new FileInfo(localPath);
        return info.Exists
            && info.Length == remote.Size
            && (info.LastWriteTime - remote.Modified).Duration() <= DateTolerance;
    }
}
