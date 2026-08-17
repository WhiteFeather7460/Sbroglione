using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.Views;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Copia file": gestisce la lista di coppie sorgente/destinazione e
/// avvia/annulla le copie con verifica checksum.
/// </summary>
public class CopyPairsViewModel : ViewModelBase
{
    public ObservableCollection<FolderFilePairViewModel> PathPairs { get; } = new();

    /// <summary>True se c'è almeno una coppia in lista (pilota l'empty state).</summary>
    public bool HasPairs => PathPairs.Count > 0;

    private readonly Dictionary<FolderFilePairViewModel, CancellationTokenSource> _ctsByPair = new();

    public ReactiveCommand<Unit, Unit> AddPairCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseSourceCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> BrowseDestinationCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> StartCopyCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> CancelCopyCommand { get; }
    public ReactiveCommand<FolderFilePairViewModel, Unit> AddExtraDestinationCommand { get; }
    public ReactiveCommand<ExtraDestinationViewModel, Unit> RemoveExtraDestinationCommand { get; }

    public CopyPairsViewModel()
    {
        PathPairs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasPairs));

        AddPairCommand = ReactiveCommand.Create(() => PathPairs.Add(new FolderFilePairViewModel()));

        BrowseSourceCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseDestinationAsync);

        // Start/Cancel: la validazione CanStart è valutata sulla singola riga via binding (IsEnabled).
        StartCopyCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(StartCopyAsync);
        CancelCopyCommand = ReactiveCommand.Create<FolderFilePairViewModel>(CancelCopy);

        AddExtraDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(AddExtraDestinationAsync);
        RemoveExtraDestinationCommand = ReactiveCommand.Create<ExtraDestinationViewModel>(
            extra => extra.Owner.ExtraDestinations.Remove(extra));

        JournalRestore = RestoreInterruptedJobsAsync();
    }

    /// <summary>
    /// Task del ripristino delle copie interrotte, avviato dal costruttore.
    /// I test lo attendono; la UI non ne ha bisogno.
    /// </summary>
    public Task JournalRestore { get; }

    /// <summary>
    /// Ripropone come coppie "interrotte" le voci rimaste nel journal
    /// (copie in corso al momento di un crash/chiusura), poi svuota il journal.
    /// </summary>
    private async Task RestoreInterruptedJobsAsync()
    {
        List<CopyJobRecord> jobs = await CopyJournalStore.LoadAsync();
        if (jobs.Count == 0)
            return;

        await CopyJournalStore.ClearAsync();

        foreach (var job in jobs)
        {
            var pair = new FolderFilePairViewModel
            {
                SourcePath = job.SourcePath,
                DestinationPath = job.DestinationPath,
                SkipUnchanged = true,
                Status = "Interrotto — premere Avvia per riprendere",
                StateKind = CopyStateKind.Warning
            };

            foreach (var extra in job.ExtraDestinations)
                pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, extra));

            PathPairs.Add(pair);
        }
    }

    private async Task BrowseSourceAsync(FolderFilePairViewModel pair)
    {
        var selected = await ShowSelectPathDialogAsync(directoriesOnly: false, pair.SourcePath);
        if (!string.IsNullOrEmpty(selected))
            pair.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await ShowSelectPathDialogAsync(directoriesOnly: true, pair.DestinationPath);
        if (!string.IsNullOrEmpty(selected))
            pair.DestinationPath = selected;
    }

    private async Task AddExtraDestinationAsync(FolderFilePairViewModel pair)
    {
        var selected = await ShowSelectPathDialogAsync(directoriesOnly: true, pair.DestinationPath);
        if (!string.IsNullOrEmpty(selected))
            pair.ExtraDestinations.Add(new ExtraDestinationViewModel(pair, selected));
    }

    private static async Task<string?> ShowSelectPathDialogAsync(bool directoriesOnly, string? currentPath)
    {
        var dialog = new SelectPathDialog
        {
            DataContext = new SelectPathDialogViewModel(
                directoriesOnly,
                currentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        };

        if ((App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return null;

        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task StartCopyAsync(FolderFilePairViewModel pair)
    {
        if (!pair.CanStart)
        {
            pair.Status = "Percorsi non validi";
            pair.StateKind = CopyStateKind.Error;
            return;
        }

        var journalRecord = new CopyJobRecord
        {
            SourcePath = pair.SourcePath!,
            DestinationPath = pair.DestinationPath!,
            ExtraDestinations = pair.ExtraDestinations.Select(e => e.Path).ToList(),
            StartedUtc = DateTime.UtcNow
        };
        await CopyJournalStore.AddAsync(journalRecord);

        var cts = new CancellationTokenSource();
        _ctsByPair[pair] = cts;

        try
        {
            // Le cartelle che conterranno le destinazioni vengono create in ogni caso
            // (in background: su percorsi di rete può bloccare).
            await Task.Run(() =>
            {
                foreach (var destination in pair.AllDestinations)
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            });

            pair.IsCopying = true;
            pair.Progress = 0;
            pair.Status = "Copia in corso…";
            pair.StateKind = CopyStateKind.Copying;

            if (await FileSystemService.GetPathTypeAsync(pair.SourcePath) == PathType.Directory)
            {
                // La copia di cartelle verifica il checksum dell'intero albero (se abilitato).
                await CopyDirectoryAsync(pair, cts.Token);
                return;
            }

            await CopySingleFileAsync(pair, cts.Token);
        }
        catch (OperationCanceledException)
        {
            pair.Status = "Annullato";
            pair.StateKind = CopyStateKind.Cancelled;
        }
        catch (Exception ex)
        {
            pair.Status = $"Errore: {ex.Message}";
            pair.StateKind = CopyStateKind.Error;
        }
        finally
        {
            await CopyJournalStore.RemoveAsync(journalRecord.Id);

            pair.IsCopying = false;

            if (_ctsByPair.Remove(pair, out var toDispose))
                toDispose.Dispose();
        }
    }

    private void CancelCopy(FolderFilePairViewModel pair)
    {
        if (_ctsByPair.TryGetValue(pair, out var cts))
            cts.Cancel();
    }

    private static async Task CopySingleFileAsync(FolderFilePairViewModel pair, CancellationToken ct)
    {
        // Se la sorgente è un file e una destinazione è una cartella, il file viene copiato dentro la cartella.
        var destinationFiles = new List<string>();
        foreach (var destination in pair.AllDestinations)
        {
            bool intoFolder = await FileSystemService.GetPathTypeAsync(destination) == PathType.Directory;
            destinationFiles.Add(intoFolder
                ? Path.Combine(destination, Path.GetFileName(pair.SourcePath!))
                : destination);
        }

        long totalBytes = new FileInfo(pair.SourcePath!).Length;
        long copiedBytes = 0;

        await FileCopyService.CopyFileToManyAsync(pair.SourcePath!, destinationFiles, deltaBytes =>
        {
            copiedBytes += deltaBytes;
            pair.Progress = totalBytes > 0 ? (double)copiedBytes / totalBytes : 1;
        }, ct, AppSettingsStore.Current.BufferSizeBytes);

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        // Verifica checksum di tutte le destinazioni.
        pair.Status = "Verifica checksum…";
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);

        bool allMatch = true;
        foreach (var destinationFile in destinationFiles)
        {
            string destinationHash = await ChecksumService.ComputeSha256Async(destinationFile, ct);
            pair.DestinationChecksum = destinationHash;
            allMatch &= string.Equals(pair.SourceChecksum, destinationHash, StringComparison.OrdinalIgnoreCase);
        }

        pair.IsVerified = allMatch;
        pair.Progress = 1;
        pair.Status = allMatch ? "Completato" : "Completato (checksum non corrisponde)";
        pair.StateKind = allMatch ? CopyStateKind.Success : CopyStateKind.Warning;
    }

    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, CancellationToken ct)
    {
        int knownFileCount = -1;

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        int parallelism = int.MaxValue;
        foreach (var destination in pair.AllDestinations)
        {
            var destinationType = await DiskTypeService.GetDiskTypeAsync(destination, ct);
            parallelism = Math.Min(
                parallelism,
                CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType));
        }

        await FileCopyService.CopyDirectoryToManyAsync(
            pair.SourcePath!,
            pair.AllDestinations,
            maxDegreeOfParallelism: parallelism,
            onProgress: progress =>
            {
                if (knownFileCount != progress.TotalFiles)
                {
                    knownFileCount = progress.TotalFiles;
                    pair.Status = progress.TotalFiles == 0
                        ? "Nessun file da copiare"
                        : $"Copia cartella… ({progress.TotalFiles} file)";
                }

                pair.Progress = progress.Fraction;
            },
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: pair.SkipUnchanged);

        if (ct.IsCancellationRequested || knownFileCount == 0)
        {
            if (knownFileCount == 0)
                pair.StateKind = CopyStateKind.Ready;
            return;
        }

        if (!AppSettingsStore.Current.VerifyChecksumAfterCopy)
        {
            pair.Progress = 1;
            pair.Status = "Completato";
            pair.StateKind = CopyStateKind.Success;
            return;
        }

        pair.Status = "Verifica checksum…";
        int totalVerified = 0;
        int mismatchedTotal = 0;
        int missingTotal = 0;

        foreach (var destination in pair.AllDestinations)
        {
            var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
                pair.SourcePath!,
                destination,
                parallelism,
                progress => pair.Status = $"Verifica checksum… ({progress.VerifiedFiles}/{progress.TotalFiles})",
                ct);

            totalVerified = verifyResult.TotalFiles;
            mismatchedTotal += verifyResult.MismatchedFiles.Count;
            missingTotal += verifyResult.MissingFiles.Count;
        }

        pair.Progress = 1;
        pair.IsVerified = mismatchedTotal == 0 && missingTotal == 0;

        if (pair.IsVerified == true)
        {
            pair.Status = $"Completato e verificato ({totalVerified} file)";
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {mismatchedTotal} file diversi, {missingTotal} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
    }
}
