using System;
using System.Threading.Tasks;

using Sbroglione.Services;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura del dialog di conferma, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la risposta dell'utente.
/// </summary>
internal static class ConfirmDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<string, string, string, Task<bool>>? Override { get; set; }

    public static async Task<bool> ShowAsync(string title, string message, string confirmLabel)
    {
        if (Override is not null)
            return await Override(title, message, confirmLabel);

        // Senza host non c'è conferma: default sicuro, non si elimina nulla.
        return await DialogPresenter.ShowAsync<ConfirmDialogContent, bool?>(
            () => new ConfirmDialog(),
            () => new ConfirmDialogContent(),
            new ConfirmDialogViewModel(title, message, confirmLabel)) ?? false;
    }
}
