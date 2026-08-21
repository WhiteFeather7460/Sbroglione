using System.Collections.Generic;

namespace Sbroglione.Models;

/// <summary>
/// Voce fallita. <paramref name="MessageKey"/>/<paramref name="Detail"/>: vedi
/// <see cref="RemoteErrorMessageKeys"/> — nessun testo tradotto qui, il confine è il ViewModel.
/// </summary>
public sealed record UploadFailure(UploadEntry Entry, string MessageKey, string? Detail = null);

/// <summary>Avanzamento del batch di upload.</summary>
public sealed record UploadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di upload.</summary>
public sealed record UploadReport(
    IReadOnlyList<UploadEntry> Uploaded,
    IReadOnlyList<UploadEntry> Skipped,
    IReadOnlyList<UploadFailure> Failed);
