using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

    public CopyPairsViewModel()
    {
        PathPairs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasPairs));

        AddPairCommand = ReactiveCommand.Create(() => PathPairs.Add(new FolderFilePairViewModel()));

        BrowseSourceCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(BrowseDestinationAsync);

        // Start/Cancel: la validazione CanStart è valutata sulla singola riga via binding (IsEnabled).
        StartCopyCommand = ReactiveCommand.CreateFromTask<FolderFilePairViewModel>(StartCopyAsync);
        CancelCopyCommand = ReactiveCommand.Create<FolderFilePairViewModel>(CancelCopy);
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

        var cts = new CancellationTokenSource();
        _ctsByPair[pair] = cts;

        try
        {
            // La cartella che conterrà la destinazione viene creata in ogni caso
            // (in background: su percorsi di rete può bloccare).
            await Task.Run(() => Directory.CreateDirectory(Path.GetDirectoryName(pair.DestinationPath!)!));

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
        // Se la sorgente è un file e la destinazione una cartella, il file viene copiato dentro la cartella.
        bool isFileCopyToFolder = await FileSystemService.GetPathTypeAsync(pair.DestinationPath) == PathType.Directory;
        string destinationPath = isFileCopyToFolder
            ? Path.Combine(pair.DestinationPath!, Path.GetFileName(pair.SourcePath!))
            : pair.DestinationPath!;

        long totalBytes = new FileInfo(pair.SourcePath!).Length;
        long copiedBytes = 0;

        await FileCopyService.CopyFileAsync(pair.SourcePath!, destinationPath, deltaBytes =>
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

        // Verifica checksum dopo la copia.
        pair.Status = "Verifica checksum…";
        pair.SourceChecksum ??= await ChecksumService.ComputeSha256Async(pair.SourcePath!, ct);
        pair.DestinationChecksum = await ChecksumService.ComputeSha256Async(destinationPath, ct);
        pair.IsVerified = string.Equals(pair.SourceChecksum, pair.DestinationChecksum, StringComparison.OrdinalIgnoreCase);

        pair.Progress = 1;
        pair.Status = pair.IsVerified == true ? "Completato" : "Completato (checksum non corrisponde)";
        pair.StateKind = pair.IsVerified == true ? CopyStateKind.Success : CopyStateKind.Warning;
    }

    private static async Task CopyDirectoryAsync(FolderFilePairViewModel pair, CancellationToken ct)
    {
        int knownFileCount = -1;

        var sourceType = await DiskTypeService.GetDiskTypeAsync(pair.SourcePath, ct);
        var destinationType = await DiskTypeService.GetDiskTypeAsync(pair.DestinationPath, ct);
        int parallelism = CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType);

        await FileCopyService.CopyDirectoryAsync(
            pair.SourcePath!,
            pair.DestinationPath!,
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
            bufferSize: AppSettingsStore.Current.BufferSizeBytes);

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
        var verifyResult = await DirectoryVerificationService.VerifyDirectoryAsync(
            pair.SourcePath!,
            pair.DestinationPath!,
            parallelism,
            progress => pair.Status = $"Verifica checksum… ({progress.VerifiedFiles}/{progress.TotalFiles})",
            ct);

        pair.Progress = 1;
        pair.IsVerified = verifyResult.IsSuccess;

        if (verifyResult.IsSuccess)
        {
            pair.Status = $"Completato e verificato ({verifyResult.TotalFiles} file)";
            pair.StateKind = CopyStateKind.Success;
        }
        else
        {
            pair.Status = $"Verifica fallita: {verifyResult.MismatchedFiles.Count} file diversi, {verifyResult.MissingFiles.Count} mancanti";
            pair.StateKind = CopyStateKind.Warning;
        }
    }
}
