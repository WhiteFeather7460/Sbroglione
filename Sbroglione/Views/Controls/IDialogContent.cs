using System;

namespace Sbroglione.Views.Controls;

/// <summary>
/// Corpo di un dialogo, indipendente dall'host che lo presenta: su desktop vive dentro una
/// <see cref="Avalonia.Controls.Window"/> mostrata con <c>ShowDialog</c>, su single-view (Android)
/// dentro l'<see cref="OverlayDialogHost"/>. Al posto di chiudere una finestra, il contenuto
/// segnala il proprio esito con <see cref="Completed"/>; l'host traduce l'evento nel proprio
/// meccanismo di chiusura (<c>Close(result)</c> oppure completamento del task dell'overlay).
/// </summary>
/// <typeparam name="TResult">
/// Tipo del risultato, già nullable dove "nessuna scelta" è un esito possibile
/// (es. <c>string?</c>, <c>bool?</c>): <c>default</c> rappresenta l'annullamento.
/// </typeparam>
public interface IDialogContent<TResult>
{
    /// <summary>Segnalato una sola volta, quando l'utente conferma o annulla.</summary>
    event Action<TResult>? Completed;
}
