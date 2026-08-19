// FileExplorer/ViewModels/ComparisonViewModel.cs
using System;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Confronto": confronta due directory (cascata dimensione → SHA-256)
/// ed esporta il report in HTML/CSV/JSON; confronta inoltre due file byte per byte
/// (primo offset diverso, percentuale identica, intervalli differenti).
/// </summary>
public class ComparisonViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo ItCulture = CultureInfo.GetCultureInfo("it-IT");

    private CancellationTokenSource? _compareCts;
    private CancellationTokenSource? _fileCompareCts;
    private string? _comparedLeftRoot;
    private string? _comparedRightRoot;

    public ComparisonViewModel()
    {
        BrowseLeftCommand = ReactiveCommand.CreateFromTask(BrowseLeftAsync);
        BrowseRightCommand = ReactiveCommand.CreateFromTask(BrowseRightAsync);
        CompareCommand = ReactiveCommand.CreateFromTask(CompareAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ExportHtmlCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Html));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Csv));
        ExportJsonCommand = ReactiveCommand.CreateFromTask(() => BrowseAndExportAsync(ComparisonReportFormat.Json));
        BrowseLeftFileCommand = ReactiveCommand.CreateFromTask(BrowseLeftFileAsync);
        BrowseRightFileCommand = ReactiveCommand.CreateFromTask(BrowseRightFileAsync);
        CompareFilesCommand = ReactiveCommand.CreateFromTask(CompareFilesAsync);
        CancelFileCompareCommand = ReactiveCommand.Create(CancelFileCompare);
    }

    public ReactiveCommand<Unit, Unit> BrowseLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportHtmlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseLeftFileCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseRightFileCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareFilesCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelFileCompareCommand { get; }

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

    private string? _leftFilePath;
    public string? LeftFilePath
    {
        get => _leftFilePath;
        set => this.RaiseAndSetIfChanged(ref _leftFilePath, value);
    }

    private string? _rightFilePath;
    public string? RightFilePath
    {
        get => _rightFilePath;
        set => this.RaiseAndSetIfChanged(ref _rightFilePath, value);
    }

    private bool _isFileComparing;
    public bool IsFileComparing
    {
        get => _isFileComparing;
        private set => this.RaiseAndSetIfChanged(ref _isFileComparing, value);
    }

    private string _fileCompareStatus = "Selezionare due file da confrontare";
    public string FileCompareStatus
    {
        get => _fileCompareStatus;
        private set => this.RaiseAndSetIfChanged(ref _fileCompareStatus, value);
    }

    private FileCompareResult? _fileResult;
    public FileCompareResult? FileResult
    {
        get => _fileResult;
        private set
        {
            this.RaiseAndSetIfChanged(ref _fileResult, value);
            this.RaisePropertyChanged(nameof(HasFileResult));
            this.RaisePropertyChanged(nameof(FirstDiffText));
            this.RaisePropertyChanged(nameof(IdenticalPercentText));
            this.RaisePropertyChanged(nameof(RangeCountText));
        }
    }

    public bool HasFileResult => FileResult is not null;

    public string FirstDiffText => FileResult switch
    {
        null => string.Empty,
        { FirstDifferenceOffset: long offset } =>
            $"Primo byte diverso: offset {offset.ToString("N0", ItCulture)} (0x{offset:X})",
        _ => "Nessuna differenza"
    };

    public string IdenticalPercentText => FileResult is { } result
        ? string.Format(ItCulture, "{0:0.##} % identico", result.IdenticalFraction * 100)
        : string.Empty;

    public string RangeCountText => FileResult is { } result
        ? $"{result.DifferentRanges.Count} intervalli differenti" +
          (result.RangesTruncated ? " (elenco troncato)" : string.Empty)
        : string.Empty;

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

    private async Task BrowseLeftFileAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: false, LeftFilePath);
        if (!string.IsNullOrEmpty(selected))
            LeftFilePath = selected;
    }

    private async Task BrowseRightFileAsync()
    {
        var selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: false, RightFilePath);
        if (!string.IsNullOrEmpty(selected))
            RightFilePath = selected;
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
            // Catturati prima dell'await: le TextBox restano editabili durante il confronto,
            // quindi LeftPath/RightPath potrebbero cambiare mentre CompareAsync è in corso.
            string left = LeftPath;
            string right = RightPath;

            int parallelism = Math.Max(2, Environment.ProcessorCount - 1);
            var result = await DirectoryComparisonService.CompareAsync(
                left, right, parallelism,
                progress => StatusText = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            Result = result;
            _comparedLeftRoot = left;
            _comparedRightRoot = right;
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

    /// <summary>Confronta i due file selezionati byte per byte. Pubblico per i test.</summary>
    public async Task CompareFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftFilePath) || string.IsNullOrWhiteSpace(RightFilePath)
            || !File.Exists(LeftFilePath) || !File.Exists(RightFilePath))
        {
            FileCompareStatus = "Selezionare due file esistenti";
            return;
        }

        _fileCompareCts?.Cancel();
        _fileCompareCts?.Dispose();
        _fileCompareCts = new CancellationTokenSource();
        var ct = _fileCompareCts.Token;

        IsFileComparing = true;
        FileResult = null;
        FileCompareStatus = "Confronto in corso…";

        try
        {
            // Catturati prima dell'await, come per il confronto directory.
            string left = LeftFilePath;
            string right = RightFilePath;

            var result = await FileByteCompareService.CompareAsync(
                left, right,
                progress => FileCompareStatus = $"Confronto in corso… ({progress.Processed}/{progress.Total})",
                ct);

            FileResult = result;
            FileCompareStatus = result.AreIdentical
                ? "File identici"
                : $"File diversi ({result.DifferentRanges.Count} intervalli)";
        }
        catch (OperationCanceledException)
        {
            FileCompareStatus = "Confronto annullato";
        }
        catch (Exception ex)
        {
            FileCompareStatus = $"Errore: {ex.Message}";
        }
        finally
        {
            IsFileComparing = false;
        }
    }

    private void CancelFileCompare() => _fileCompareCts?.Cancel();

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
        if (Result is null || _comparedLeftRoot is null || _comparedRightRoot is null)
            return null;

        try
        {
            DateTime generatedUtc = DateTime.UtcNow;
            string filePath = Path.Combine(
                targetDirectory, ComparisonReportExporter.SuggestFileName(format, generatedUtc));

            await ComparisonReportExporter.ExportAsync(
                filePath, Result, format, _comparedLeftRoot, _comparedRightRoot, generatedUtc, CancellationToken.None);

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
        _fileCompareCts?.Cancel();
        _fileCompareCts?.Dispose();
        _fileCompareCts = null;
        GC.SuppressFinalize(this);
    }
}
