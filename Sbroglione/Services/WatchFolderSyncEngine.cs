using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Esecuzione di una singola sync: motore reale più emissione degli stati prima/dopo.
/// Usato dal loop di <see cref="RuleRunner"/> e dal percorso one-shot di
/// <see cref="WatchFolderService.RunNowAsync"/>.
/// </summary>
internal static class WatchFolderSyncEngine
{
    /// <summary>Sync reale: copia incrementale directory → directory con parallelismo adattivo.</summary>
    internal static async Task DefaultSyncAsync(WatchRule rule, CancellationToken ct)
    {
        // La destinazione deve esistere già: se il disco di backup è smontato, ricrearla
        // riempirebbe il mount point locale invece del volume previsto. L'eccezione
        // diventa uno stato di errore e la passata viene saltata: il segnale (o il tick)
        // successivo riprova.
        if (!Directory.Exists(rule.DestinationPath))
            throw new DirectoryNotFoundException(rule.DestinationPath);

        // Snapshot: l'utente può cambiare le impostazioni mentre la sync è in corso.
        AppSettings settings = AppSettingsStore.Current;

        DiskType sourceType = await DiskTypeService.GetDiskTypeAsync(rule.SourcePath, ct).ConfigureAwait(false);
        DiskType destinationType = await DiskTypeService.GetDiskTypeAsync(rule.DestinationPath, ct).ConfigureAwait(false);
        int parallelism = CopyParallelismResolver.Resolve(settings, sourceType, destinationType);

        await FileCopyService.CopyDirectoryAsync(
            rule.SourcePath,
            rule.DestinationPath,
            parallelism,
            onProgress: null,
            ct,
            bufferSize: settings.BufferSizeBytes,
            skipUnchanged: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Esegue una sync emettendo gli stati prima/dopo. Ritorna il nuovo LastRunUtc.
    /// Le eccezioni (tranne la cancellazione) diventano uno stato di errore: mai
    /// propagate fuori dai loop dei runner.
    /// </summary>
    internal static async Task<DateTime?> SyncWithStatusAsync(WatchRule rule, DateTime? lastRunUtc, CancellationToken ct)
    {
        WatchFolderService.RaiseStatus(new WatchStatus(rule.Id, true, lastRunUtc, WatchFolderService.StatusSyncing));
        try
        {
            Func<WatchRule, CancellationToken, Task> sync = WatchFolderService.SyncOverride ?? DefaultSyncAsync;
            await sync(rule, ct).ConfigureAwait(false);
            DateTime completed = DateTime.UtcNow;
            WatchFolderService.RaiseStatus(new WatchStatus(rule.Id, false, completed, WatchFolderService.StatusCompleted));
            return completed;
        }
        catch (OperationCanceledException)
        {
            WatchFolderService.RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, WatchFolderService.StatusInterrupted));
            throw;
        }
        catch (DirectoryNotFoundException ex)
        {
            // Message porta solo il percorso (vedi DefaultSyncAsync): nessun testo italiano
            // hardcoded da propagare, la traduzione avviene al confine ViewModel.
            WatchFolderService.RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, WatchFolderService.StatusDestinationNotFound, ex.Message));
            return lastRunUtc;
        }
        catch (Exception ex)
        {
            WatchFolderService.RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, WatchFolderService.StatusError, ex.Message));
            return lastRunUtc;
        }
    }
}
