namespace Sbroglione.Models;

/// <summary>
/// Stato di avanzamento di un singolo file nella lista "File da elaborare".
/// </summary>
public enum FileCopyStatus
{
    Pending,
    Copying,
    Done
}
