using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Runner di una singola regola: watcher/loop dedicati e CTS proprio.
/// OnChange: FileSystemWatcher + debounce con coalescing (una sola sync per raffica
/// di eventi; eventi arrivati durante una sync ne accodano una successiva).
/// Interval: sync ogni <see cref="WatchRule.IntervalMinutes"/> minuti.
/// </summary>
internal sealed class RuleRunner : IDisposable
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

    /// <summary>
    /// Crea e attiva un watcher sulla sorgente. Da chiamare sotto <see cref="_lifecycle"/>.
    /// Può lanciare (percorso non valido, limiti del sistema): il chiamante decide.
    /// </summary>
    private FileSystemWatcher CreateWatcher()
    {
        FileSystemWatcher watcher = WatchFolderService.WatcherFactory?.Invoke(_rule.SourcePath)
                                    ?? new FileSystemWatcher(_rule.SourcePath);
        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite | NotifyFilters.Size;

        // Buffer interno più ampio del default (8 KB): con IncludeSubdirectories le
        // raffiche grosse lo saturano facilmente e ogni overflow perde eventi.
        watcher.InternalBufferSize = 65536;
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
        WatchFolderService.RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, WatchFolderService.StatusWatcherError, e.GetException().Message));
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
                WatchFolderService.RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, WatchFolderService.StatusWatcherNotRestored, ex.Message));
            }
        }
    }

    /// <summary>Sync manuale, serializzata con quelle del loop tramite <see cref="_syncGate"/>.</summary>
    public Task RunOnceAsync() => RunSyncAsync(_cts.Token);

    /// <summary>
    /// Segnala un cambiamento. Il coalescing lo fa il semaforo stesso (capacità 1):
    /// il caso comune "segnale già pendente" si riconosce da CurrentCount, ma il
    /// controllo non è atomico rispetto al Release, quindi la SemaphoreFullException
    /// resta come rete di sicurezza per le raffiche concorrenti.
    /// </summary>
    private void Signal()
    {
        if (_wake.CurrentCount > 0)
            return;

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

                // Debounce: ogni segnale consumato riapre la finestra di quiete, ma
                // l'attesa complessiva è limitata a MaxDebounceWindow dal primo
                // segnale. Senza il tetto una cartella sempre in movimento non
                // verrebbe mai sincronizzata.
                long windowStart = Environment.TickCount64;
                long windowMs = (long)WatchFolderService.MaxDebounceWindow.TotalMilliseconds;
                while (true)
                {
                    long remainingMs = windowMs - (Environment.TickCount64 - windowStart);
                    if (remainingMs <= 0)
                        break;

                    TimeSpan wait = TimeSpan.FromMilliseconds(Math.Min(WatchFolderService.DebounceDelay.TotalMilliseconds, remainingMs));
                    if (!await _wake.WaitAsync(wait, ct).ConfigureAwait(false))
                        break; // quiete raggiunta (o finestra esaurita)
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
                TimeSpan interval = WatchFolderService.IntervalOverride?.Invoke(_rule)
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
            LastRunUtc = await WatchFolderSyncEngine.SyncWithStatusAsync(_rule, LastRunUtc, ct).ConfigureAwait(false);
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
