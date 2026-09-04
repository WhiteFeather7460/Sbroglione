using System;
using System.IO;
using System.Threading.Tasks;

using Sbroglione.Services;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>
/// Apertura del dialog di selezione percorso, condivisa tra le schede.
/// <see cref="Override"/> permette ai test (senza UI) di simulare la scelta dell'utente.
/// </summary>
internal static class SelectPathDialogHelper
{
    /// <summary>Solo per i test: se impostato, sostituisce il dialog reale. Ripristinare a null in Dispose.</summary>
    internal static Func<bool, string?, bool, Task<string?>>? Override { get; set; }

    public static async Task<string?> ShowAsync(bool directoriesOnly, string? currentPath, bool filesOnly = false)
    {
        if (Override is not null)
            return await Override(directoriesOnly, currentPath, filesOnly);

        // Senza host non c'è selezione: nessuna azione. Il dialogo viene costruito dal presenter
        // solo sul ramo effettivamente usato, mai per essere subito abbandonato.
        return await DialogPresenter.ShowAsync<SelectPathDialogContent, string?>(
            () => new SelectPathDialog(),
            () => new SelectPathDialogContent(),
            new SelectPathDialogViewModel(
                directoriesOnly,
                ResolveStartDirectory(currentPath),
                filesOnly));
    }

    /// <summary>
    /// Il dialog elenca il contenuto di <c>currentPath</c>: se questo è un file (es. l'ultima
    /// selezione fatta nel picker "solo file"), va risolto alla cartella che lo contiene,
    /// altrimenti il primo caricamento fallisce come se il picker fosse "rotto".
    /// </summary>
    private static string ResolveStartDirectory(string? currentPath)
    {
        if (string.IsNullOrEmpty(currentPath))
            return PlatformPaths.DefaultRootPath;

        if (File.Exists(currentPath))
            return Path.GetDirectoryName(currentPath) is { Length: > 0 } dir
                ? dir
                : PlatformPaths.DefaultRootPath;

        return currentPath;
    }
}
