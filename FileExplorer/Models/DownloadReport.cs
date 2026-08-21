using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>
/// File fallito. <paramref name="MessageKey"/>/<paramref name="Detail"/>: vedi
/// <see cref="RemoteErrorMessageKeys"/> — nessun testo tradotto qui, il confine è il ViewModel.
/// </summary>
public sealed record DownloadFailure(RemoteItem Item, string MessageKey, string? Detail = null);

/// <summary>Avanzamento del batch di download.</summary>
public sealed record DownloadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di download.</summary>
public sealed record DownloadReport(
    IReadOnlyList<RemoteItem> Downloaded,
    IReadOnlyList<RemoteItem> Skipped,
    IReadOnlyList<DownloadFailure> Failed);
