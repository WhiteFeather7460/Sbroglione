using System;
using System.Reactive;
using System.Threading.Tasks;
using Sbroglione.Models;
using Sbroglione.Services;
using ReactiveUI;

namespace Sbroglione.ViewModels;

/// <summary>Stato della shell: pannello di navigazione laterale espanso/collassato (persistito) e banner di aggiornamento.</summary>
public class MainWindowViewModel : ViewModelBase
{
    private bool _isNavExpanded;
    private bool _showUpdateBanner;
    private string _updateVersionText = string.Empty;
    private bool _isUpdating;
    private double _updateProgress;
    private string? _updateErrorMessage;
    private UpdateInfo? _pendingUpdate;

    public MainWindowViewModel()
    {
        _isNavExpanded = AppSettingsStore.Current.NavExpanded;
        ToggleNavCommand = ReactiveCommand.CreateFromTask(ToggleNavAsync);
        UpdateCommand = ReactiveCommand.CreateFromTask(ApplyUpdateAsync);
        DismissUpdateCommand = ReactiveCommand.CreateFromTask(DismissUpdateAsync);
    }

    public bool IsNavExpanded
    {
        get => _isNavExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isNavExpanded, value);
    }

    /// <summary>True quando una nuova versione è disponibile e non è stata ignorata dall'utente.</summary>
    public bool ShowUpdateBanner
    {
        get => _showUpdateBanner;
        private set => this.RaiseAndSetIfChanged(ref _showUpdateBanner, value);
    }

    /// <summary>Numero di versione (es. "1.4.0") mostrato nel banner.</summary>
    public string UpdateVersionText
    {
        get => _updateVersionText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _updateVersionText, value);
            this.RaisePropertyChanged(nameof(UpdateBannerText));
        }
    }

    /// <summary>Testo del banner già formattato ("Nuova versione X.Y.Z disponibile" / equivalente EN), usato da MainWindow.axaml.</summary>
    public string UpdateBannerText => string.Format(LocalizationService.Tr("Str.Update.BannerTextFormat"), UpdateVersionText);

    /// <summary>True mentre il download/replace dell'eseguibile è in corso: la UI mostra la progress bar al posto dei pulsanti.</summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        private set => this.RaiseAndSetIfChanged(ref _isUpdating, value);
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        private set => this.RaiseAndSetIfChanged(ref _updateProgress, value);
    }

    /// <summary>Messaggio d'errore dell'ultimo tentativo di aggiornamento fallito; null se nessun errore.</summary>
    public string? UpdateErrorMessage
    {
        get => _updateErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _updateErrorMessage, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleNavCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissUpdateCommand { get; }

    internal async Task ToggleNavAsync()
    {
        IsNavExpanded = !IsNavExpanded;
        AppSettingsStore.Current.NavExpanded = IsNavExpanded;
        try
        {
            await AppSettingsStore.SaveCurrentAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: lo stato resta valido in memoria anche se il salvataggio su disco fallisce.
        }
    }

    /// <summary>
    /// Controlla GitHub in background e mostra il banner se c'è una versione più recente non
    /// ignorata. Chiamato esplicitamente da App.axaml.cs dopo lo startup — non dal costruttore,
    /// per non scattare (con relativa chiamata HTTP) in ogni test che costruisce questa ViewModel.
    /// </summary>
    public async Task StartUpdateCheckAsync()
    {
        UpdateCheckResult result = await UpdateCheckService.CheckAsync().ConfigureAwait(false);
        if (result.Status != UpdateCheckStatus.Available || result.Info is null)
            return;

        string versionText = result.Info.Version.ToString();
        if (versionText == AppSettingsStore.Current.IgnoredUpdateVersion)
            return;

        UiDispatch.Post(() =>
        {
            _pendingUpdate = result.Info;
            UpdateVersionText = versionText;
            UpdateErrorMessage = null;
            ShowUpdateBanner = true;
        });
    }

    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null)
            return;

        IsUpdating = true;
        UpdateErrorMessage = null;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<double>(value => UiDispatch.Post(() => UpdateProgress = value));
            await SelfUpdateService.ApplyUpdateAsync(_pendingUpdate, progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiDispatch.Post(() =>
            {
                UpdateErrorMessage = ex.Message;
                IsUpdating = false;
            });
        }
    }

    private async Task DismissUpdateAsync()
    {
        AppSettingsStore.Current.IgnoredUpdateVersion = UpdateVersionText;
        ShowUpdateBanner = false;
        try
        {
            await AppSettingsStore.SaveCurrentAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: il banner resta comunque nascosto in memoria per questa sessione.
        }
    }
}
