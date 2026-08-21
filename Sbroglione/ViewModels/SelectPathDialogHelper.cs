using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Sbroglione.Views;

namespace Sbroglione.ViewModels;

/// <summary>Apertura del dialog di selezione percorso, condivisa tra le schede.</summary>
internal static class SelectPathDialogHelper
{
    public static async Task<string?> ShowAsync(bool directoriesOnly, string? currentPath)
    {
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly,
                currentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        };

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null;

        return await dialog.ShowDialog<string?>(owner);
    }
}
