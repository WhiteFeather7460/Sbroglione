using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

using Sbroglione.Models;
using Sbroglione.Services;

using ReactiveUI;

using App = Sbroglione.App;

namespace Sbroglione.ViewModels;

/// <summary>
/// Scheda "Sync auto": gestione delle regole watch-folder. Persiste su
/// <see cref="WatchRuleStore"/> a ogni modifica e riallinea i runner di
/// <see cref="WatchFolderService"/>. I runner iniziali sono avviati da App.
/// </summary>
public class WatchFoldersViewModel : ViewModelBase, IDisposable
{
    private readonly Action<WatchStatus> _statusHandler;

    /// <summary>
    /// Indice per RuleId usato da <see cref="OnStatusChanged"/>, che gira su thread di
    /// background: <see cref="Rules"/> (ObservableCollection, non thread-safe) è mutata
    /// solo dal thread UI in Add/Remove/Load, quindi enumerarla da un altro thread
    /// (FirstOrDefault) potrebbe incappare in un InvalidOperationException a metà
    /// enumerazione. ConcurrentDictionary tiene la lookup thread-safe senza lock espliciti.
    /// </summary>
    private readonly ConcurrentDictionary<string, WatchRuleViewModel> _ruleIndex = new();

    /// <summary>Catena delle operazioni sui runner, una per RuleId. Vedi <see cref="QueueRunnerSync"/>.</summary>
    private readonly Dictionary<string, Task> _runnerOps = new();
    private readonly object _runnerOpsGate = new();

    public WatchFoldersViewModel()
    {
        AddRuleCommand = ReactiveCommand.Create(AddRule);
        RemoveRuleCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(RemoveRuleAsync);
        BrowseSourceCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(BrowseSourceAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(BrowseDestinationAsync);
        RunNowCommand = ReactiveCommand.CreateFromTask<WatchRuleViewModel>(RunNowAsync);

        _statusHandler = OnStatusChanged;
        WatchFolderService.StatusChanged += _statusHandler;

        RulesLoad = LoadRulesAsync();
    }

    public ObservableCollection<WatchRuleViewModel> Rules { get; } = new();

    public bool HasRules => Rules.Count > 0;

    /// <summary>Caricamento iniziale; attendibile nei test (pattern JournalRestore).</summary>
    public Task RulesLoad { get; }

    /// <summary>Ultimo salvataggio best-effort; attendibile nei test.</summary>
    internal Task? LastSaveTask { get; private set; }

    /// <summary>
    /// Ultima operazione accodata sui runner: attenderla significa attendere anche tutte
    /// quelle accodate prima per la stessa regola (catena). Attendibile nei test.
    /// </summary>
    internal Task? LastRunnerOpTask { get; private set; }

    /// <summary>False nei test headless: nessun runner reale (pattern ApplyThemesToApplication).</summary>
    internal bool ManageRunners { get; set; } = true;

    public ReactiveCommand<Unit, Unit> AddRuleCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> RemoveRuleCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> BrowseSourceCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> BrowseDestinationCommand { get; }
    public ReactiveCommand<WatchRuleViewModel, Unit> RunNowCommand { get; }

    /// <summary>Pubblico per i test.</summary>
    public void AddRule()
    {
        var model = new WatchRule();
        if (!AndroidRuntime.IsNotAndroid)
            model.Mode = WatchMode.Interval;
        var rule = new WatchRuleViewModel(model, this);
        Rules.Add(rule);
        _ruleIndex[rule.Model.Id] = rule;
        this.RaisePropertyChanged(nameof(HasRules));
        // Nessun salvataggio: una regola senza percorsi verrebbe scartata dal Sanitize dello store.
    }

    /// <summary>Pubblico per i test.</summary>
    public async Task RemoveRuleAsync(WatchRuleViewModel rule)
    {
        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            LocalizationService.Tr("Str.WatchFolders.RemoveConfirmTitle"),
            string.Format(LocalizationService.Tr("Str.WatchFolders.RemoveConfirmMessageFormat"), rule.SourcePath, rule.DestinationPath),
            LocalizationService.Tr("Str.WatchFolders.Remove"));
        if (!confirmed)
            return;

        if (ManageRunners)
        {
            // Nella stessa catena degli altri riallineamenti: uno stop diretto potrebbe
            // essere scavalcato da un OnRuleChanged ancora accodato (es. l'utente attiva
            // la regola e la rimuove subito dopo), che riavvierebbe un runner per una
            // regola non più esistente.
            QueueRunnerOp(rule.Model.Id, () =>
            {
                try
                {
                    WatchFolderService.Stop(rule.Model.Id);
                }
                catch (Exception)
                {
                    // best effort: la riga sparisce comunque dalla lista
                }
            });
        }

        Rules.Remove(rule);
        _ruleIndex.TryRemove(rule.Model.Id, out _);
        this.RaisePropertyChanged(nameof(HasRules));
        SaveRules();
    }

    private async Task BrowseSourceAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.SourcePath);
        if (selected is null || IsSelfFeeding(rule, selected, rule.DestinationPath))
            return;

        rule.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.DestinationPath);
        if (selected is null || IsSelfFeeding(rule, rule.SourcePath, selected))
            return;

        rule.DestinationPath = selected;
    }

    /// <summary>
    /// Rifiuta una combinazione che si autoalimenta (destinazione dentro la sorgente):
    /// il percorso non viene assegnato, quindi non viene né persistito né dato a un runner.
    /// La riga non ha un'altra superficie d'errore: il motivo va nel badge di stato.
    /// </summary>
    private static bool IsSelfFeeding(WatchRuleViewModel rule, string source, string destination)
    {
        if (!WatchFolderService.IsDestinationInsideSource(source, destination))
            return false;

        rule.StatusText = LocalizationService.Tr("Str.WatchFolders.SelfFeeding");
        return true;
    }

    /// <summary>Pubblico per i test.</summary>
    public async Task RunNowAsync(WatchRuleViewModel rule)
    {
        try
        {
            await WatchFolderService.RunNowAsync(rule.Model);
        }
        catch (OperationCanceledException)
        {
            // runner fermato durante l'esecuzione manuale: lo stato lo segnala già
        }
    }

    /// <summary>Chiamato dalle righe a ogni modifica: persiste e riallinea il runner.</summary>
    internal void OnRuleChanged(WatchRuleViewModel rule)
    {
        SaveRules();

        if (!ManageRunners)
            return;

        QueueRunnerSync(rule);
    }

    /// <summary>
    /// Accoda il riallineamento del runner in una catena per regola: OnRuleChanged scatta
    /// a ogni set di proprietà della riga, quindi due modifiche ravvicinate producevano due
    /// Task.Run concorrenti sulla stessa regola. La catena li esegue nell'ordine di arrivo
    /// e uno alla volta, così l'ultima azione dell'utente è anche l'ultima applicata
    /// (un disable non può più essere scavalcato da uno start più lento partito prima).
    /// La mutua esclusione con gli altri chiamanti del servizio — l'avvio iniziale di App,
    /// un'altra istanza della scheda — la garantisce comunque il lock per regola dentro
    /// <see cref="WatchFolderService.Start"/>: qui si serializza solo l'ordine.
    /// </summary>
    private void QueueRunnerSync(WatchRuleViewModel rule) =>
        QueueRunnerOp(rule.Model.Id, () => ApplyRunnerState(rule));

    /// <summary>Accoda un'operazione nella catena della regola (vedi <see cref="QueueRunnerSync"/>).</summary>
    private void QueueRunnerOp(string ruleId, Action operation)
    {
        lock (_runnerOpsGate)
        {
            Task previous = _runnerOps.TryGetValue(ruleId, out Task? pending) ? pending : Task.CompletedTask;
            Task next = previous.ContinueWith(
                _ => operation(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            _runnerOps[ruleId] = next;
            LastRunnerOpTask = next;
        }
    }

    /// <summary>
    /// Ferma il runner della regola e lo riavvia se la regola è attiva e completa.
    /// Non lancia mai: una catena che si guasta lascerebbe la regola senza riallineamenti.
    /// </summary>
    private void ApplyRunnerState(WatchRuleViewModel rule)
    {
        try
        {
            WatchFolderService.Stop(rule.Model.Id);
        }
        catch (Exception)
        {
            // Difesa in profondità: Stop non è garantito lanciare se il runner
            // non esiste già. Una regola disabilitata è OK.
        }

        // Il check di Enabled qui dentro è sicuro: letture atomiche e stato più fresco al
        // momento dello Start. Con la catena per regola è anche definitivo: nessuna
        // operazione precedente può più applicarsi dopo questa.
        if (rule.Model.Enabled
            && !string.IsNullOrWhiteSpace(rule.Model.SourcePath)
            && !string.IsNullOrWhiteSpace(rule.Model.DestinationPath))
        {
            try
            {
                WatchFolderService.Start(rule.Model);

                // Su Android il runner appena avviato muore con l'Activity se non c'è già un
                // foreground service a tenerlo vivo (vedi App.axaml.cs, che lo avvia solo se
                // trova regole abilitate AL LAUNCH): una regola abilitata a runtime, la prima
                // di una sessione, non passa mai da lì. StartBackgroundWatchHost è idempotente
                // (richiama solo StartForegroundService, no-op se già in esecuzione) e null su
                // desktop, quindi è sicuro invocarlo qui a ogni avvio di runner.
                App.StartBackgroundWatchHost?.Invoke();
            }
            catch (Exception)
            {
                // Difesa in profondità: Start non lancia più, ma una singola regola
                // malata non deve fermare l'arresto della precedente.
            }
        }

        // Nessuna regola abilitata rimasta: ferma il foreground service invece di lasciarlo
        // vivo con una notifica persistente e nulla da sincronizzare. Null su desktop.
        if (!Rules.Any(r => r.Model.Enabled))
        {
            App.StopBackgroundWatchHost?.Invoke();
        }
    }

    private void SaveRules()
    {
        List<WatchRule> models = Rules.Select(r => r.Model).ToList();
        LastSaveTask = SaveRulesAsync(models);
    }

    private static async Task SaveRulesAsync(IReadOnlyList<WatchRule> rules)
    {
        try
        {
            await WatchRuleStore.SaveAsync(rules);
        }
        catch (Exception)
        {
            // best effort: la UI non deve rompersi se il disco non è scrivibile
        }
    }

    private async Task LoadRulesAsync()
    {
        List<WatchRule> rules = await WatchRuleStore.LoadAsync();
        foreach (WatchRule rule in rules)
        {
            // StatusText nasce dalla baseline della riga (regola attiva → runner già
            // avviato da App), non da un valore fisso: gli stati emessi prima di questa
            // sottoscrizione sono persi.
            var row = new WatchRuleViewModel(rule, this);
            Rules.Add(row);
            _ruleIndex[rule.Id] = row;
        }

        this.RaisePropertyChanged(nameof(HasRules));
        // I runner delle regole attive sono già stati avviati da App all'apertura.
    }

    private void OnStatusChanged(WatchStatus status)
    {
        // Thread di background: lookup via _ruleIndex (thread-safe), assegnazioni sulla
        // riga trovata marshalate sul thread UI come per i progressi di copia.
        if (!_ruleIndex.TryGetValue(status.RuleId, out WatchRuleViewModel? row))
            return;

        UiDispatch.Post(() =>
        {
            row.StatusText = FormatStatusMessage(status);
            if (status.LastRunUtc is { } lastRun)
                row.LastRunText = string.Format(LocalizationService.Tr("Str.WatchFolders.LastRunFormat"), lastRun.ToLocalTime());
        });
    }

    /// <summary>
    /// Traduce l'identificatore stabile e indipendente dalla lingua emesso dal Service
    /// (<see cref="WatchStatus.MessageKind"/>) nel testo mostrato in UI. Confine
    /// Service→ViewModel: vedi il commento sullo stesso pattern in
    /// <see cref="DuplicatesViewModel"/> per <c>DuplicateFinderService.PartialHashStage</c>.
    /// </summary>
    private static string FormatStatusMessage(WatchStatus status) => status.MessageKind switch
    {
        WatchFolderService.StatusSyncing => LocalizationService.Tr("Str.WatchFolders.Status.Syncing"),
        WatchFolderService.StatusCompleted => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.CompletedFormat"),
            (status.LastRunUtc ?? DateTime.UtcNow).ToLocalTime()),
        WatchFolderService.StatusInterrupted => LocalizationService.Tr("Str.WatchFolders.Status.Interrupted"),
        WatchFolderService.StatusError => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.ErrorFormat"), status.MessageDetail),
        WatchFolderService.StatusSourceNotFound => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.SourceNotFoundFormat"), status.MessageDetail),
        WatchFolderService.StatusStartFailed => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.StartFailedFormat"), status.MessageDetail),
        WatchFolderService.StatusSelfFeeding => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.SelfFeedingFormat"), status.MessageDetail),
        WatchFolderService.StatusDestinationNotFound => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.DestinationNotFoundFormat"), status.MessageDetail),
        WatchFolderService.StatusWatcherError => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.WatcherErrorFormat"), status.MessageDetail),
        WatchFolderService.StatusWatcherNotRestored => string.Format(
            LocalizationService.Tr("Str.WatchFolders.Status.WatcherNotRestoredFormat"), status.MessageDetail),
        _ => status.MessageDetail ?? status.MessageKind,
    };

    public void Dispose() => WatchFolderService.StatusChanged -= _statusHandler;
}
