using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>Apertura del dialog di selezione percorso, condivisa tra le schede.</summary>
internal static class SelectPathDialogHelper
{
    public static async Task<string?> ShowAsync(bool directoriesOnly, string? currentPath, bool filesOnly = false)
    {
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly,
                ResolveStartDirectory(currentPath),
                filesOnly)
        };

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null;

        return await dialog.ShowDialog<string?>(owner);
    }

    /// <summary>
    /// Il dialog elenca il contenuto di <c>currentPath</c>: se questo è un file (es. l'ultima
    /// selezione fatta nel picker "solo file"), va risolto alla cartella che lo contiene,
    /// altrimenti il primo caricamento fallisce come se il picker fosse "rotto".
    /// </summary>
    private static string ResolveStartDirectory(string? currentPath)
    {
        if (string.IsNullOrEmpty(currentPath))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (File.Exists(currentPath))
            return Path.GetDirectoryName(currentPath) is { Length: > 0 } dir
                ? dir
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return currentPath;
    }
}
