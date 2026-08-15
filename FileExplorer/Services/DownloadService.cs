using System;
using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>
/// Orchestrazione dei download remoti: check di esistenza locale, filtri, batch e report.
/// </summary>
public static class DownloadService
{
    /// <summary>Tolleranza sul confronto delle date di modifica (timestamp FTP poco precisi).</summary>
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

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
}
