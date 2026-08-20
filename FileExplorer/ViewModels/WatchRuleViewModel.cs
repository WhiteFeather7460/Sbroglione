using System;
using System.Linq;

using FileExplorer.Models;
using FileExplorer.Services;

using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>Riga reattiva di una regola watch-folder; inoltra ogni modifica al parent.</summary>
public class WatchRuleViewModel : ReactiveObject
{
    private string? _statusText;
    private string? _lastRunText;

    public WatchRuleViewModel(WatchRule model, WatchFoldersViewModel? owner)
    {
        Model = model;
        Owner = owner;
        _statusText = BaselineStatus();
    }

    /// <summary>Modello persistito sottostante.</summary>
    public WatchRule Model { get; }

    /// <summary>ViewModel della scheda; null solo nei test di unità della riga.</summary>
    public WatchFoldersViewModel? Owner { get; }

    public string SourcePath
    {
        get => Model.SourcePath;
        set
        {
            if (Model.SourcePath == value)
                return;
            Model.SourcePath = value;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public string DestinationPath
    {
        get => Model.DestinationPath;
        set
        {
            if (Model.DestinationPath == value)
                return;
            Model.DestinationPath = value;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value)
                return;
            Model.Enabled = value;
            this.RaisePropertyChanged();

            string? statusBefore = _statusText;
            Owner?.OnRuleChanged(this);

            // Il badge deve seguire l'interruttore. Se però il riallineamento dei runner
            // ha già emesso uno stato (tipicamente un avvio rifiutato), quello vince:
            // sostituirlo con la baseline nasconderebbe l'errore appena mostrato.
            if (_statusText == statusBefore)
                StatusText = BaselineStatus();
        }
    }

    /// <summary>Adapter radio: agisce solo su true (pattern IsTheme* di SettingsViewModel).</summary>
    public bool IsOnChange
    {
        get => Model.Mode == WatchMode.OnChange;
        set
        {
            if (!value || Model.Mode == WatchMode.OnChange)
                return;
            Model.Mode = WatchMode.OnChange;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsInterval));
            Owner?.OnRuleChanged(this);
        }
    }

    /// <summary>Adapter radio: agisce solo su true.</summary>
    public bool IsInterval
    {
        get => Model.Mode == WatchMode.Interval;
        set
        {
            if (!value || Model.Mode == WatchMode.Interval)
                return;
            Model.Mode = WatchMode.Interval;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsOnChange));
            Owner?.OnRuleChanged(this);
        }
    }

    public int IntervalMinutes
    {
        get => Model.IntervalMinutes;
        set
        {
            int clamped = Math.Clamp(value, WatchRuleStore.MinIntervalMinutes, WatchRuleStore.MaxIntervalMinutes);
            if (Model.IntervalMinutes == clamped)
                return;
            Model.IntervalMinutes = clamped;
            this.RaisePropertyChanged();
            Owner?.OnRuleChanged(this);
        }
    }

    public string? StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string? LastRunText
    {
        get => _lastRunText;
        set => this.RaiseAndSetIfChanged(ref _lastRunText, value);
    }

    /// <summary>
    /// Stato di partenza dedotto dai runner realmente attivi: gli stati emessi prima che
    /// la scheda esistesse (regole avviate da App all'apertura) sono persi per sempre,
    /// quindi il badge non può basarsi solo sulle notifiche live.
    /// </summary>
    private string BaselineStatus()
    {
        if (!Model.Enabled)
            return "Disattivata";

        return WatchFolderService.ActiveRuleIds.Contains(Model.Id) ? "In ascolto" : "In attesa";
    }
}
