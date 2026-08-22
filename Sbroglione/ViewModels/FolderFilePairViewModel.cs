using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

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
/// Avanzamento, velocità e stato di una singola destinazione durante una copia
/// multi-destinazione: ogni destinazione procede al proprio ritmo e può fallire
/// indipendentemente dalle altre.
/// </summary>
public sealed class DestinationProgressViewModel : ReactiveObject
{
    public DestinationProgressViewModel(string path) => Path = path;

    public string Path { get; }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string? _status;
    public string? Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private string? _speedText;
    public string? SpeedText
    {
        get => _speedText;
        set => this.RaiseAndSetIfChanged(ref _speedText, value);
    }

    /// <summary>Velocità istantanea in byte/s: non a binding diretto, usata per aggregare la velocità totale del pair.</summary>
    public double CurrentBytesPerSecond { get; set; }

    private CopyStateKind _stateKind = CopyStateKind.Copying;
    public CopyStateKind StateKind
    {
        get => _stateKind;
        set => this.RaiseAndSetIfChanged(ref _stateKind, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>File attualmente in copia verso questa destinazione (sottoinsieme di FilesToProcess).</summary>
    public ObservableCollection<FileSystemItem> CopyingFiles { get; } = new();
}

/// <summary>
/// Riga della lista di copie: coppia sorgente/destinazione con stato, avanzamento
/// ed esito della verifica checksum.
/// </summary>
public class FolderFilePairViewModel : ReactiveObject
{
    private IReadOnlyList<FileSystemItem> _filesToProcess = Array.Empty<FileSystemItem>();

    /// <summary>
    /// Elenco dei file che verranno elaborati; caricato con un listing ricorsivo
    /// solo alla prima apertura dell'Expander (<see cref="IsFilesExpanded"/>) e
    /// ricaricato in blocco quando cambia <see cref="SourcePath"/> a Expander aperto.
    /// </summary>
    public IReadOnlyList<FileSystemItem> FilesToProcess
    {
        get => _filesToProcess;
        private set => this.RaiseAndSetIfChanged(ref _filesToProcess, value);
    }

    private bool _isFilesExpanded;

    /// <summary>
    /// True quando l'utente ha aperto l'Expander "Mostra file da elaborare". Ogni transizione
    /// REALE false→true rifà il listing ricorsivo (<see cref="FilesLoad"/>), anche se
    /// <see cref="SourcePath"/> non è cambiato: il contenuto della cartella può essere
    /// cambiato su disco (watch rule, modifica esterna) mentre l'Expander era chiuso.
    /// </summary>
    public bool IsFilesExpanded
    {
        get => _isFilesExpanded;
        set
        {
            bool changed = _isFilesExpanded != value;
            this.RaiseAndSetIfChanged(ref _isFilesExpanded, value);
            if (changed && value)
            {
                // Invalida la generazione dell'ultimo load: ogni apertura reale deve rifare il
                // listing, anche se SourcePath non è cambiato dall'ultima chiusura.
                _filesLoadGeneration = -1;
                FilesLoad = TriggerFilesLoad();
            }
        }
    }

    /// <summary>
    /// Task dell'ultimo listing di <see cref="FilesToProcess"/>; attendibile per sapere
    /// quando lo swap in blocco è completato (avviato solo con Expander aperto).
    /// </summary>
    public Task FilesLoad { get; private set; } = Task.CompletedTask;

    // Incrementato a ogni set di SourcePath (anche a parità di valore) e confrontato con
    // _filesLoadGeneration per evitare un doppio listing quando SourcePath e IsFilesExpanded
    // scattano entrambi in rapida sequenza (es. object initializer) o quando IsFilesExpanded
    // resta true ma RefreshSourceStateAsync prova comunque a ri-avviare il load: il primo dei
    // due trigger "vince" la generazione corrente, l'altro la trova già marcata e non duplica.
    // Il gate NON deduplica tra un'apertura e la successiva: il setter di IsFilesExpanded
    // invalida esplicitamente _filesLoadGeneration a ogni transizione reale false→true, quindi
    // riaprire l'Expander rifà sempre il listing.
    private int _sourceGeneration;
    private int _filesLoadGeneration = -1;

    /// <summary>Numero di listing effettivamente avviati; solo per verifica nei test (anti doppio-trigger).</summary>
    internal int FilesLoadStartCountForTests { get; private set; }

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
            _sourceGeneration++;

            // Azzeramento qui, in modo sincrono con il bump della generazione: la lista
            // appartiene alla sorgente precedente e non deve restare visibile. Farlo dopo
            // l'await di RefreshSourceStateAsync era una race: il load avviato dal setter
            // di IsFilesExpanded per la STESSA generazione poteva completare prima, e la
            // continuation del refresh cancellava la lista appena popolata senza che
            // TriggerFilesLoad — generazione già marcata — ne riavviasse un altro.
            FilesToProcess = Array.Empty<FileSystemItem>();
            SourceStateRefresh = RefreshSourceStateAsync();
        }
    }

    /// <summary>
    /// Verifica l'esistenza della sorgente. Il listing di <see cref="FilesToProcess"/> non
    /// parte più qui: la lista è già stata azzerata dal setter di <see cref="SourcePath"/>
    /// e riparte solo se l'Expander è già aperto (<see cref="IsFilesExpanded"/>), evitando
    /// I/O ricorsivo quando la griglia è chiusa.
    /// Se nel frattempo <see cref="SourcePath"/> cambia di nuovo, l'esito viene scartato:
    /// niente scritture su stato appartenente a una generazione più recente.
    /// </summary>
    private async Task RefreshSourceStateAsync()
    {
        string? path = _sourcePath;

        var type = await FileSystemService.GetPathTypeAsync(path);
        if (path != _sourcePath)
            return;

        SourceExists = type != PathType.Unknown;

        if (IsFilesExpanded)
            FilesLoad = TriggerFilesLoad();
    }

    /// <summary>
    /// Avvia <see cref="LoadFilesToProcessAsync"/> per la generazione corrente di
    /// <see cref="SourcePath"/>, a meno che non sia già stato avviato un load per la stessa
    /// generazione: evita il doppio listing quando <see cref="IsFilesExpanded"/> e
    /// <see cref="RefreshSourceStateAsync"/> scattano entrambi per lo stesso set di SourcePath.
    /// </summary>
    private Task TriggerFilesLoad()
    {
        if (_filesLoadGeneration == _sourceGeneration)
            return FilesLoad;

        _filesLoadGeneration = _sourceGeneration;
        FilesLoadStartCountForTests++;
        return LoadFilesToProcessAsync();
    }

    /// <summary>
    /// Esegue il listing ricorsivo della sorgente e pubblica il risultato con un unico
    /// swap (<see cref="FilesToProcess"/>), invece di un Add per item. Se <see cref="SourcePath"/>
    /// cambia nel frattempo, l'esito viene scartato.
    /// </summary>
    private async Task LoadFilesToProcessAsync()
    {
        string? path = _sourcePath;
        if (path is null || await FileSystemService.GetPathTypeAsync(path) != PathType.Directory)
        {
            if (path == _sourcePath)
                FilesToProcess = Array.Empty<FileSystemItem>();
            return;
        }

        var listing = await FileSystemService.ListFilesRecursiveAsync(path);
        if (path != _sourcePath)
            return;                                   // sorgente cambiata nel frattempo: esito scartato

        FilesToProcess = listing.Items;               // swap unico: un solo PropertyChanged
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

    /// <summary>Avanzamento per destinazione durante una copia, per il widget "in corso".</summary>
    public ObservableCollection<DestinationProgressViewModel> DestinationsProgress { get; } = new();

    /// <summary>
    /// True per le coppie ripristinate dal journal: la copia di cartelle salta
    /// i file già identici in destinazione (stessa dimensione e mtime).
    /// </summary>
    public bool SkipUnchanged { get; set; }

    /// <summary>Se true, prima di copiare svuota tutte le destinazioni (primaria + extra), previa conferma.</summary>
    public bool ClearDestinationBeforeCopy { get; set; }

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

    private string _status = LocalizationService.Tr("Str.Common.Ready");
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

    private string? _simulationSummary;

    /// <summary>Esito testuale dell'ultima simulazione (dry-run); null = nessuna simulazione visibile.</summary>
    public string? SimulationSummary
    {
        get => _simulationSummary;
        set
        {
            this.RaiseAndSetIfChanged(ref _simulationSummary, value);
            this.RaisePropertyChanged(nameof(HasSimulation));
        }
    }

    public bool HasSimulation => !string.IsNullOrEmpty(SimulationSummary);

    private string? _speedText;

    /// <summary>Riga velocità: "12.3 MB/s · media 10.1 MB/s · picco 15.2 MB/s · ETA 00:42".</summary>
    public string? SpeedText
    {
        get => _speedText;
        set => this.RaiseAndSetIfChanged(ref _speedText, value);
    }

    private IReadOnlyList<double>? _speedSamples;

    /// <summary>Campioni MB/s per la sparkline.</summary>
    public IReadOnlyList<double>? SpeedSamples
    {
        get => _speedSamples;
        set => this.RaiseAndSetIfChanged(ref _speedSamples, value);
    }
}
