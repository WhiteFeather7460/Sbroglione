using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Sbroglione.Models;
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

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null; // senza finestra non c'è input: nessuna azione.

        var dialog = new NetworkCredentialDialog
        {
            DataContext = new NetworkCredentialDialogViewModel(server)
        };

        return await dialog.ShowDialog<NetworkCredentialResult?>(owner);
    }
}
