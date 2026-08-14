
using Avalonia.Controls.ApplicationLifetimes;
using GetStartedApp.Utils;
using GetStartedApp.Views;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace GetStartedApp.ViewModels;

public class View1ViewModel : ViewModelBase
{
    public ObservableCollection<FolderFilePairViewModel> PathPairs { get; } = new();
    private readonly Dictionary<FolderFilePairViewModel, CancellationTokenSource> _ctsByPair = new();
    private async Task<string?> OpenPathDialog(bool isDest, string currentPath)
    {
        var dialog = new SelectPathDialog { DataContext = new SelectPathDialogViewModel(isDest, currentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))};

        var owner = (App.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return await dialog.ShowDialog<string?>(owner);
    }


    public ReactiveCommand<Unit, Unit> AddPairCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseSourceCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseDestinationCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> StartCopyCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> CancelCopyCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleDetailsCommand { get; }

    public View1ViewModel()
    {
        AddPairCommand = ReactiveCommand.Create(() =>
        {
            PathPairs.Add(new FolderFilePairViewModel());
        });

        BrowseSourceCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseDestinationAsync);

        // Start/Cancel: la validazione CanStart è valutata sulla riga via binding (IsEnabled)
        StartCopyCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(StartCopyAsync);
        CancelCopyCommand = ReactiveCommand.Create<FolderFilePairViewModel>(CancelCopy);

        //ToggleDetailsCommand = ReactiveCommand.Create<FolderFilePairViewModel>(PopolateExpandableTable);
    }

    private async Task BrowseSourceAsync(FolderFilePairViewModel pair)
    {
        var selected = await OpenPathDialog(false, pair.SourcePath);
        if (!string.IsNullOrEmpty(selected))
            pair.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await OpenPathDialog(true, pair.DestinationPath);

        if (!string.IsNullOrEmpty(selected)) {
            pair.DestinationPath = selected;
        }
    }

    private async Task StartCopyAsync(FolderFilePairViewModel pair)
    {
        if (!pair.CanStart)
        {
            pair.Status = "Percorsi non validi";
            return;
        }

        try
        {
            // La directory di destinazione la creo in ogni caso
            Directory.CreateDirectory(Path.GetDirectoryName(pair.DestinationPath!)!);

            // Crea/Registra CTS
            var cts = new CancellationTokenSource();
            _ctsByPair[pair] = cts;

            // Setto la copia avviata
            pair.IsCopying = true;
            pair.Progress = 0;
            pair.Status = "Copia in corso…";

            if (FileUtils.GetPathType(pair.SourcePath!) == PathType.Directory)
            {
                // Copia ricorsiva di una cartella (più file in parallelo)
                await CopyDirectoryAsync(pair, maxDegreeOfParallelism: Math.Max(2, Environment.ProcessorCount - 1), cts.Token);

                // Non vado avanti con il resto
                return;
            }

            // Se il source è un file e il dest è una cartella allora aggiorno
            bool isFileCopyToFolder = FileUtils.GetPathType(pair.SourcePath!) == PathType.File && FileUtils.GetPathType(pair.DestinationPath!) == PathType.Directory;
            string pathDestination = isFileCopyToFolder ? Path.Combine(pair.DestinationPath!, Path.GetFileName(pair.SourcePath!)) : pair.DestinationPath!;

            await CopyFileAsync(pair.SourcePath!, pathDestination,
                                p => pair.Progress = p,
                                cts.Token);

            if (!cts.IsCancellationRequested)
            {
                pair.Progress = 1;
                pair.Status = "Completato";
            }

            // === Verifica checksum dopo la copia ===
            pair.Status = "Verifica checksum…";

            // Se non l’avevi precalcolato, calcolalo ora
            pair.SourceChecksum ??= await FileUtils.ComputeChecksumAsync(pair.SourcePath!, "SHA256", cts.Token);

            // Calcolo del checksum del file di destinazione
            pair.DestChecksum = await FileUtils.ComputeChecksumAsync(pathDestination, "SHA256", cts.Token);

            pair.IsVerified = string.Equals(pair.SourceChecksum, pair.DestChecksum, StringComparison.OrdinalIgnoreCase);

            if (pair.IsVerified == true)
            {
                pair.Progress = 1;
                pair.Status = "Completato";
            }
            else
            {
                pair.Status = "Completato (checksum non corrisponde)";
            }
        }
        catch (OperationCanceledException)
        {
            pair.Status = "Annullato";
        }
        catch (Exception ex)
        {
            pair.Status = $"Errore: {ex.Message}";
        }
        finally
        {
            pair.IsCopying = false;

            if (_ctsByPair.TryGetValue(pair, out var toDispose))
            {
                toDispose.Dispose();
                _ctsByPair.Remove(pair);
            }
        }
    }

    private void CancelCopy(FolderFilePairViewModel pair)
    {
        if (_ctsByPair.TryGetValue(pair, out var cts))
            cts.Cancel();
    }

    private static async Task CopyFileAsync(
        string src,
        string dst,
        Action<long>? reportDeltaBytes,
        CancellationToken ct)
    {
        const int BUF = 1024 * 1024; // 1MB
        await using var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var outStream = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[BUF];
        int read;
        while ((read = await inStream.ReadAsync(buffer.AsMemory(0, BUF), ct)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
            reportDeltaBytes?.Invoke(read); // segnala i byte copiati in questo step
        }

        await outStream.FlushAsync(ct);
    }

    private async Task CopyDirectoryAsync(FolderFilePairViewModel pair, int maxDegreeOfParallelism, CancellationToken ct)
    {
        string srcRoot = pair.SourcePath!;
        string dstRoot = pair.DestinationPath!;

        // 1) Elenco dei file (ricorsivo) + peso totale
        var files = Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories).ToList();
        long totalBytes = files.Sum(f => new FileInfo(f).Length);
        if (totalBytes == 0 && files.Count == 0)
        {
            pair.Status = "Nessun file da copiare";
            pair.Progress = 1;
            return;
        }

        // 2) Throttling del parallelismo
        using var sem = new SemaphoreSlim(maxDegreeOfParallelism);
        long copiedBytesTotal = 0;

        pair.Status = $"Copia cartella… ({files.Count} file)";

        var tasks = files.Select(async srcFile =>
        {
            ct.ThrowIfCancellationRequested();
            await sem.WaitAsync(ct);
            try
            {
                // Dest path: sostituisci il prefisso della root sorgente con la root destinazione
                string relative = Path.GetRelativePath(srcRoot, srcFile);
                string dstFile = Path.Combine(dstRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);

                // Copia il file con progresso incrementale
                await CopyFileAsync(srcFile, dstFile, deltaBytes =>
                {
                    // aggiorno il cumulato in modo thread-safe
                    long newTotal = Interlocked.Add(ref copiedBytesTotal, deltaBytes);
                    pair.Progress = totalBytes > 0 ? (double)newTotal / totalBytes : 1.0;
                }, ct);
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (!ct.IsCancellationRequested)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
        }
    }

}
