using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>
/// Categoria di errore di una lettura del file system.
/// </summary>
public enum ListingErrorKind
{
    /// <summary>Il percorso non esiste.</summary>
    NotFound,

    /// <summary>Permessi insufficienti (es. share di rete non autenticata).</summary>
    AccessDenied,

    /// <summary>Errore di I/O, tipicamente rete non raggiungibile.</summary>
    Unavailable
}

/// <summary>
/// Errore di lettura con categoria e messaggio presentabile all'utente.
/// </summary>
public sealed record ListingError(ListingErrorKind Kind, string Message);

/// <summary>
/// Esito di un elenco di file/cartelle: elementi trovati ed eventuale errore.
/// In caso di errore l'elenco è vuoto.
/// </summary>
public sealed record DirectoryListingResult(IReadOnlyList<FileSystemItem> Items, ListingError? Error);
