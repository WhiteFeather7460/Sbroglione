using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Voce fallita con motivo presentabile.</summary>
public sealed record UploadFailure(UploadEntry Entry, string Reason);

/// <summary>Avanzamento del batch di upload.</summary>
public sealed record UploadProgress(int FileIndex, int TotalFiles, string CurrentFile, long BytesTransferred);

/// <summary>Esito complessivo di un batch di upload.</summary>
public sealed record UploadReport(
    IReadOnlyList<UploadEntry> Uploaded,
    IReadOnlyList<UploadEntry> Skipped,
    IReadOnlyList<UploadFailure> Failed);
