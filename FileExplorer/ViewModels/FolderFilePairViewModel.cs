using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Destinazione aggiuntiva di una coppia di copia (copia multi-destinazione).</summary>
public class ExtraDestinationViewModel
{
    public ExtraDestinationViewModel(FolderFilePairViewModel owner, string path)
    {
        Owner = owner;
        Path = path;
    }

    public FolderFilePairViewModel Owner { get; }
    public string Path { get; }
}

/// <summary>
/// Riga della lista di copie: coppia sorgente/destinazione con stato, avanzamento
/// ed esito della verifica checksum.
/// </summary>
public class FolderFilePairViewModel : ReactiveObject
{
    private readonly ObservableCollection<FileSystemItem> _filesToProcess = new();

    /// <summary>
    /// Elenco dei file che verranno elaborati; ricaricato in background
    /// quando cambia <see cref="SourcePath"/>.
    /// </summary>
    public ObservableCollection<FileSystemItem> FilesToProcess => _filesToProcess;

    private bool _sourceExists;

    /// <summary>
    /// True se la sorgente esiste sul disco; aggiornato in background al cambio di
    /// <see cref="SourcePath"/> (il controllo su percorsi di rete può essere lento).
    /// </summary>
    public bool SourceExists
    {
        get => _sourceExists;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sourceExists, value);
            this.RaisePropertyChanged(nameof(CanStart));
        }
    }

    /// <summary>
    /// Task dell'ultima verifica della sorgente; attendibile per sapere quando
    /// <see cref="SourceExists"/> e <see cref="FilesToProcess"/> sono aggiornati.
    /// </summary>
    public Task SourceStateRefresh { get; private set; } = Task.CompletedTask;

    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourcePath, value);
            this.RaisePropertyChanged(nameof(CanStart));
            SourceStateRefresh = RefreshSourceStateAsync();
        }
    }

    /// <summary>
    /// Verifica l'esistenza della sorgente e ricarica <see cref="FilesToProcess"/>.
    /// Se nel frattempo <see cref="SourcePath"/> cambia di nuovo, l'esito viene scartato.
    /// </summary>
    private async Task RefreshSourceStateAsync()
    {
        string? path = _sourcePath;

        var type = await FileSystemService.GetPathTypeAsync(path);
        if (path != _sourcePath)
            return;

        SourceExists = type != PathType.Unknown;
        _filesToProcess.Clear();

        if (type != PathType.Directory)
            return;

        var listing = await FileSystemService.ListFilesRecursiveAsync(path!);
        if (path != _sourcePath)
            return;

        foreach (var item in listing.Items)
        {
            _filesToProcess.Add(item);
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

    /// <summary>Destinazioni aggiuntive oltre a <see cref="DestinationPath"/>.</summary>
    public ObservableCollection<ExtraDestinationViewModel> ExtraDestinations { get; } = new();

    /// <summary>Tutte le destinazioni (primaria + extra). Valido solo quando CanStart è true.</summary>
    public IReadOnlyList<string> AllDestinations =>
        new[] { DestinationPath! }.Concat(ExtraDestinations.Select(e => e.Path)).ToList();

    /// <summary>
    /// True per le coppie ripristinate dal journal: la copia di cartelle salta
    /// i file già identici in destinazione (stessa dimensione e mtime).
    /// </summary>
    public bool SkipUnchanged { get; set; }

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

    private CopyStateKind _stateKind = CopyStateKind.Ready;

    /// <summary>
    /// Stato di presentazione del badge; impostato da <see cref="CopyPairsViewModel"/>
    /// negli stessi punti in cui viene impostato <see cref="Status"/>.
    /// </summary>
    public CopyStateKind StateKind
    {
        get => _stateKind;
        set => this.RaiseAndSetIfChanged(ref _stateKind, value);
    }

    /// <summary>
    /// True quando la coppia è pronta per avviare una copia.
    /// L'esistenza della sorgente è verificata in background (<see cref="SourceExists"/>):
    /// nessun I/O sincrono durante la valutazione del binding.
    /// </summary>
    public bool CanStart =>
        !IsCopying
        && !string.IsNullOrWhiteSpace(SourcePath)
        && SourceExists
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
