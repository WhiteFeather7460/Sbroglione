using System;
using ReactiveUI;

namespace FileExplorer.Models;

/// <summary>
/// Elemento (file o cartella) mostrato nelle liste del file system.
/// </summary>
public class FileSystemItem : ReactiveObject
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string? Size { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsDirectory { get; set; }
    public string Icon => IsDirectory ? "📁" : "📄";

    private string? _checkSum;
    public string? CheckSum
    {
        get => _checkSum;
        set => this.RaiseAndSetIfChanged(ref _checkSum, value);
    }
}
