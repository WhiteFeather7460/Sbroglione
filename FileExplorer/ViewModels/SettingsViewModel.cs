using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FileExplorer.Models;
using FileExplorer.Services;
using ReactiveUI;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Impostazioni": espone le proprietà di <see cref="AppSettingsStore.Current"/>
/// con auto-save ad ogni modifica (nessun bottone "Salva").
/// </summary>
public class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly Action _throttleChangedHandler;

    public SettingsViewModel()
    {
        _throttleChangedHandler = () =>
        {
            this.RaisePropertyChanged(nameof(ThrottleEnabled));
            this.RaisePropertyChanged(nameof(ThrottleMBps));
        };
        AppSettingsStore.ThrottleChanged += _throttleChangedHandler;

        foreach (ColorTheme theme in ThemeStore.LoadAll())
            CustomThemes.Add(theme);
    }

    /// <summary>
    /// Rimuove l'handler dall'evento statico <see cref="AppSettingsStore.ThrottleChanged"/>:
    /// senza questo, ogni istanza resterebbe rootata per sempre (leak).
    /// </summary>
    public void Dispose()
    {
        AppSettingsStore.ThrottleChanged -= _throttleChangedHandler;
        GC.SuppressFinalize(this);
    }

    public bool AutoParallelism
    {
        get => AppSettingsStore.Current.AutoParallelism;
        set
        {
            if (AppSettingsStore.Current.AutoParallelism == value)
                return;

            AppSettingsStore.Current.AutoParallelism = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public int ManualParallelism
    {
        get => AppSettingsStore.Current.ManualParallelism;
        set
        {
            int clamped = Math.Clamp(value, 1, 32);
            if (AppSettingsStore.Current.ManualParallelism == clamped)
                return;

            AppSettingsStore.Current.ManualParallelism = clamped;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    /// <summary>Dimensione del buffer di copia in KB (leggibile in UI), 256-16384. Mappa BufferSizeBytes.</summary>
    public int BufferSizeKb
    {
        get => AppSettingsStore.Current.BufferSizeBytes / 1024;
        set
        {
            int clampedKb = Math.Clamp(value, 256, 16384);
            int bytes = clampedKb * 1024;
            if (AppSettingsStore.Current.BufferSizeBytes == bytes)
                return;

            AppSettingsStore.Current.BufferSizeBytes = bytes;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public bool VerifyChecksumAfterCopy
    {
        get => AppSettingsStore.Current.VerifyChecksumAfterCopy;
        set
        {
            if (AppSettingsStore.Current.VerifyChecksumAfterCopy == value)
                return;

            AppSettingsStore.Current.VerifyChecksumAfterCopy = value;
            this.RaisePropertyChanged();
            SaveCurrent();
        }
    }

    public bool ThrottleEnabled
    {
        get => AppSettingsStore.Current.ThrottleEnabled;
        set
        {
            if (AppSettingsStore.Current.ThrottleEnabled == value)
                return;

            AppSettingsStore.Current.ThrottleEnabled = value;
            this.RaisePropertyChanged();
            SaveCurrent();
            AppSettingsStore.RaiseThrottleChanged();
        }
    }

    public int ThrottleMBps
    {
        get => AppSettingsStore.Current.ThrottleMBps;
        set
        {
            int clamped = Math.Clamp(value, 1, 1000);
            if (AppSettingsStore.Current.ThrottleMBps == clamped)
                return;

            AppSettingsStore.Current.ThrottleMBps = clamped;
            this.RaisePropertyChanged();
            SaveCurrent();
            AppSettingsStore.RaiseThrottleChanged();
        }
    }

    public string ThemeVariant
    {
        get => AppSettingsStore.Current.ThemeVariant;
        set
        {
            bool hadCustom = AppSettingsStore.Current.CustomThemeId is not null;
            if (AppSettingsStore.Current.ThemeVariant == value && !hadCustom)
                return;

            AppSettingsStore.Current.ThemeVariant = value;
            AppSettingsStore.Current.CustomThemeId = null;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsThemeDefault));
            this.RaisePropertyChanged(nameof(IsThemeLight));
            this.RaisePropertyChanged(nameof(IsThemeDark));
            this.RaisePropertyChanged(nameof(ActiveCustomTheme));
            if (ApplyThemesToApplication)
                ThemeService.Revert(value);
            SaveCurrent();
        }
    }

    public bool IsThemeDefault
    {
        get => ThemeVariant == "Default" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Default"; }
    }

    public bool IsThemeLight
    {
        get => ThemeVariant == "Light" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Light"; }
    }

    public bool IsThemeDark
    {
        get => ThemeVariant == "Dark" && ActiveCustomTheme is null;
        set { if (value) ThemeVariant = "Dark"; }
    }

    /// <summary>Temi custom salvati su disco, in ordine alfabetico.</summary>
    public ObservableCollection<ColorTheme> CustomThemes { get; } = new();

    public bool HasCustomThemes => CustomThemes.Count > 0;

    /// <summary>False nei test: evita di toccare Application.Current tramite ThemeService.</summary>
    internal bool ApplyThemesToApplication { get; set; } = true;

    /// <summary>Tema custom attivo risolto da CustomThemeId, o null.</summary>
    public ColorTheme? ActiveCustomTheme =>
        CustomThemes.FirstOrDefault(t => t.Id == AppSettingsStore.Current.CustomThemeId);

    /// <summary>Attiva un tema custom: persiste l'id e applica i colori.</summary>
    public void ApplyCustomTheme(ColorTheme theme)
    {
        AppSettingsStore.Current.CustomThemeId = theme.Id;
        if (ApplyThemesToApplication)
            ThemeService.Apply(theme);
        this.RaisePropertyChanged(nameof(ActiveCustomTheme));
        this.RaisePropertyChanged(nameof(IsThemeDefault));
        this.RaisePropertyChanged(nameof(IsThemeLight));
        this.RaisePropertyChanged(nameof(IsThemeDark));
        SaveCurrent();
    }

    /// <summary>Copia modificabile di un tema (anche built-in): nuovo Id, nome "(copia)". Non persistita.</summary>
    public ColorTheme CreateThemeFrom(ColorTheme source)
    {
        ColorTheme copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " (copia)";
        copy.IsBuiltIn = false;
        return copy;
    }

    /// <summary>Elimina un tema custom; se era attivo torna alla variante base corrente.</summary>
    public async Task DeleteThemeAsync(ColorTheme theme)
    {
        bool wasActive = AppSettingsStore.Current.CustomThemeId == theme.Id;
        ThemeStore.Delete(theme.Id);
        CustomThemes.Remove(theme);
        this.RaisePropertyChanged(nameof(HasCustomThemes));

        if (wasActive)
        {
            AppSettingsStore.Current.CustomThemeId = null;
            if (ApplyThemesToApplication)
                ThemeService.Revert(AppSettingsStore.Current.ThemeVariant);
            this.RaisePropertyChanged(nameof(ActiveCustomTheme));
            this.RaisePropertyChanged(nameof(IsThemeDefault));
            this.RaisePropertyChanged(nameof(IsThemeLight));
            this.RaisePropertyChanged(nameof(IsThemeDark));
            SaveCurrent();
        }

        if (LastSaveTask is not null)
            await LastSaveTask;
    }

    /// <summary>Upsert nella lista dopo un salvataggio dall'editor (match per Id).</summary>
    public void OnThemeSaved(ColorTheme theme)
    {
        ColorTheme? existing = CustomThemes.FirstOrDefault(t => t.Id == theme.Id);
        if (existing is not null)
            CustomThemes.Remove(existing);
        CustomThemes.Add(theme);
        this.RaisePropertyChanged(nameof(HasCustomThemes));
        this.RaisePropertyChanged(nameof(ActiveCustomTheme));
    }

    public Task ExportThemeAsync(ColorTheme theme, string path) => ThemeStore.ExportAsync(theme, path);

    /// <summary>Importa da file: sanitizza, persiste e aggiunge alla lista. Null se illeggibile.</summary>
    public async Task<ColorTheme?> ImportThemeAsync(string path)
    {
        ColorTheme? theme = ThemeStore.Import(path);
        if (theme is null)
            return null;

        await ThemeStore.SaveAsync(theme);
        OnThemeSaved(theme);
        return theme;
    }

    /// <summary>
    /// Task dell'ultimo salvataggio fire-and-forget avviato da <see cref="SaveCurrent"/>.
    /// Esposta solo per consentire ai test di attendere deterministicamente il
    /// completamento del salvataggio in background, senza alterare il comportamento
    /// fire-and-forget per i chiamanti UI reali.
    /// </summary>
    internal Task? LastSaveTask { get; private set; }

    private void SaveCurrent()
    {
        LastSaveTask = SaveCurrentAsync();
    }

    private static async Task SaveCurrentAsync()
    {
        try
        {
            await AppSettingsStore.SaveCurrentAsync();
        }
        catch (Exception)
        {
            // best effort: le impostazioni restano valide in memoria anche se il salvataggio su disco fallisce.
        }
    }
}
