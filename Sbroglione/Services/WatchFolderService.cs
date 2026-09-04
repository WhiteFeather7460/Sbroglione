using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Stato di una regola watch-folder, notificato via <see cref="WatchFolderService.StatusChanged"/>.
/// <paramref name="MessageKind"/> è un identificatore stabile e indipendente dalla lingua
/// (uno dei const <c>Status*</c> di <see cref="WatchFolderService"/>); <paramref name="MessageDetail"/>
/// porta l'eventuale dato dinamico (percorso, messaggio d'eccezione). La traduzione avviene
/// al confine ViewModel — vedi il commento su <see cref="WatchFolderService.StatusSyncing"/>.
/// </summary>
public sealed record WatchStatus(string RuleId, bool IsRunning, DateTime? LastRunUtc, string MessageKind, string? MessageDetail = null);

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
    /// Un lock per Id di regola: rende atomica l'intera sequenza di <see cref="Start"/>
    /// (stop → controlli → registrazione → avvio) rispetto a qualunque altro Start/Stop
    /// sulla stessa regola. Il solo <see cref="Gate"/> proteggeva il dizionario, non la
    /// sequenza: due Start ravvicinati (due OnRuleChanged, oppure la UI contro l'avvio
    /// iniziale di App) potevano interlacciarsi e il più lento nei controlli (es.
    /// Directory.Exists su una share lenta) registrava il proprio runner sopra quello
    /// dell'altro — che restava vivo ma non più fermabile: due runner sulla stessa regola
    /// e uno zombie che continua a copiare anche dopo averla disabilitata.
    /// I lock non vengono mai rimossi: toglierli in Stop mentre uno Start li tiene
    /// occupati farebbe creare un oggetto diverso al chiamante successivo, annullando la
    /// mutua esclusione. Sono uno per Id di regola vista nella sessione: quantità
    /// trascurabile.
    /// </summary>
    private static readonly Dictionary<string, object> RuleGates = new();

    /// <summary>
    /// Identificatori di <see cref="WatchStatus.MessageKind"/>, stabili e indipendenti dalla
    /// lingua: la traduzione (e l'eventuale <see cref="string.Format(string, object?)"/> con
    /// <see cref="WatchStatus.MessageDetail"/>) avviene al confine ViewModel, mai qui.
    /// </summary>
    internal const string StatusSyncing = "Syncing";
    internal const string StatusCompleted = "Completed";
    internal const string StatusInterrupted = "Interrupted";
    internal const string StatusError = "Error";
    internal const string StatusSourceNotFound = "SourceNotFound";
    internal const string StatusStartFailed = "StartFailed";
    internal const string StatusSelfFeeding = "SelfFeeding";
    internal const string StatusDestinationNotFound = "DestinationNotFound";
    internal const string StatusWatcherError = "WatcherError";
    internal const string StatusWatcherNotRestored = "WatcherNotRestored";

    /// <summary>
    /// Notifica di stato. Invocato su thread di background: i ViewModel assegnano
    /// proprietà reactive direttamente, come per i callback di progresso della copia.
    /// </summary>
    public static event Action<WatchStatus>? StatusChanged;

    /// <summary>Finestra di quiete dopo l'ultimo evento prima di sincronizzare. Ridotta nei test.</summary>
    internal static TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Tetto complessivo del debounce: su una cartella sempre in movimento la finestra
    /// di quiete non scadrebbe mai (starvation), quindi si sincronizza comunque una
    /// volta trascorso questo tempo dal primo segnale della raffica. Ridotto nei test.
    /// </summary>
    internal static TimeSpan MaxDebounceWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Factory del FileSystemWatcher (test): permette di simulare un avvio fallito.</summary>
    internal static Func<string, FileSystemWatcher>? WatcherFactory { get; set; }

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
    /// Avvia i runner di tutte le regole abilitate. Punto d'ingresso unico condiviso tra
    /// l'avvio desktop (<c>App.OnFrameworkInitializationCompleted</c>) e l'host di background
    /// Android (<c>WatchFolderForegroundService</c>): senza, le due piattaforme avrebbero due
    /// copie dello stesso loop, libere di divergere.
    /// Non lancia mai: una singola regola malata non deve impedire l'avvio delle altre.
    /// </summary>
    /// <param name="rules">
    /// Regole da considerare; se <c>null</c> vengono caricate da <see cref="WatchRuleStore.Load"/>.
    /// </param>
    /// <returns>
    /// Numero di regole abilitate per cui <see cref="Start"/> è stato invocato senza eccezioni.
    /// Non è il numero di runner effettivamente attivi: <see cref="Start"/> può non registrare
    /// alcun runner (sorgente assente, regola autoalimentante) segnalandolo solo via
    /// <see cref="StatusChanged"/>.
    /// </returns>
    public static int StartAllEnabledRules(IEnumerable<WatchRule>? rules = null)
    {
        int started = 0;
        foreach (WatchRule rule in rules ?? WatchRuleStore.Load())
        {
            if (!rule.Enabled)
                continue;
            try
            {
                Start(rule);
                started++;
            }
            catch (Exception)
            {
                // Difesa in profondità: Start non lancia più, ma una singola regola
                // malata non deve fermare le altre.
            }
        }

        return started;
    }

    /// <summary>
    /// Avvia (o riavvia) il runner della regola. Idempotente per Id. Non lancia mai:
    /// ogni fallimento (sorgente assente, regola autoalimentante, watcher non attivabile)
    /// diventa uno stato di errore.
    /// Limite dichiarato: se la sorgente non esiste il runner non parte
    /// (nessun retry automatico).
    /// </summary>
    public static void Start(WatchRule rule)
    {
        // Tutta la sequenza sotto il lock della regola: lo Stop iniziale è rientrante
        // (stesso thread, stesso oggetto), quindi non serve una variante "senza lock".
        lock (RuleGateFor(rule.Id))
        {
            Stop(rule.Id);

            if (IsDestinationInsideSource(rule.SourcePath, rule.DestinationPath))
            {
                RaiseStatus(new WatchStatus(rule.Id, false, null, StatusSelfFeeding, rule.DestinationPath));
                return;
            }

            if (!Directory.Exists(rule.SourcePath))
            {
                RaiseStatus(new WatchStatus(rule.Id, false, null, StatusSourceNotFound, rule.SourcePath));
                return;
            }

            var runner = new RuleRunner(rule);
            lock (Gate)
                Runners[rule.Id] = runner;

            try
            {
                runner.Start();
            }
            catch (Exception ex)
            {
                // EnableRaisingEvents può fallire (limiti inotify, percorso ostile): il runner
                // registrato resterebbe uno zombie sordo, mostrato come attivo nella UI.
                lock (Gate)
                {
                    if (Runners.TryGetValue(rule.Id, out RuleRunner? registered) && ReferenceEquals(registered, runner))
                        Runners.Remove(rule.Id);
                }

                runner.Dispose();
                RaiseStatus(new WatchStatus(rule.Id, false, null, StatusStartFailed, ex.Message));
            }
        }
    }

    /// <summary>Lock dedicato alla regola, creato al primo uso. Vedi <see cref="RuleGates"/>.</summary>
    private static object RuleGateFor(string ruleId)
    {
        lock (Gate)
        {
            if (!RuleGates.TryGetValue(ruleId, out object? gate))
            {
                gate = new object();
                RuleGates[ruleId] = gate;
            }

            return gate;
        }
    }

    /// <summary>
    /// True se la destinazione coincide con la sorgente o è contenuta in essa: la copia
    /// finirebbe dentro l'albero osservato, rialimentando il watcher a ogni passata
    /// (ricorsione che cresce fino a riempire il disco).
    /// Confronto case-insensitive su Windows/macOS, byte-exact altrove (come
    /// <see cref="DirectoryComparisonService.DefaultPathComparer"/>).
    /// </summary>
    internal static bool IsDestinationInsideSource(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return false;

        try
        {
            string source = WithTrailingSeparator(Path.GetFullPath(sourcePath));
            string destination = WithTrailingSeparator(Path.GetFullPath(destinationPath));
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return destination.StartsWith(source, comparison);
        }
        catch (Exception)
        {
            // Percorso non normalizzabile: non è questo il posto dove segnalarlo,
            // ci pensa il controllo di esistenza o la sync stessa.
            return false;
        }
    }

    /// <summary>Aggiunge il separatore finale: senza, "/a/bc" risulterebbe dentro "/a/b".</summary>
    private static string WithTrailingSeparator(string fullPath) =>
        fullPath.EndsWith(Path.DirectorySeparatorChar) ? fullPath : fullPath + Path.DirectorySeparatorChar;

    /// <summary>
    /// Ferma il runner della regola (no-op se assente). Anche lo stop passa dal lock della
    /// regola: eseguito a metà di uno <see cref="Start"/> concorrente non troverebbe ancora
    /// nulla da rimuovere e lascerebbe vivo il runner registrato subito dopo (zombie a
    /// regola disabilitata).
    /// </summary>
    public static void Stop(string ruleId)
    {
        lock (RuleGateFor(ruleId))
        {
            RuleRunner? runner;
            lock (Gate)
                Runners.Remove(ruleId, out runner);
            runner?.Dispose();
        }
    }

    /// <summary>Ferma tutti i runner (test e chiusure future).</summary>
    public static void StopAll()
    {
        // Per Id, così ogni stop è serializzato con un eventuale Start in volo sulla stessa
        // regola: si passa dagli Id di tutte le regole viste, non solo da quelle con un
        // runner registrato adesso.
        List<string> ids;
        lock (Gate)
            ids = Runners.Keys.Union(RuleGates.Keys, StringComparer.Ordinal).ToList();

        foreach (string ruleId in ids)
            Stop(ruleId);
    }

    /// <summary>
    /// Esegue subito una sync: tramite il runner se attivo (serializzata con quelle del loop),
    /// altrimenti one-shot. Il percorso one-shot non è serializzato con nulla: due chiamate
    /// concorrenti sulla stessa regola senza runner attivo possono sovrapporsi.
    /// </summary>
    public static async Task RunNowAsync(WatchRule rule)
    {
        if (IsDestinationInsideSource(rule.SourcePath, rule.DestinationPath))
        {
            RaiseStatus(new WatchStatus(rule.Id, false, null, StatusSelfFeeding, rule.DestinationPath));
            return;
        }

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
        RaiseStatus(new WatchStatus(rule.Id, true, lastRunUtc, StatusSyncing));
        try
        {
            Func<WatchRule, CancellationToken, Task> sync = SyncOverride ?? DefaultSyncAsync;
            await sync(rule, ct).ConfigureAwait(false);
            DateTime completed = DateTime.UtcNow;
            RaiseStatus(new WatchStatus(rule.Id, false, completed, StatusCompleted));
            return completed;
        }
        catch (OperationCanceledException)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, StatusInterrupted));
            throw;
        }
        catch (DirectoryNotFoundException ex)
        {
            // Message porta solo il percorso (vedi DefaultSyncAsync): nessun testo italiano
            // hardcoded da propagare, la traduzione avviene al confine ViewModel.
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, StatusDestinationNotFound, ex.Message));
            return lastRunUtc;
        }
        catch (Exception ex)
        {
            RaiseStatus(new WatchStatus(rule.Id, false, lastRunUtc, StatusError, ex.Message));
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

        /// <summary>
        /// Crea e attiva un watcher sulla sorgente. Da chiamare sotto <see cref="_lifecycle"/>.
        /// Può lanciare (percorso non valido, limiti del sistema): il chiamante decide.
        /// </summary>
        private FileSystemWatcher CreateWatcher()
        {
            FileSystemWatcher watcher = WatcherFactory?.Invoke(_rule.SourcePath)
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
            RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, StatusWatcherError, e.GetException().Message));
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
                    RaiseStatus(new WatchStatus(_rule.Id, false, LastRunUtc, StatusWatcherNotRestored, ex.Message));
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
        /// Nessun flag affiancato al semaforo: due stati da tenere coerenti senza un
        /// lock comune si disallineerebbero, lasciando il runner sordo per sempre.
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
                    long windowMs = (long)MaxDebounceWindow.TotalMilliseconds;
                    while (true)
                    {
                        long remainingMs = windowMs - (Environment.TickCount64 - windowStart);
                        if (remainingMs <= 0)
                            break;

                        TimeSpan wait = TimeSpan.FromMilliseconds(Math.Min(DebounceDelay.TotalMilliseconds, remainingMs));
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
