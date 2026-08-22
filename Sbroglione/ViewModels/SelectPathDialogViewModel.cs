using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>
/// ViewModel della finestra di selezione file/cartella.
/// Con <c>directoriesOnly</c> (scelta della destinazione) mostra solo cartelle.
/// </summary>
public class SelectPathDialogViewModel : ReactiveObject
{
    private readonly bool _directoriesOnly;

    /// <summary>True se il dialog deve rifiutare la conferma su una cartella (serve un file).</summary>
    public bool FilesOnly { get; }

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

    public SelectPathDialogViewModel(bool directoriesOnly, string startPath, bool filesOnly = false)
    {
        _directoriesOnly = directoriesOnly;
        _currentPath = startPath;
        FilesOnly = filesOnly;
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
            ErrorMessage = result.Error is null ? null : TranslateListingError(result.Error);

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

    /// <summary>
    /// Traduce l'identificatore stabile e indipendente dalla lingua emesso da
    /// <see cref="FileSystemService.CreateListingError"/> nel testo mostrato in UI. Confine
    /// Service→ViewModel: stesso pattern di <c>RemoteBrowserViewModel.TranslateRemoteMessage</c>.
    /// </summary>
    private static string TranslateListingError(ListingError error) => error.MessageKey switch
    {
        ListingErrorMessageKeys.NotFound => LocalizationService.Tr("Str.SelectPathDialog.Error.NotFound"),
        ListingErrorMessageKeys.AccessDenied => LocalizationService.Tr("Str.SelectPathDialog.Error.AccessDenied"),
        ListingErrorMessageKeys.Unavailable => string.Format(
            LocalizationService.Tr("Str.SelectPathDialog.Error.UnavailableFormat"), error.Detail),
        ListingErrorMessageKeys.Generic => error.Detail ?? error.MessageKey,
        _ => error.Detail ?? error.MessageKey,
    };
}
