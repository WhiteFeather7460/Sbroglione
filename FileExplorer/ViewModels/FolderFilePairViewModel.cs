using System.Collections.ObjectModel;
using System.IO;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Riga della lista di copie: coppia sorgente/destinazione con stato, avanzamento
/// ed esito della verifica checksum.
/// </summary>
public class FolderFilePairViewModel : ReactiveObject
{
    private readonly ObservableCollection<FileSystemItem> _filesToProcess = new();

    /// <summary>
    /// Elenco dei file che verranno elaborati, ricalcolato a ogni lettura
    /// (viene rinotificato quando cambia <see cref="SourcePath"/>).
    /// </summary>
    public ObservableCollection<FileSystemItem> FilesToProcess
    {
        get
        {
            _filesToProcess.Clear();

            if (FileSystemService.GetPathType(_sourcePath) == PathType.Directory)
            {
                foreach (var item in FileSystemService.ListFilesRecursive(_sourcePath!))
                {
                    _filesToProcess.Add(item);
                }
            }

            return _filesToProcess;
        }
    }

    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourcePath, value);
            this.RaisePropertyChanged(nameof(CanStart));
            this.RaisePropertyChanged(nameof(FilesToProcess));
        }
    }

    private string? _destinationPath;
    public string? DestinationPath
    {
        get => _destinationPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _destinationPath, value);
            this.RaisePropertyChanged(nameof(CanStart));
        }
    }

    private bool _isCopying;
    public bool IsCopying
    {
        get => _isCopying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isCopying, value);
            this.RaisePropertyChanged(nameof(CanStart));
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string _status = "Pronto";
    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>
    /// True quando la coppia è pronta per avviare una copia.
    /// </summary>
    public bool CanStart =>
        !IsCopying
        && !string.IsNullOrWhiteSpace(SourcePath)
        && (File.Exists(SourcePath) || Directory.Exists(SourcePath))
        && SourcePath != DestinationPath
        && !string.IsNullOrWhiteSpace(DestinationPath);

    private string? _sourceChecksum;
    public string? SourceChecksum
    {
        get => _sourceChecksum;
        set => this.RaiseAndSetIfChanged(ref _sourceChecksum, value);
    }

    private string? _destinationChecksum;
    public string? DestinationChecksum
    {
        get => _destinationChecksum;
        set => this.RaiseAndSetIfChanged(ref _destinationChecksum, value);
    }

    // null = non ancora verificato, true/false = esito della verifica.
    private bool? _isVerified;
    public bool? IsVerified
    {
        get => _isVerified;
        set => this.RaiseAndSetIfChanged(ref _isVerified, value);
    }
}
