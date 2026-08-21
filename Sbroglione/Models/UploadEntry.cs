namespace Sbroglione.Models;

/// <summary>File locale da caricare: percorso assoluto e percorso remoto relativo alla cartella di destinazione.</summary>
public sealed record UploadEntry(string LocalPath, string RemoteRelativePath);
