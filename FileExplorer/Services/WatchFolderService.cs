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
/// La sync è additiva: copia file nuovi o modificati e non cancella mai nulla dalla
/// destinazione, quindi i file rimossi dalla sorgente restano nella copia.
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

    /// <summary>
    /// Esegue subito una sync: tramite il runner se attivo (serializzata con quelle del loop),
    /// altrimenti one-shot. Il percorso one-shot non è serializzato con nulla: due chiamate
    /// concorrenti sulla stessa regola senza runner attivo possono sovrapporsi.
    /// </summary>
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
                    _watcher = CreateWatcher();
                    _ = Task.Run(() => LoopOnChangeAsync(_cts.Token));
                }
                else
                {
                    _ = Task.Run(() => LoopIntervalAsync(_cts.Token));
                }
            }
        }

        /// <summary>Crea e attiva un watcher sulla sorgente. Da chiamare sotto <see cref="_lifecycle"/>.</summary>
        private FileSystemWatcher CreateWatcher()
        {
            var watcher = new FileSystemWatcher(_rule.SourcePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += (_, _) => Signal();
            watcher.Changed += (_, _) => Signal();
            watcher.Renamed += (_, _) => Signal();
            watcher.Deleted += (_, _) => Signal();
            watcher.Error += (_, e) => OnWatcherError(e);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        /// <summary>
        /// Errore del watcher (tipicamente InternalBufferOverflowException con
        /// IncludeSubdirectories): gli eventi persi non tornano più. Si recupera
        /// segnalando comunque una sync — la passata incrementale confronta l'intero
        /// albero e riprende i cambi persi — e ricreando il watcher, che dopo un
        /// overflow può essere morto pur risultando attivo nella UI.
        /// </summary>
        private void OnWatcherError(ErrorEventArgs e)
        {
            RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, $"Errore watcher: {e.GetException().Message}"));
            Signal();

            // Fuori dal thread di callback del watcher: non si dispone un watcher
            // dall'interno di un suo stesso evento, né si blocca quel thread su _lifecycle.
            _ = Task.Run(RecreateWatcher);
        }

        private void RecreateWatcher()
        {
            lock (_lifecycle)
            {
                if (_disposed || _rule.Mode != WatchMode.OnChange)
                    return;

                try
                {
                    _watcher?.Dispose();
                    _watcher = CreateWatcher();
                }
                catch (Exception ex)
                {
                    _watcher = null;
                    RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, $"Watcher non ripristinato: {ex.Message}"));
                }
            }
        }

        /// <summary>Sync manuale, serializzata con quelle del loop tramite <see cref="_syncGate"/>.</summary>
        public Task RunOnceAsync() => RunSyncAsync(_cts.Token);

        /// <summary>
        /// Segnala un cambiamento. Il coalescing lo fa il semaforo stesso (capacità 1):
        /// se un segnale è già pendente, Release lancia e l'eccezione viene ignorata.
        /// Nessun flag affiancato al semaforo: due stati da tenere coerenti senza un
        /// lock comune si disallineerebbero, lasciando il runner sordo per sempre.
        /// </summary>
        private void Signal()
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // segnale già pendente
            }
        }

        private async Task LoopOnChangeAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    // Primo evento della raffica: attesa senza timeout.
                    await _wake.WaitAsync(ct).ConfigureAwait(false);

                    // Debounce: ogni segnale consumato riapre la finestra di quiete.
                    // Esce quando per DebounceDelay non arriva più nulla.
                    while (await _wake.WaitAsync(DebounceDelay, ct).ConfigureAwait(false))
                    {
                        // raffica ancora in corso
                    }

                    await RunSyncAsync(ct).ConfigureAwait(false);
                    // Un evento arrivato durante la sync ha lasciato un segnale pendente:
                    // il giro successivo lo consuma subito e riesegue.
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
            FileSystemWatcher? watcher;
            lock (_lifecycle)
            {
                if (_disposed)
                    return;

                _disposed = true;
                watcher = _watcher;
                _watcher = null;
            }

            // Fuori dal lock: Dispose e Cancel eseguono callback esterni (handler del
            // watcher, continuation dei loop), che non devono mai girare sotto _lifecycle.
            // Il flag _disposed è già alzato, quindi Start/RecreateWatcher concorrenti
            // escono senza creare nulla.
            watcher?.Dispose();
            _cts.Cancel();

            // Il CTS non viene disposto qui: loop e sync in volo potrebbero ancora
            // osservare il token. Cancellato resta innocuo; lo raccoglie il GC.
        }
    }
}
