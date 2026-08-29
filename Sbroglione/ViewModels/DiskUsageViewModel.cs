using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>
/// Scheda "Spazio disco": scansione di una cartella e navigazione della lista gerarchica
/// (drill-down nei nodi cartella, risalita lungo la catena visitata).
/// </summary>
public class DiskUsageViewModel : ViewModelBase, IDisposable
{
    private readonly List<DiskUsageNode> _breadcrumb = new();
    private CancellationTokenSource? _scanCts;

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

    private string _statusText = LocalizationService.Tr("Str.Common.Ready");
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private DiskUsageNode? _currentNode;
    public DiskUsageNode? CurrentNode
    {
        get => _currentNode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentNode, value);
            this.RaisePropertyChanged(nameof(CurrentPathText));
            this.RaisePropertyChanged(nameof(CanNavigateUp));
        }
    }

    public string CurrentPathText => _currentNode is null
        ? ""
        : string.Format(LocalizationService.Tr("Str.DiskUsage.PathSizeFormat"), _currentNode.FullPath, SizeFormatter.Format(_currentNode.SizeBytes));

    public bool CanNavigateUp => _breadcrumb.Count > 0;

    public ReactiveCommand<Unit, Unit> BrowseRootCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelScanCommand { get; }
    public ReactiveCommand<Unit, Unit> NavigateUpCommand { get; }

    /// <summary>
    /// Scatta dopo ogni strato scansionato (incluso il primo), a struttura già visibile: la
    /// vista lo usa per ridisegnare <c>HierarchyListControl</c> senza cambiare il riferimento
    /// di <see cref="CurrentNode"/> (che resta lo stesso albero, via via più completo).
    /// </summary>
    public event Action? StructureUpdated;

    public DiskUsageViewModel()
    {
        BrowseRootCommand = ReactiveCommand.CreateFromTask(BrowseRootAsync);
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        CancelScanCommand = ReactiveCommand.Create(() => { _scanCts?.Cancel(); });
        NavigateUpCommand = ReactiveCommand.Create(NavigateUp);
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
            StatusText = LocalizationService.Tr("Str.Common.SelectValidFolder");
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = LocalizationService.Tr("Str.Common.Analyzing");
        _breadcrumb.Clear();
        CurrentNode = null;

        var structureShown = false;

        try
        {
            var root = await DiskUsageService.BuildTreeLayeredAsync(
                RootPath,
                node => UiDispatch.InvokeAsync(() =>
                {
                    if (!structureShown)
                    {
                        structureShown = true;
                        CurrentNode = node;
                        StatusText = LocalizationService.Tr("Str.DiskUsage.ScanningInBackground");
                    }

                    this.RaisePropertyChanged(nameof(CurrentPathText));
                    StructureUpdated?.Invoke();
                }),
                _scanCts.Token);

            StatusText = string.Format(LocalizationService.Tr("Str.DiskUsage.TotalFormat"), SizeFormatter.Format(root.SizeBytes));
            this.RaisePropertyChanged(nameof(CurrentPathText));
            StructureUpdated?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Tr("Str.Common.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationService.Tr("Str.Common.ErrorFormat"), ex.Message);
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>Entra in un nodo cartella (no-op su file, cartelle vuote o senza scansione).</summary>
    public void DrillDown(DiskUsageNode node)
    {
        if (CurrentNode is null || !node.IsDirectory || node.Children.Count == 0)
            return;

        _breadcrumb.Add(CurrentNode);
        CurrentNode = node;
    }

    /// <summary>Risale al nodo precedente della catena visitata.</summary>
    public void NavigateUp()
    {
        if (_breadcrumb.Count == 0)
            return;

        var parent = _breadcrumb[^1];
        _breadcrumb.RemoveAt(_breadcrumb.Count - 1);
        CurrentNode = parent;
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
