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
/// Identificatori stabili e indipendenti dalla lingua per <see cref="ListingError.MessageKey"/>.
/// La traduzione avviene al confine ViewModel (<c>SelectPathDialogViewModel</c>), mai in
/// <c>FileSystemService</c> — stesso pattern di <c>RemoteErrorMessageKeys</c>.
/// </summary>
public static class ListingErrorMessageKeys
{
    public const string NotFound = "NotFound";
    public const string AccessDenied = "AccessDenied";

    /// <summary>Detail: <c>ex.Message</c> (errore di I/O tipicamente di rete).</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>
    /// Detail: <c>ex.Message</c> di un'eccezione non riconosciuta. Dinamico e già in linguaggio
    /// naturale (prodotto dal runtime/OS): mostrato così com'è, senza ulteriore traduzione.
    /// </summary>
    public const string Generic = "Generic";
}

/// <summary>
/// Errore di lettura. <paramref name="MessageKey"/> è un identificatore stabile (vedi
/// <see cref="ListingErrorMessageKeys"/>), <paramref name="Detail"/> l'eventuale dato dinamico.
/// </summary>
public sealed record ListingError(ListingErrorKind Kind, string MessageKey, string? Detail = null);

/// <summary>
/// Esito di un elenco di file/cartelle: elementi trovati ed eventuale errore.
/// In caso di errore l'elenco è vuoto.
/// </summary>
public sealed record DirectoryListingResult(IReadOnlyList<FileSystemItem> Items, ListingError? Error);
