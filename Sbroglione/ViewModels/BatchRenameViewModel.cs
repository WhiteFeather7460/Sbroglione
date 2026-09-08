using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.ViewModels;

public sealed class BatchRenameViewModel : ViewModelBase, IDisposable
{
    private readonly List<(string Path, DateTime LastModified)> _files = new();

    private string _folderPath = "";
    public string FolderPath
    {
        get => _folderPath;
        private set => this.RaiseAndSetIfChanged(ref _folderPath, value);
    }

    public ObservableCollection<RenamePreviewItemViewModel> Items { get; } = new();

    private bool _hasItems;
    public bool HasItems
    {
        get => _hasItems;
        private set => this.RaiseAndSetIfChanged(ref _hasItems, value);
    }

    private string _findPattern = "";
    public string FindPattern
    {
        get => _findPattern;
        set => this.RaiseAndSetIfChanged(ref _findPattern, value);
    }

    private string _replacePattern = "";
    public string ReplacePattern
    {
        get => _replacePattern;
        set => this.RaiseAndSetIfChanged(ref _replacePattern, value);
    }

    private bool _useRegex;
    public bool UseRegex
    {
        get => _useRegex;
        set => this.RaiseAndSetIfChanged(ref _useRegex, value);
    }

    private string _template = "";
    public string Template
    {
        get => _template;
        set => this.RaiseAndSetIfChanged(ref _template, value);
    }

    private int _counterStart = 1;
    public int CounterStart
    {
        get => _counterStart;
        set => this.RaiseAndSetIfChanged(ref _counterStart, value);
    }

    private int _counterStep = 1;
    public int CounterStep
    {
        get => _counterStep;
        set => this.RaiseAndSetIfChanged(ref _counterStep, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public ReactiveCommand<Unit, Unit> BrowseFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }

    private readonly IDisposable _previewSubscription;

    public BatchRenameViewModel()
    {
        BrowseFolderCommand = ReactiveCommand.CreateFromTask(BrowseFolderAsync);
        ApplyCommand = ReactiveCommand.CreateFromTask(ApplyAsync);
        UndoCommand = ReactiveCommand.CreateFromTask(UndoAsync);

        _previewSubscription = this
            .WhenAnyValue(x => x.FindPattern, x => x.ReplacePattern, x => x.UseRegex, x => x.Template, x => x.CounterStart, x => x.CounterStep)
            .Skip(1)
            .Subscribe(_ => RecomputePreview());
    }

    private async Task BrowseFolderAsync()
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, FolderPath);
        if (string.IsNullOrEmpty(selected))
            return;

        FolderPath = selected;
        await LoadFolderAsync();
    }

    private async Task LoadFolderAsync()
    {
        _files.Clear();
        Items.Clear();

        DirectoryListingResult listing = await FileSystemService.ListDirectoryAsync(FolderPath, directoriesOnly: false);
        foreach (FileSystemItem item in listing.Items.Where(i => !i.IsDirectory))
            _files.Add((item.FullPath, item.LastModified));

        RecomputePreview();
    }

    private void RecomputePreview()
    {
        if (_files.Count == 0)
        {
            HasItems = false;
            return;
        }

        var options = new RenameOptions(UseRegex, FindPattern, ReplacePattern, Template, CounterStart, CounterStep);
        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(_files, options);

        Items.Clear();
        foreach (RenamePlanItem planItem in plan)
        {
            Items.Add(new RenamePreviewItemViewModel(planItem.OriginalPath, planItem.OriginalName)
            {
                NewName = planItem.NewName,
                HasConflict = planItem.HasConflict,
                Error = planItem.Error,
            });
        }

        HasItems = Items.Count > 0;
    }

    private async Task ApplyAsync()
    {
        var options = new RenameOptions(UseRegex, FindPattern, ReplacePattern, Template, CounterStart, CounterStep);
        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(_files, options);

        BatchRenameResult result = await BatchRenameService.ExecuteAsync(plan);
        StatusText = string.Format(LocalizationService.Tr("Str.BatchRename.ResultFormat"), result.SuccessCount, result.Failures.Count);

        await LoadFolderAsync();
    }

    private async Task UndoAsync()
    {
        BatchRenameResult result = await BatchRenameService.UndoLastBatchAsync();
        StatusText = result.SuccessCount == 0 && result.Failures.Count == 0
            ? LocalizationService.Tr("Str.BatchRename.NothingToUndo")
            : string.Format(LocalizationService.Tr("Str.BatchRename.UndoResultFormat"), result.SuccessCount, result.Failures.Count);

        if (!string.IsNullOrEmpty(FolderPath))
            await LoadFolderAsync();
    }

    public void Dispose() => _previewSubscription.Dispose();
}
