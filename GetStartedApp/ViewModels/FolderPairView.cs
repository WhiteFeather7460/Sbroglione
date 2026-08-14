using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using Avalonia.Controls.Shapes;
using DynamicData;
using GetStartedApp.Utils;
using ReactiveUI;

namespace GetStartedApp.ViewModels;

public class FolderFilePairViewModel : ReactiveObject
{
    private ObservableCollection<FileSystemItem> _filesToProcess = new();
    public ObservableCollection<FileSystemItem> FilesToProcess { 
        get {
            _filesToProcess.Clear();

            if (FileUtils.GetPathType(_sourcePath) == PathType.Directory)
            {
                _filesToProcess.AddRange(FileUtils.PopolaTabellaFS(_sourcePath, false, true));
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
        set {
            this.RaiseAndSetIfChanged(ref _destinationPath, value);
            this.RaisePropertyChanged(nameof(CanStart));
        }
    }

    // Queste ti serviranno subito dopo quando colleghi Avvia/Stop
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


    public bool CanStart
    {
        get {
            return !IsCopying
                && !string.IsNullOrWhiteSpace(SourcePath)
                && (File.Exists(SourcePath!) || Directory.Exists(SourcePath!))
                && SourcePath != DestinationPath
                && !string.IsNullOrWhiteSpace(DestinationPath);
        }
    }


    private string? _sourceChecksum;
    public string? SourceChecksum
    {
        get => _sourceChecksum;
        set => this.RaiseAndSetIfChanged(ref _sourceChecksum, value);
    }


    private string? _destChecksum;
    public string? DestChecksum
    {
        get => _destChecksum;
        set => this.RaiseAndSetIfChanged(ref _destChecksum, value);
    }


    private bool? _isVerified; // null = non verificato, true/false = esito
    public bool? IsVerified
    {
        get => _isVerified;
        set => this.RaiseAndSetIfChanged(ref _isVerified, value);
    }



}
