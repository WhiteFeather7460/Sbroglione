using ReactiveUI;

namespace Sbroglione.ViewModels;

public sealed class RenamePreviewItemViewModel : ReactiveObject
{
    public string OriginalPath { get; }
    public string OriginalName { get; }

    private string _newName;
    public string NewName
    {
        get => _newName;
        set => this.RaiseAndSetIfChanged(ref _newName, value);
    }

    private bool _hasConflict;
    public bool HasConflict
    {
        get => _hasConflict;
        set => this.RaiseAndSetIfChanged(ref _hasConflict, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public RenamePreviewItemViewModel(string originalPath, string originalName)
    {
        OriginalPath = originalPath;
        OriginalName = originalName;
        _newName = originalName;
    }
}
