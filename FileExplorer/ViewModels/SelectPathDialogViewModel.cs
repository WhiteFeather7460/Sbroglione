using System.Collections.ObjectModel;
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

    public SelectPathDialogViewModel(bool directoriesOnly, string startPath)
    {
        _directoriesOnly = directoriesOnly;
        _currentPath = startPath;
        LoadItems();
    }

    /// <summary>
    /// Naviga al percorso indicato e ricarica l'elenco.
    /// </summary>
    public void NavigateTo(string path)
    {
        CurrentPath = path;
        LoadItems();
    }

    private void LoadItems()
    {
        Items.Clear();
        foreach (var item in FileSystemService.ListDirectory(CurrentPath, _directoriesOnly))
        {
            Items.Add(item);
        }
    }
}
