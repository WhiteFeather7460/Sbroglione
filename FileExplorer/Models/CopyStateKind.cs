namespace FileExplorer.Models;

/// <summary>
/// Stato di presentazione di una coppia di copia: pilota colore e classe del badge.
/// </summary>
public enum CopyStateKind
{
    Ready,
    Copying,
    Success,
    Warning,
    Error,
    Cancelled
}
