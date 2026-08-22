using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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

    /// <summary>Vista filtrata di <see cref="Items"/> mostrata dalla griglia.</summary>
    public ObservableCollection<FileSystemItem> VisibleItems { get; } = new();

    /// <summary>Debounce applicato a <see cref="ScheduleRebuild"/>; i test lo azzerano.</summary>
    internal static TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(200);
    private CancellationTokenSource? _filterCts;

    /// <summary>Task dell'ultimo rebuild filtri programmato; attendibile nei test.</summary>
    public Task FilterRefresh { get; private set; } = Task.CompletedTask;

    private string? _filterPattern;
    public string? FilterPattern
    {
        get => _filterPattern;
        set { this.RaiseAndSetIfChanged(ref _filterPattern, value); ScheduleRebuild(); }
    }

    private string? _filterMinSizeKb;
    public string? FilterMinSizeKb
    {
        get => _filterMinSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMinSizeKb, value); ScheduleRebuild(); }
    }

    private string? _filterMaxSizeKb;
    public string? FilterMaxSizeKb
    {
        get => _filterMaxSizeKb;
        set { this.RaiseAndSetIfChanged(ref _filterMaxSizeKb, value); ScheduleRebuild(); }
    }

    private DateTimeOffset? _filterModifiedAfter;
    public DateTimeOffset? FilterModifiedAfter
    {
        get => _filterModifiedAfter;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedAfter, value); ScheduleRebuild(); }
    }

    private DateTimeOffset? _filterModifiedBefore;
    public DateTimeOffset? FilterModifiedBefore
    {
        get => _filterModifiedBefore;
        set { this.RaiseAndSetIfChanged(ref _filterModifiedBefore, value); ScheduleRebuild(); }
    }

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
            RebuildVisibleItems();
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

    /// <summary>True se il file passa nome, dimensione e data. Le cartelle passano sempre.</summary>
    private bool MatchesFilter(FileSystemItem item)
    {
        if (item.IsDirectory)
            return true;

        if (!MatchesName(item.Name))
            return false;

        long? minBytes = ParseKb(FilterMinSizeKb);
        long? maxBytes = ParseKb(FilterMaxSizeKb);
        if (minBytes is not null && item.SizeBytes < minBytes)
            return false;
        if (maxBytes is not null && item.SizeBytes > maxBytes)
            return false;

        if (FilterModifiedAfter is not null && item.LastModified < FilterModifiedAfter.Value.DateTime)
            return false;
        if (FilterModifiedBefore is not null && item.LastModified > FilterModifiedBefore.Value.DateTime)
            return false;

        return true;
    }

    private bool MatchesName(string name)
    {
        if (string.IsNullOrWhiteSpace(FilterPattern))
            return true;

        return FilterPattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(pattern => Regex.IsMatch(
                name,
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// KB → byte; testo vuoto, non numerico, negativo o così grande da traboccare = nessun limite
    /// (un overflow darebbe una soglia negativa e ribalterebbe il senso del filtro).
    /// </summary>
    private static long? ParseKb(string? text) =>
        long.TryParse(text, out long kb) && kb >= 0 && kb <= long.MaxValue / 1024 ? kb * 1024 : null;

    /// <summary>Riallinea <see cref="VisibleItems"/> a <see cref="Items"/> applicando il filtro.</summary>
    private void RebuildVisibleItems()
    {
        VisibleItems.Clear();
        foreach (var item in Items)
        {
            if (MatchesFilter(item))
                VisibleItems.Add(item);
        }
    }

    /// <summary>
    /// Programma un rebuild di <see cref="VisibleItems"/> dopo <see cref="FilterDebounce"/>: più
    /// set ravvicinati (es. l'utente che digita nel filtro) cancellano il rebuild precedente
    /// invece di accodarne uno per ogni carattere.
    /// </summary>
    private void ScheduleRebuild()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = _filterCts = new CancellationTokenSource();
        FilterRefresh = RebuildAfterDebounceAsync(cts.Token);
    }

    private async Task RebuildAfterDebounceAsync(CancellationToken ct)
    {
        try { await Task.Delay(FilterDebounce, ct); }
        catch (OperationCanceledException) { return; }
        UiDispatch.Post(RebuildVisibleItems);
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
