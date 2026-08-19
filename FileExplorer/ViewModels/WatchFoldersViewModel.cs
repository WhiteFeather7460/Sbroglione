using System;
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
        Rules.Add(new WatchRuleViewModel(new WatchRule(), this));
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
        this.RaisePropertyChanged(nameof(HasRules));
        SaveRules();
    }

    private async Task BrowseSourceAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.SourcePath);
        if (selected is not null)
            rule.SourcePath = selected;
    }

    private async Task BrowseDestinationAsync(WatchRuleViewModel rule)
    {
        string? selected = await SelectPathDialogHelper.ShowAsync(directoriesOnly: true, rule.DestinationPath);
        if (selected is not null)
            rule.DestinationPath = selected;
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

        WatchFolderService.Stop(rule.Model.Id);
        if (rule.Model.Enabled
            && !string.IsNullOrWhiteSpace(rule.Model.SourcePath)
            && !string.IsNullOrWhiteSpace(rule.Model.DestinationPath))
        {
            WatchFolderService.Start(rule.Model);
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
            Rules.Add(new WatchRuleViewModel(rule, this)
            {
                StatusText = rule.Enabled ? "In attesa" : "Disattivata"
            });
        }

        this.RaisePropertyChanged(nameof(HasRules));
        // I runner delle regole attive sono già stati avviati da App all'apertura.
    }

    private void OnStatusChanged(WatchStatus status)
    {
        // Thread di background: assegnazioni dirette come per i progressi di copia.
        WatchRuleViewModel? row = Rules.FirstOrDefault(r => r.Model.Id == status.RuleId);
        if (row is null)
            return;

        row.StatusText = status.Message;
        if (status.LastRunUtc is { } lastRun)
            row.LastRunText = $"Ultima sync: {lastRun.ToLocalTime():HH:mm:ss}";
    }

    public void Dispose() => WatchFolderService.StatusChanged -= _statusHandler;
}
