using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Esito di un elenco remoto: voci trovate ed eventuale errore (elenco vuoto se errore).</summary>
public sealed record RemoteListingResult(IReadOnlyList<RemoteItem> Items, RemoteError? Error);
