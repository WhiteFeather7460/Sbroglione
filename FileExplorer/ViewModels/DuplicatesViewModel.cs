using System;
using System.Collections.ObjectModel;
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

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

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
        Groups.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasGroups));

        BrowseRootCommand = ReactiveCommand.CreateFromTask(BrowseRootAsync);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        CancelScanCommand = ReactiveCommand.Create(() => { _scanCts?.Cancel(); });
        DeleteFileCommand = ReactiveCommand.CreateFromTask<DuplicateFileViewModel>(DeleteFileAsync);
        KeepFirstCommand = ReactiveCommand.CreateFromTask<DuplicateGroupViewModel>(KeepFirstAsync);
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
        Groups.Clear();
        IsScanning = true;
        StatusText = "Analisi…";

        try
        {
            var found = await DuplicateFinderService.FindDuplicatesAsync(
                RootPath,
                Math.Max(2, Environment.ProcessorCount - 1),
                progress => StatusText = $"{progress.Stage}: {progress.Processed}/{progress.Total}",
                _scanCts.Token);

            foreach (var group in found)
                Groups.Add(new DuplicateGroupViewModel(group));

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

    /// <summary>Annulla e rilascia un'eventuale scansione in corso (chiusura della vista).</summary>
    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        GC.SuppressFinalize(this);
    }
}
