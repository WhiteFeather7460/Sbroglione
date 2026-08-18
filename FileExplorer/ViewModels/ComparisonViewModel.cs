using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Confronto": confronta due directory (cascata dimensione → SHA-256)
/// ed esporta il report in HTML/CSV/JSON.
/// </summary>
public class ComparisonViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _compareCts;

    public ComparisonViewModel()
    {
        BrowseLeftCommand = ReactiveCommand.CreateFromTask(BrowseLeftAsync);
        BrowseRightCommand = ReactiveCommand.CreateFromTask(BrowseRightAsync);
        CompareCommand = ReactiveCommand.CreateFromTask(CompareAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExportHtmlCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Html));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Csv));
        ExportJsonCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Json));
    }

    public ReactiveCommand<Unit, Unit> BrowseLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportHtmlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }

    private string? _leftPath;
    public string? LeftPath
    {
        get => _leftPath;
        set => this.RaiseAndSetIfChanged(ref _leftPath, value);
    }

    private string? _rightPath;
    public string? RightPath
    {
        get => _rightPath;
        set => this.RaiseAndSetIfChanged(ref _rightPath, value);
    }

    private bool _isComparing;
    public bool IsComparing
    {
        get => _isComparing;
        private set => this.RaiseAndSetIfChanged(ref _isComparing, value);
    }

    private string _statusText = "Selezionare due cartelle da confrontare";
    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private DirectoryComparisonResult? _result;
    public DirectoryComparisonResult? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(HasResult));
            this.RaisePropertyChanged(nameof(LeftOnlyCount));
            this.RaisePropertyChanged(nameof(RightOnlyCount));
            this.RaisePropertyChanged(nameof(DifferentCount));
            this.RaisePropertyChanged(nameof(IdenticalCount));
        }
    }

    public bool HasResult => Result is not null;
    public int LeftOnlyCount => Result?.LeftOnly.Count ?? 0;
    public int RightOnlyCount => Result?.RightOnly.Count ?? 0;
    public int DifferentCount => Result?.Different.Count ?? 0;
    public int IdenticalCount => Result?.Identical.Count ?? 0;

    private async Task BrowseLeftAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, LeftPath);
        if (!string.IsNullOrEmpty(selected))
            LeftPath = selected;
    }

    private async Task BrowseRightAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, RightPath);
        if (!string.IsNullOrEmpty(selected))
            RightPath = selected;
    }

    /// <summary>Confronta le due directory selezionate. Pubblico per i test.</summary>
    public async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath)
            || !Directory.Exists(LeftPath) || !Directory.Exists(RightPath))
        {
            StatusText = "Selezionare due cartelle esistenti";
            return;
        }

        _compareCts?.Cancel();
        _compareCts?.Dispose();
        _compareCts = new CancellationTokenSource();
        var ct = _compareCts.Token;

        IsComparing = true;
        Result = null;
        StatusText = "Confronto in corso…";

        try
        {
            int parallelism = Math.Max(2, Environment.ProcessorCount - 1);
            var result = await DirectoryComparisonService.CompareAsync(
                LeftPath, RightPath, parallelism,
                progress => StatusText = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            Result = result;
            StatusText = $"{result.Identical.Count} identici, {result.Different.Count} diversi, " +
                         $"{result.LeftOnly.Count} solo a sinistra, {result.RightOnly.Count} solo a destra";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Confronto annullato";
        }
        catch (Exception ex)
        {
            StatusText = $"Errore: {ex.Message}";
        }
        finally
        {
            IsComparing = false;
        }
    }

    private void Cancel() => _compareCts?.Cancel();

    private async Task BrowseAndExportAsync(ComparisonReportFormat format)
    {
        if (Result is null)
            return;

        var targetDirectory = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, null);
        if (string.IsNullOrEmpty(targetDirectory))
            return;

        await ExportAsync(format, targetDirectory);
    }

    /// <summary>Esporta l'ultimo risultato nella cartella indicata; ritorna il path scritto o null. Pubblico per i test.</summary>
    public async Task<string?> ExportAsync(ComparisonReportFormat format, string targetDirectory)
    {
        if (Result is null)
            return null;

        try
        {
            DateTime generatedUtc = DateTime.UtcNow;
            string filePath = Path.Combine(
                targetDirectory, ComparisonReportExporter.SuggestFileName(format, generatedUtc));

            await ComparisonReportExporter.ExportAsync(
                filePath, Result, format, LeftPath!, RightPath!, generatedUtc, CancellationToken.None);

            StatusText = $"Report esportato: {filePath}";
            return filePath;
        }
        catch (Exception ex)
        {
            StatusText = $"Errore esportazione: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        _compareCts?.Cancel();
        _compareCts?.Dispose();
        _compareCts = null;
        GC.SuppressFinalize(this);
    }
}
