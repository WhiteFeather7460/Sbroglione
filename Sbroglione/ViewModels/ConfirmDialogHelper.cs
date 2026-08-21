using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
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

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return false; // senza finestra non c'è conferma: default sicuro, non si elimina nulla.

        var dialog = new ConfirmDialog
        {
            DataContext = new ConfirmDialogViewModel(title, message, confirmLabel)
        };

        return await dialog.ShowDialog<bool?>(owner) ?? false;
    }
}
