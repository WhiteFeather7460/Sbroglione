using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Views;

/// <summary>
/// Finestra di selezione di un file o di una cartella.
/// Il percorso scelto viene restituito come risultato del dialogo.
/// </summary>
public partial class SelectPathDialog : Window
{
    public SelectPathDialog()
    {
        InitializeComponent();
    }

    private SelectPathDialogViewModel? ViewModel => DataContext as SelectPathDialogViewModel;

    /// <summary>
    /// Doppio click: se l'elemento è una cartella la apre, altrimenti lo seleziona e chiude.
    /// </summary>
    public void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        CloseAfterSelectElement(isDoubleTap: true);
    }

    public void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        CloseAfterSelectElement();
    }

    private void CloseAfterSelectElement(bool isDoubleTap = false)
    {
        if (ViewModel is not { } vm)
            return;

        // Doppio click su una cartella: la apre invece di selezionarla.
        if (isDoubleTap
            && vm.SelectedItem is { } selected
            && FileSystemService.GetPathType(selected.FullPath) == PathType.Directory)
        {
            vm.NavigateTo(selected.FullPath);
            return;
        }

        // Senza un elemento selezionato viene scelta la cartella corrente.
        Close(vm.SelectedItem?.FullPath ?? vm.CurrentPath);
    }

    /// <summary>
    /// Invio nella barra del percorso: equivale al pulsante "Vai".
    /// </summary>
    public void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnGoClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    public void OnGoClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Se il percorso non esiste la barra viene evidenziata in rosso.
        if (FileSystemService.GetPathType(vm.CurrentPath) == PathType.Unknown)
        {
            PathTextBar.Background = Brushes.Red;
            return;
        }

        PathTextBar.Background = Brushes.White;
        vm.NavigateTo(vm.SelectedItem?.FullPath ?? vm.CurrentPath);
        e.Handled = true;
    }

    public void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && FileSystemService.GetParentPath(vm.CurrentPath) is { } parent)
            vm.NavigateTo(parent);
    }
}
