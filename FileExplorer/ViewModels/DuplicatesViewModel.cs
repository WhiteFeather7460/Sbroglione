using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Riga file dentro un gruppo di duplicati.</summary>
public class DuplicateFileViewModel
{
    public DuplicateFileViewModel(DuplicateGroupViewModel group, string filePath)
    {
        Group = group;
        FilePath = filePath;
    }

    public DuplicateGroupViewModel Group { get; }
    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public string Directory => Path.GetDirectoryName(FilePath) ?? "";
}

/// <summary>Gruppo di file identici, con intestazione riepilogativa.</summary>
public class DuplicateGroupViewModel : ReactiveObject
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        FileSize = group.FileSize;
        foreach (var path in group.FilePaths)
            Files.Add(new DuplicateFileViewModel(this, path));

        Files.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Header));
    }

    public long FileSize { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; } = new();

    public string Header =>
        $"{Files.Count} copie · {SizeFormatter.Format(FileSize)} l'una · spreco {SizeFormatter.Format(FileSize * Math.Max(0, Files.Count - 1))}";
}

/// <summary>
/// Scheda "Duplicati": scansione di una cartella alla ricerca di file identici,
/// con eliminazione per singolo file o per gruppo ("tieni solo il primo").
/// </summary>
public class DuplicatesViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _scanCts;

    private ObservableCollection<DuplicateGroupViewModel> _groups = new();
    public ObservableCollection<DuplicateGroupViewModel> Groups
    {
        get => _groups;
        set
        {
            _groups.CollectionChanged -= OnGroupsChanged;
            this.RaiseAndSetIfChanged(ref _groups, value);
            _groups.CollectionChanged += OnGroupsChanged;
            this.RaisePropertyChanged(nameof(HasGroups));
        }
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        this.RaisePropertyChanged(nameof(HasGroups));

    public bool HasGroups => Groups.Count > 0;

    private string? _rootPath;
    public string? RootPath
    {
        get => _rootPath;
        set => this.RaiseAndSetIfChanged(ref _rootPath, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    private string _statusText = "Pronto";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public ReactiveCommand<Unit, Unit> BrowseRootCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelScanCommand { get; }
    public ReactiveCommand<DuplicateFileViewModel, Unit> DeleteFileCommand { get; }
    public ReactiveCommand<DuplicateGroupViewModel, Unit> KeepFirstCommand { get; }

    public DuplicatesViewModel()
    {
        Groups.CollectionChanged += OnGroupsChanged;

        BrowseRootCommand = ReactiveCommand.CreateFromTask(BrowseRootAsync);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        CancelScanCommand = ReactiveCommand.Create(() => { _scanCts?.Cancel(); });
        DeleteFileCommand = ReactiveCommand.CreateFromTask<DuplicateFileViewModel>(ConfirmAndDeleteFileAsync);
        KeepFirstCommand = ReactiveCommand.CreateFromTask<DuplicateGroupViewModel>(ConfirmAndKeepFirstAsync);
    }

    private async Task BrowseRootAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, RootPath);
        if (!string.IsNullOrEmpty(selected))
            RootPath = selected;
    }

    public async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            StatusText = "Selezionare una cartella valida";
            return;
        }

        _scanCts = new CancellationTokenSource();
        Groups = new ObservableCollection<DuplicateGroupViewModel>();
        IsScanning = true;
        StatusText = "Analisi…";

        try
        {
            // Il callback arriva da threadpool e in parallelo: throttle sulla frequenza,
            // set su thread UI e clamp monotono sul contatore pubblicato. Il gate è per
            // fase ("Hash parziale"/"Hash completo"): ogni fase riparte da 1.
            var progressThrottle = new UiProgressThrottle();
            var progressGates = new ConcurrentDictionary<string, MonotonicProgressGate>(StringComparer.Ordinal);
            var found = await DuplicateFinderService.FindDuplicatesAsync(
                RootPath,
                Math.Max(2, Environment.ProcessorCount - 1),
                progress =>
                {
                    if (!progressThrottle.ShouldPublish())
                        return;

                    string stage = progress.Stage;
                    int processed = progress.Processed;
                    int total = progress.Total;
                    UiDispatch.Post(() =>
                    {
                        if (progressGates.GetOrAdd(stage, _ => new MonotonicProgressGate()).TryAdvance(processed))
                            StatusText = $"{stage}: {processed}/{total}";
                    });
                },
                _scanCts.Token);

            var groups = new ObservableCollection<DuplicateGroupViewModel>(
                found.Select(group => new DuplicateGroupViewModel(group)));
            Groups = groups; // un solo reset per la UI

            StatusText = found.Count == 0
                ? "Nessun duplicato trovato"
                : found.Count == 1 ? "1 gruppo di duplicati" : $"{found.Count} gruppi di duplicati";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Annullato";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    public async Task DeleteFileAsync(DuplicateFileViewModel file)
    {
        try
        {
            await Task.Run(() => File.Delete(file.FilePath));
        }
        catch (Exception ex)
        {
            StatusText = $"Errore eliminazione: {ex.Message}";
            return;
        }

        file.Group.Files.Remove(file);
        if (file.Group.Files.Count < 2)
            Groups.Remove(file.Group);
    }

    public async Task KeepFirstAsync(DuplicateGroupViewModel group)
    {
        foreach (var file in group.Files.Skip(1).ToList())
            await DeleteFileAsync(file);
    }

    /// <summary>Chiede conferma e poi elimina il singolo file. Pubblico per i test.</summary>
    public async Task ConfirmAndDeleteFileAsync(DuplicateFileViewModel file)
    {
        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Elimina file",
            $"Eliminare definitivamente \"{file.FilePath}\"?\nL'operazione non è reversibile.",
            "Elimina");

        if (confirmed)
            await DeleteFileAsync(file);
    }

    /// <summary>Chiede conferma una sola volta per il gruppo e poi elimina tutte le copie tranne la prima. Pubblico per i test.</summary>
    public async Task ConfirmAndKeepFirstAsync(DuplicateGroupViewModel group)
    {
        var toDelete = group.Files.Skip(1).ToList();
        if (toDelete.Count == 0)
            return;

        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Tieni solo il primo",
            $"Eliminare definitivamente {toDelete.Count} file ({SizeFormatter.Format(group.FileSize * toDelete.Count)})?\n" +
            $"Resta solo \"{group.Files[0].FilePath}\". L'operazione non è reversibile.",
            "Elimina");

        if (confirmed)
            await KeepFirstAsync(group);
    }

    /// <summary>Annulla e rilascia un'eventuale scansione in corso (chiusura della vista).</summary>
    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        GC.SuppressFinalize(this);
    }
}
