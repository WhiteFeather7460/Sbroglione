using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        // Il caricamento iniziale parte all'apertura: il costruttore del ViewModel
        // non fa I/O (i percorsi di rete possono essere lenti o irraggiungibili).
        Opened += async (_, _) =>
        {
            if (ViewModel is { } vm)
            {
                await vm.RefreshAsync();
                UpdatePathBarErrorClass();
            }
        };
    }

    private SelectPathDialogViewModel? ViewModel => DataContext as SelectPathDialogViewModel;

    /// <summary>
    /// Doppio click: se l'elemento è una cartella la apre, altrimenti lo seleziona e chiude.
    /// </summary>
    public async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Doppio click su una cartella: la apre invece di selezionarla.
        if (vm.SelectedItem is { IsDirectory: true } selected)
        {
            await vm.NavigateToAsync(selected.FullPath);
            UpdatePathBarErrorClass();
            return;
        }

        CloseAfterSelectElement();
    }

    public void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        CloseAfterSelectElement();
    }

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void CloseAfterSelectElement()
    {
        if (ViewModel is not { } vm)
            return;

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

    public async void OnGoClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        await vm.NavigateToAsync(vm.CurrentPath);
        UpdatePathBarErrorClass();
    }

    public async void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && FileSystemService.GetParentPath(vm.CurrentPath) is { } parent)
        {
            await vm.NavigateToAsync(parent);
            UpdatePathBarErrorClass();
        }
    }

    /// <summary>
    /// Evidenzia la barra del percorso con la classe "error" quando l'ultimo caricamento è fallito.
    /// </summary>
    private void UpdatePathBarErrorClass()
    {
        if (ViewModel is not { } vm)
            return;

        if (vm.ErrorMessage is not null)
            PathTextBar.Classes.Add("error");
        else
            PathTextBar.Classes.Remove("error");
    }
}
