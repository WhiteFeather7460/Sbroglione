using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Sbroglione.Services;
using Sbroglione.ViewModels;
using Sbroglione.Views.Controls;

namespace Sbroglione.Views;

/// <summary>
/// Corpo del selettore di file/cartella, indipendente dall'host: restituisce il percorso
/// scelto, <c>null</c> su annulla.
/// </summary>
public partial class SelectPathDialogContent : UserControl, IDialogContent<string?>
{
    private bool _initialized;
    private bool _manualEntry = AndroidRuntime.IsNotAndroid;

    public SelectPathDialogContent()
    {
        InitializeComponent();
        UpdatePathRowVisibility();

        // Il caricamento iniziale parte all'aggancio all'albero visuale: il costruttore del
        // ViewModel non fa I/O (i percorsi di rete possono essere lenti o irraggiungibili).
        AttachedToVisualTree += (_, _) => _ = InitializeAsync();
    }

    /// <inheritdoc />
    public event Action<string?>? Completed;

    private SelectPathDialogViewModel? ViewModel => DataContext as SelectPathDialogViewModel;

    /// <summary>
    /// Primo caricamento del contenuto della cartella. Idempotente: l'host può invocarlo a sua
    /// volta (la <see cref="SelectPathDialog"/> lo fa su <c>Opened</c>) senza raddoppiare l'I/O.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized || ViewModel is not { } vm)
            return;

        _initialized = true;
        await vm.RefreshAsync();
        UpdatePathBarErrorClass();
    }

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

        CompleteAfterSelectElement();
    }

    public void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        CompleteAfterSelectElement();
    }

    public void OnCancelClick(object? sender, RoutedEventArgs e) => Completed?.Invoke(null);

    private void CompleteAfterSelectElement()
    {
        if (ViewModel is not { } vm)
            return;

        // Senza un elemento selezionato viene scelta la cartella corrente.
        if (vm.SelectedItem is null)
        {
            if (!vm.FilesOnly)
                Completed?.Invoke(vm.CurrentPath);
            return;
        }

        // FilesOnly: una cartella selezionata non è una risposta valida, la si apre invece.
        if (vm.FilesOnly && vm.SelectedItem.IsDirectory)
        {
            _ = OpenDirectoryAsync(vm.SelectedItem.FullPath);
            return;
        }

        Completed?.Invoke(vm.SelectedItem.FullPath);
    }

    private async Task OpenDirectoryAsync(string path)
    {
        if (ViewModel is not { } vm)
            return;

        await vm.NavigateToAsync(path);
        UpdatePathBarErrorClass();
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
        RevertToBreadcrumbOnSuccessIfAndroid(vm);
    }

    /// <summary>
    /// Mostra/nasconde le due varianti della barra del percorso: editor manuale (desktop, o
    /// Android dopo aver toccato la matita) contro breadcrumb (Android, di default).
    /// </summary>
    private void UpdatePathRowVisibility()
    {
        ManualPathRow.IsVisible = _manualEntry;
        BreadcrumbPathRow.IsVisible = !_manualEntry;
    }

    /// <summary>
    /// Tocco sulla matita: passa dalla breadcrumb all'editor manuale per digitare/incollare
    /// un percorso (es. UNC).
    /// </summary>
    public void OnEditPathClick(object? sender, RoutedEventArgs e)
    {
        _manualEntry = true;
        UpdatePathRowVisibility();
        PathTextBar.Focus();
    }

    /// <summary>
    /// Tocco su un segmento della breadcrumb: naviga al percorso cumulativo di quel segmento.
    /// </summary>
    public async void OnBreadcrumbSegmentClicked(object? sender, string path)
    {
        if (ViewModel is not { } vm)
            return;

        await vm.NavigateToAsync(path);
        UpdatePathBarErrorClass();
    }

    public async void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && FileSystemService.GetParentPath(vm.CurrentPath) is { } parent)
        {
            await vm.NavigateToAsync(parent);
            UpdatePathBarErrorClass();
            RevertToBreadcrumbOnSuccessIfAndroid(vm);
        }
    }

    /// <summary>
    /// Su Android, dopo una navigazione riuscita dall'editor manuale, torna alla breadcrumb:
    /// l'editor è una via di fuga (es. percorsi UNC), non la modalità di default.
    /// </summary>
    private void RevertToBreadcrumbOnSuccessIfAndroid(SelectPathDialogViewModel vm)
    {
        if (AndroidRuntime.IsAndroid && vm.ErrorMessage is null)
        {
            _manualEntry = false;
            UpdatePathRowVisibility();
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
