using System;
using System.Threading.Tasks;

using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura del dialog credenziali di rete, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la risposta dell'utente.
/// </summary>
internal static class NetworkCredentialDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<string, Task<NetworkCredentialResult?>>? Override { get; set; }

    public static async Task<NetworkCredentialResult?> ShowAsync(string server)
    {
        if (Override is not null)
            return await Override(server);

        // Senza host non c'è input: nessuna azione.
        return await DialogPresenter.ShowAsync<NetworkCredentialDialogContent, NetworkCredentialResult?>(
            () => new NetworkCredentialDialog(),
            () => new NetworkCredentialDialogContent(),
            new NetworkCredentialDialogViewModel(server));
    }
}
