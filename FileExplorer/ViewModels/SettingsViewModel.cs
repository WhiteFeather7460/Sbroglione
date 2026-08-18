using System;
using System.Threading.Tasks;
using Avalonia;
using FileExplorer.Services;
using ReactiveUI;
using AvaloniaThemeVariant = Avalonia.Styling.ThemeVariant;

namespace FileExplorer.ViewModels;

/// <summary>
/// Scheda "Impostazioni": espone le proprietà di <see cref="AppSettingsStore.Current"/>
/// con auto-save ad ogni modifica (nessun bottone "Salva").
/// </summary>
public class SettingsViewModel : ViewModelBase
{
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
        }
    }

    public string ThemeVariant
    {
        get => AppSettingsStore.Current.ThemeVariant;
        set
        {
            if (AppSettingsStore.Current.ThemeVariant == value)
                return;

            AppSettingsStore.Current.ThemeVariant = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsThemeDefault));
            this.RaisePropertyChanged(nameof(IsThemeLight));
            this.RaisePropertyChanged(nameof(IsThemeDark));
            ApplyThemeVariant(value);
            SaveCurrent();
        }
    }

    public bool IsThemeDefault
    {
        get => ThemeVariant == "Default";
        set { if (value) ThemeVariant = "Default"; }
    }

    public bool IsThemeLight
    {
        get => ThemeVariant == "Light";
        set { if (value) ThemeVariant = "Light"; }
    }

    public bool IsThemeDark
    {
        get => ThemeVariant == "Dark";
        set { if (value) ThemeVariant = "Dark"; }
    }

    private static void ApplyThemeVariant(string value)
    {
        try
        {
            if (Application.Current is null)
                return;

            Application.Current.RequestedThemeVariant = value switch
            {
                "Light" => AvaloniaThemeVariant.Light,
                "Dark" => AvaloniaThemeVariant.Dark,
                _ => AvaloniaThemeVariant.Default
            };
        }
        catch (Exception)
        {
            // applicazione del tema opzionale: un fallimento qui non deve rompere il salvataggio.
        }
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
