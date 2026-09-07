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
/// al confine ViewModel.
/// </summary>
public sealed record WatchStatus(string RuleId, bool IsRunning, DateTime? LastRunUtc, string MessageKind, string? MessageDetail = null);

/// <summary>
/// Registro delle regole watch-folder attive: un <see cref="RuleRunner"/> per regola,
/// avviato/fermato in modo atomico per Id. L'esecuzione del loop/watcher vive in
/// <see cref="RuleRunner"/>, quella della sync in <see cref="WatchFolderSyncEngine"/>;
/// questa classe resta l'unica API pubblica (Start/Stop/RunNow/StartAllEnabledRules).
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
    /// e uno zombie che continua a copiare anche dopo averla disabilitata. I lock non
    /// vengono mai rimossi: toglierli in Stop mentre uno Start li tiene occupati farebbe
    /// creare un oggetto diverso al chiamante successivo, annullando la mutua esclusione.
    /// Sono uno per Id di regola vista nella sessione: quantità trascurabile.
    /// </summary>
    private static readonly Dictionary<string, object> RuleGates = new();

    /// <summary>
    /// Identificatori di <see cref="WatchStatus.MessageKind"/>, stabili e indipendenti dalla
    /// lingua: la traduzione avviene al confine ViewModel, mai qui.
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

    /// <summary>Override della sync (test). Default: <see cref="WatchFolderSyncEngine.DefaultSyncAsync"/>.</summary>
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
    /// l'avvio desktop e l'host di background Android. Non lancia mai: una singola regola
    /// malata non deve impedire l'avvio delle altre.
    /// </summary>
    /// <returns>
    /// Numero di regole abilitate per cui <see cref="Start"/> è stato invocato senza
    /// eccezioni. Non è il numero di runner effettivamente attivi: <see cref="Start"/>
    /// può non registrare alcun runner (sorgente assente, regola autoalimentante)
    /// segnalandolo solo via <see cref="StatusChanged"/>.
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
    /// </summary>
    public static void Start(WatchRule rule)
    {
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

    /// <summary>Lock dedicato alla regola, creato al primo uso.</summary>
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
    /// finirebbe dentro l'albero osservato, rialimentando il watcher a ogni passata.
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
            return false;
        }
    }

    /// <summary>Aggiunge il separatore finale: senza, "/a/bc" risulterebbe dentro "/a/b".</summary>
    private static string WithTrailingSeparator(string fullPath) =>
        fullPath.EndsWith(Path.DirectorySeparatorChar) ? fullPath : fullPath + Path.DirectorySeparatorChar;

    /// <summary>Ferma il runner della regola (no-op se assente).</summary>
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

        await WatchFolderSyncEngine.SyncWithStatusAsync(rule, lastRunUtc: null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Emette un cambio di stato (interno: usato anche dai test dei ViewModel e da
    /// <see cref="RuleRunner"/>/<see cref="WatchFolderSyncEngine"/>). Le eccezioni dei
    /// sottoscrittori vengono ingoiate: una notifica è un effetto collaterale e non deve
    /// mai uccidere il loop del runner che l'ha emessa.
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
}
