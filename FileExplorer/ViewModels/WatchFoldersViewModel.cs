using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

using FileExplorer.Models;
using FileExplorer.Services;

using ReactiveUI;

namespace FileExplorer.ViewModels;

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
        var rule = new WatchRuleViewModel(new WatchRule(), this);
        Rules.Add(rule);
        _ruleIndex[rule.Model.Id] = rule;
        this.RaisePropertyChanged(nameof(HasRules));
        // Nessun salvataggio: una regola senza percorsi verrebbe scartata dal Sanitize dello store.
    }

    /// <summary>Pubblico per i test.</summary>
    public async Task RemoveRuleAsync(WatchRuleViewModel rule)
    {
        bool confirmed = await ConfirmDialogHelper.ShowAsync(
            "Rimuovere la regola?",
            $"La sincronizzazione automatica {rule.SourcePath} → {rule.DestinationPath} verrà rimossa.",
            "Rimuovi");
        if (!confirmed)
            return;

        if (ManageRunners)
            WatchFolderService.Stop(rule.Model.Id);
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

        rule.StatusText = $"{WatchFolderService.SelfFeedingMessagePrefix}: scegli un'altra cartella";
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

        // Stop e Start della stessa regola vanno nello stesso Task.Run sequenziale:
        // se eseguissero in task indipendenti, il threadpool potrebbe farli out-of-order,
        // e uno Stop arrivato dopo Start ucciderebbe il runner appena avviato.
        _ = Task.Run(() =>
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

            // Il check di Enabled qui dentro è sicuro: letture atomiche, stato più fresco
            // al momento dello Start, e auto-correttivo (il prossimo OnRuleChanged ferma
            // se l'utente disabilita intanto).
            if (rule.Model.Enabled
                && !string.IsNullOrWhiteSpace(rule.Model.SourcePath)
                && !string.IsNullOrWhiteSpace(rule.Model.DestinationPath))
            {
                try
                {
                    WatchFolderService.Start(rule.Model);
                }
                catch (Exception)
                {
                    // Difesa in profondità: Start non lancia più, ma una singola regola
                    // malata non deve fermare l'arresto della precedente.
                }
            }
        });
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
            row.StatusText = status.Message;
            if (status.LastRunUtc is { } lastRun)
                row.LastRunText = $"Ultima sync: {lastRun.ToLocalTime():HH:mm:ss}";
        });
    }

    public void Dispose() => WatchFolderService.StatusChanged -= _statusHandler;
}
