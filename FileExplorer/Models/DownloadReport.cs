using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>File fallito con motivo presentabile.</summary>
public sealed record DownloadFailure(RemoteItem Item, string Reason);

/// <summary>Avanzamento del batch di download.</summary>
public sealed record DownloadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di download.</summary>
public sealed record DownloadReport(
    IReadOnlyList<RemoteItem> Downloaded,
    IReadOnlyList<RemoteItem> Skipped,
    IReadOnlyList<DownloadFailure> Failed);
