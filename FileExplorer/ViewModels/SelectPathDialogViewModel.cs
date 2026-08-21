using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// ViewModel della finestra di selezione file/cartella.
/// Con <c>directoriesOnly</c> (scelta della destinazione) mostra solo cartelle.
/// </summary>
public class SelectPathDialogViewModel : ReactiveObject
{
    private readonly bool _directoriesOnly;

    private string _currentPath;
    public string CurrentPath
    {
        get => _currentPath;
        set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    public ObservableCollection<FileSystemItem> Items { get; } = new();

    private FileSystemItem? _selectedItem;
    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    private bool _isLoading;

    /// <summary>True mentre l'elenco è in caricamento (percorsi di rete possono essere lenti).</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string? _errorMessage;

    /// <summary>Messaggio d'errore dell'ultimo caricamento, o null se riuscito.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public SelectPathDialogViewModel(bool directoriesOnly, string startPath)
    {
        _directoriesOnly = directoriesOnly;
        _currentPath = startPath;
    }

    /// <summary>
    /// Naviga al percorso indicato e ricarica l'elenco.
    /// </summary>
    public Task NavigateToAsync(string path)
    {
        CurrentPath = path;
        return RefreshAsync();
    }

    /// <summary>
    /// Ricarica l'elenco del percorso corrente, esponendo eventuali errori in <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            Items.Clear();

            // Un percorso UNC fuori da Windows non è accessibile direttamente:
            // la condivisione va montata dal sistema operativo.
            if (!OperatingSystem.IsWindows() && FileSystemService.IsUncPath(CurrentPath))
            {
                ErrorMessage = LocalizationService.Tr("Str.SelectPathDialog.UncNotSupported");
                return;
            }

            var result = await FileSystemService.ListDirectoryAsync(CurrentPath, _directoriesOnly);
            ErrorMessage = result.Error?.Message;

            foreach (var item in result.Items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
