using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura del dialog di input testo, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la risposta dell'utente.
/// </summary>
internal static class InputDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<string, string, string?, Task<string?>>? Override { get; set; }

    public static async Task<string?> ShowAsync(string title, string message, string? initialText)
    {
        if (Override is not null)
            return await Override(title, message, initialText);

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null; // senza finestra non c'è input: nessuna azione.

        var dialog = new InputDialog
        {
            DataContext = new InputDialogViewModel(title, message, initialText)
        };

        return await dialog.ShowDialog<string?>(owner);
    }
}
