using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>Stato di una regola watch-folder, notificato via <see cref="WatchFolderService.StatusChanged"/>.</summary>
public sealed record WatchStatus(string RuleId, bool IsRunning, DateTime? LastRunUtc, string Message);

/// <summary>
/// Motore delle regole watch-folder: un runner per regola attiva.
/// OnChange: FileSystemWatcher + debounce con coalescing (una sola sync per raffica
/// di eventi; eventi arrivati durante una sync ne accodano una successiva).
/// Interval: sync ogni <see cref="WatchRule.IntervalMinutes"/> minuti.
/// La sync è <see cref="FileCopyService.CopyDirectoryAsync"/> con skipUnchanged=true
/// (incrementale). Non usa CopyJournalStore: una sync interrotta a metà viene
/// completata dalla successiva grazie al confronto dimensione+mtime.
/// </summary>
public static class WatchFolderService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RuleRunner> Runners = new();

    /// <summary>
    /// Notifica di stato. Invocato su thread di background: i ViewModel assegnano
    /// proprietà reactive direttamente, come per i callback di progresso della copia.
    /// </summary>
    public static event Action<WatchStatus>? StatusChanged;

    /// <summary>Finestra di quiete dopo l'ultimo evento prima di sincronizzare. Ridotta nei test.</summary>
    internal static TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Override dell'intervallo (test). Default: IntervalMinutes della regola.</summary>
    internal static Func<WatchRule, TimeSpan>? IntervalOverride { get; set; }

    /// <summary>Override della sync (test). Default: <see cref="DefaultSyncAsync"/>.</summary>
    internal static Func<WatchRule, CancellationToken, Task>? SyncOverride { get; set; }

    /// <summary>Id delle regole con runner attivo.</summary>
    public static IReadOnlyCollection<string> ActiveRuleIds
    {
        get
        {
            lock (Gate)
                return Runners.Keys.ToList();
        }
    }

    /// <summary>
    /// Avvia (o riavvia) il runner della regola. Idempotente per Id.
    /// Limite dichiarato: se la sorgente non esiste il runner non parte
    /// (nessun retry automatico); viene emesso uno stato di errore.
    /// </summary>
    public static void Start(WatchRule rule)
    {
        Stop(rule.Id);

        if (!Directory.Exists(rule.SourcePath))
        {
            RaiseStatus(new WatchStatus(rule.Id, false, null, $"Sorgente non trovata: {rule.SourcePath}"));
            return;
        }

        var runner = new RuleRunner(rule);
        lock (Gate)
            Runners[rule.Id] = runner;
        runner.Start();
    }

    /// <summary>Ferma il runner della regola (no-op se assente).</summary>
    public static void Stop(string ruleId)
    {
        RuleRunner? runner;
        lock (Gate)
            Runners.Remove(ruleId, out runner);
        runner?.Dispose();
    }

    /// <summary>Ferma tutti i runner (test e chiusure future).</summary>
    public static void StopAll()
    {
        List<RuleRunner> toStop;
        lock (Gate)
        {
            toStop = Runners.Values.ToList();
            Runners.Clear();
        }

        foreach (RuleRunner runner in toStop)
            runner.Dispose();
    }

    /// <summary>Esegue subito una sync: tramite il runner se attivo (serializzata), altrimenti one-shot.</summary>
    public static async Task RunNowAsync(WatchRule rule)
    {
        RuleRunner? runner;
        lock (Gate)
            Runners.TryGetValue(rule.Id, out runner);

        if (runner is not null)
        {
            await runner.RunOnceAsync().ConfigureAwait(false);
            return;
        }

        await SyncWithStatusAsync(rule, lastRunUtc: null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Sync reale: copia incrementale directory → directory con parallelismo adattivo.</summary>
    internal static async Task DefaultSyncAsync(WatchRule rule, CancellationToken ct)
    {
        DiskType sourceType = await DiskTypeService.GetDiskTypeAsync(rule.SourcePath, ct).ConfigureAwait(false);
        DiskType destinationType = await DiskTypeService.GetDiskTypeAsync(rule.DestinationPath, ct).ConfigureAwait(false);
        int parallelism = CopyParallelismResolver.Resolve(AppSettingsStore.Current, sourceType, destinationType);

        await FileCopyService.CopyDirectoryAsync(
            rule.SourcePath,
            rule.DestinationPath,
            parallelism,
            onProgress: null,
            ct,
            bufferSize: AppSettingsStore.Current.BufferSizeBytes,
            skipUnchanged: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Emette un cambio di stato (interno: usato anche dai test dei ViewModel).
    /// Le eccezioni dei sottoscrittori vengono ingoiate: una notifica è un effetto
    /// collaterale e non deve mai uccidere il loop del runner che l'ha emessa.
    /// </summary>
    internal static void RaiseStatus(WatchStatus status)
    {
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception)
        {
            // notifica best-effort
        }
    }

    /// <summary>
    /// Esegue una sync emettendo gli stati prima/dopo. Ritorna il nuovo LastRunUtc.
    /// Le eccezioni (tranne la cancellazione) diventano uno stato di errore: mai
    /// propagate fuori dai loop dei runner.
    /// </summary>
    private static async Task<DateTime?> SyncWithStatusAsync(WatchRule rule, DateTime? lastRunUtc, CancellationToken ct)
    {
        RaiseStatus(new WatchStatus(rule.Id, true, lastRunUtc, "Sincronizzazione…"));
        try
        {
            Func<WatchRule, CancellationToken, Task> sync = SyncOverride ?? DefaultSyncAsync;
            await sync(rule, ct).ConfigureAwait(false);
            DateTime completed = DateTime.UtcNow;
            RaiseStatus(new WatchStatus(rule.Id, false, completed, $"Completata alle {completed.ToLocalTime():HH:mm:ss}"));
            return completed;
        }
        catch (OperationCanceledException)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, "Interrotta"));
            throw;
        }
        catch (Exception ex)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, $"Errore: {ex.Message}"));
            return lastRunUtc;
        }
    }

    /// <summary>Runner di una singola regola: watcher/loop dedicati e CTS proprio.</summary>
    private sealed class RuleRunner : IDisposable
    {
        private readonly WatchRule _rule;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _wake = new(0, 1);
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        /// <summary>Serializza Start/Dispose: senza, uno Stop concorrente a Start lascerebbe il watcher orfano.</summary>
        private readonly object _lifecycle = new();

        /// <summary>Protegge <see cref="_lastRunUtc"/>: DateTime? non ha letture atomiche.</summary>
        private readonly object _stateGate = new();

        private FileSystemWatcher? _watcher;
        private bool _disposed;
        private int _dirty;
        private DateTime? _lastRunUtc;

        public RuleRunner(WatchRule rule) => _rule = rule;

        private DateTime? LastRunUtc
        {
            get { lock (_stateGate) return _lastRunUtc; }
            set { lock (_stateGate) _lastRunUtc = value; }
        }

        public void Start()
        {
            lock (_lifecycle)
            {
                if (_disposed)
                    return;

                if (_rule.Mode == WatchMode.OnChange)
                {
                    _watcher = new FileSystemWatcher(_rule.SourcePath)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                     | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    _watcher.Created += (_, _) => Signal();
                    _watcher.Changed += (_, _) => Signal();
                    _watcher.Renamed += (_, _) => Signal();
                    _watcher.Deleted += (_, _) => Signal();
                    _watcher.Error += (_, e) =>
                        RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, $"Errore watcher: {e.GetException().Message}"));
                    _watcher.EnableRaisingEvents = true;

                    _ = Task.Run(() => LoopOnChangeAsync(_cts.Token));
                }
                else
                {
                    _ = Task.Run(() => LoopIntervalAsync(_cts.Token));
                }
            }
        }

        /// <summary>Sync manuale, serializzata con quelle del loop tramite <see cref="_syncGate"/>.</summary>
        public Task RunOnceAsync() => RunSyncAsync(_cts.Token);

        private void Signal()
        {
            // Coalescing: un solo release pendente per qualsiasi numero di eventi.
            if (Interlocked.Exchange(ref _dirty, 1) == 0)
            {
                try
                {
                    _wake.Release();
                }
                catch (SemaphoreFullException)
                {
                    // già segnalato
                }
            }
        }

        private async Task LoopOnChangeAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await _wake.WaitAsync(ct).ConfigureAwait(false);

                    // Debounce: attende una finestra di quiete coalescendo gli eventi.
                    do
                    {
                        Interlocked.Exchange(ref _dirty, 0);
                        await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
                    }
                    while (Volatile.Read(ref _dirty) == 1);

                    // Consuma l'eventuale release residuo maturato durante il debounce.
                    while (_wake.CurrentCount > 0)
                        await _wake.WaitAsync(ct).ConfigureAwait(false);

                    await RunSyncAsync(ct).ConfigureAwait(false);
                    // Eventi arrivati durante la sync hanno rimesso _dirty/_wake:
                    // il giro successivo riparte da WaitAsync e riesegue.
                }
            }
            catch (OperationCanceledException)
            {
                // stop richiesto
            }
        }

        private async Task LoopIntervalAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    TimeSpan interval = IntervalOverride?.Invoke(_rule)
                                        ?? TimeSpan.FromMinutes(Math.Clamp(
                                            _rule.IntervalMinutes,
                                            WatchRuleStore.MinIntervalMinutes,
                                            WatchRuleStore.MaxIntervalMinutes));
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    await RunSyncAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // stop richiesto
            }
        }

        private async Task RunSyncAsync(CancellationToken ct)
        {
            await _syncGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                LastRunUtc = await SyncWithStatusAsync(_rule, LastRunUtc, ct).ConfigureAwait(false);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        public void Dispose()
        {
            lock (_lifecycle)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _cts.Cancel();
                _watcher?.Dispose();
                _watcher = null;
            }

            // Il CTS non viene disposto qui: loop e sync in volo potrebbero ancora
            // osservare il token. Cancellato resta innocuo; lo raccoglie il GC.
        }
    }
}
