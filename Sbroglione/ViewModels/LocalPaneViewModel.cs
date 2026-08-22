using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>
/// Pannello locale del browser remoto: navigazione, elenco e operazioni di cartella
/// (create/rinomina/elimina) sul file system locale. Modellata su
/// <see cref="SelectPathDialogViewModel"/>, con l'aggiunta delle operazioni di cartella.
/// </summary>
public class LocalPaneViewModel : ReactiveObject
{
    private string _currentPath;
    public string CurrentPath
    {
        get => _currentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    public ObservableCollection<FileSystemItem> Items { get; } = new();

    private FileSystemItem? _selectedItem;
    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public LocalPaneViewModel(string startPath)
    {
        _currentPath = startPath;
    }

    public Task NavigateToAsync(string path)
    {
        CurrentPath = path;
        return RefreshAsync();
    }

    public Task NavigateUpAsync()
    {
        string? parent = FileSystemService.GetParentPath(CurrentPath);
        return parent is null || parent == CurrentPath ? Task.CompletedTask : NavigateToAsync(parent);
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var result = await FileSystemService.ListDirectoryAsync(CurrentPath, directoriesOnly: false);
            ErrorMessage = result.Error is null ? null : TranslateListingError(result.Error);

            Items.Clear();
            foreach (var item in result.Items
                         .OrderByDescending(i => i.IsDirectory)
                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                Items.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task CreateFolderAsync(string name)
    {
        var error = await FileSystemService.CreateDirectoryAsync(CurrentPath, name);
        ErrorMessage = error is null ? null : TranslateListingError(error);
        if (error is null)
            await RefreshAsync();
    }

    public async Task RenameSelectedAsync(string newName)
    {
        if (SelectedItem is not { } item)
            return;

        var error = await FileSystemService.RenameAsync(item.FullPath, newName);
        ErrorMessage = error is null ? null : TranslateListingError(error);
        if (error is null)
            await RefreshAsync();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedItem is not { } item)
            return;

        var error = await FileSystemService.DeleteAsync(item.FullPath);
        ErrorMessage = error is null ? null : TranslateListingError(error);
        if (error is null)
            await RefreshAsync();
    }

    /// <summary>
    /// Traduce l'identificatore stabile emesso da <see cref="FileSystemService"/> nel testo
    /// mostrato in UI. Confine Service→ViewModel: stesso pattern di
    /// <see cref="SelectPathDialogViewModel"/>.
    /// </summary>
    private static string TranslateListingError(ListingError error) => error.MessageKey switch
    {
        ListingErrorMessageKeys.NotFound => LocalizationService.Tr("Str.LocalPane.Error.NotFound"),
        ListingErrorMessageKeys.AccessDenied => LocalizationService.Tr("Str.LocalPane.Error.AccessDenied"),
        ListingErrorMessageKeys.AlreadyExists => LocalizationService.Tr("Str.LocalPane.Error.AlreadyExists"),
        ListingErrorMessageKeys.Unavailable => string.Format(
            LocalizationService.Tr("Str.LocalPane.Error.UnavailableFormat"), error.Detail),
        ListingErrorMessageKeys.Generic => error.Detail ?? error.MessageKey,
        _ => error.Detail ?? error.MessageKey,
    };
}
