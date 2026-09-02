using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;
using Sbroglione.Views;

namespace Sbroglione;

public partial class App : Application
{
    /// <summary>
    /// Seam piattaforma: avvia l'host di background che tiene vivi i runner watch-folder
    /// quando il processo non è una normale app desktop (su Android il foreground service
    /// <c>WatchFolderForegroundService</c>). Impostato dall'head project prima che
    /// <see cref="OnFrameworkInitializationCompleted"/> giri (Avalonia chiama
    /// <c>CustomizeAppBuilder</c> prima); resta <c>null</c> su desktop, dove i runner
    /// partono in-process.
    /// </summary>
    public static Action? StartBackgroundWatchHost { get; set; }

    /// <summary>
    /// Seam piattaforma: stato del permesso "All files access". <c>null</c> su desktop (dove
    /// non serve alcun permesso — <see cref="MainWindowViewModel.IsStorageAccessGranted"/> resta
    /// sempre <c>true</c>), impostato da <c>MainActivity</c> su Android.
    /// </summary>
    public static Func<bool>? StorageAccessGranted { get; set; }

    /// <summary>Apre le Impostazioni di sistema per concedere il permesso. <c>null</c> su desktop.</summary>
    public static Action? RequestStorageAccess { get; set; }

    /// <summary>
    /// Invocato da <c>MainActivity.OnResume</c> quando l'utente torna dalle Impostazioni: la UI
    /// non ha altro modo di accorgersi di una concessione/revoca avvenuta fuori dall'app.
    /// </summary>
    public static Action<bool>? OnStorageAccessChanged { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettingsStore.LoadCurrent();
            LocalizationService.Apply(AppSettingsStore.Current.Language);
            ApplySavedTheme();

            // Avvia i runner watch-folder delle regole attive. Nessun handler di
            // shutdown nell'app: i runner muoiono col processo (limite dichiarato).
            List<WatchRule> rules = WatchRuleStore.Load();
            _ = Task.Run(() => WatchFolderService.StartAllEnabledRules(rules));

            // Best effort: rimuove un .old lasciato da un update precedente. Prima di creare
            // la finestra, non blocca comunque lo startup (I/O trascurabile, un file).
            SelfUpdateService.CleanupOrphanBackup();

            var mainWindowViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel
            };

            // Fire-and-forget: il check di aggiornamento non deve bloccare l'avvio né la UI;
            // eventuali errori restano contenuti dentro StartUpdateCheckAsync (nessuna eccezione
            // propagata al chiamante).
            _ = mainWindowViewModel.StartUpdateCheckAsync();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            AppSettingsStore.LoadCurrent();
            LocalizationService.Apply(AppSettingsStore.Current.Language);
            ApplySavedTheme();

            SelfUpdateService.CleanupOrphanBackup();

            // Fase 3C: su Android i runner non possono vivere nel processo dell'Activity
            // (Doze / chiusura), quindi li ospita un foreground service registrato dall'head
            // project in StartBackgroundWatchHost. Lo si avvia solo se c'è almeno una regola
            // abilitata: un foreground service richiede una notifica persistente, e mostrarla
            // senza nulla da sincronizzare sarebbe solo rumore.
            // IsWatchFolderSupported segue il permesso di storage: la tab mostra il banner
            // finché l'accesso non è concesso, poi la UI di gestione regole (solo Interval,
            // vedi WatchFoldersView).
            if (StartBackgroundWatchHost is { } startBackgroundWatchHost
                && WatchRuleStore.Load().Exists(rule => rule.Enabled))
            {
                try
                {
                    startBackgroundWatchHost();
                }
                catch (Exception)
                {
                    // L'avvio del service non deve mai impedire l'apertura della UI.
                }
            }

            var mainViewModel = new MainWindowViewModel
            {
                IsWatchFolderSupported = StorageAccessGranted?.Invoke() ?? false,
                IsStorageAccessGranted = StorageAccessGranted?.Invoke() ?? true
            };
            OnStorageAccessChanged = granted => UiDispatch.Post(() =>
            {
                mainViewModel.IsStorageAccessGranted = granted;
                mainViewModel.IsWatchFolderSupported = granted;
            });
            singleView.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Applica il tema salvato in <see cref="AppSettingsStore.Current"/> (custom o
    /// Light/Dark/Default): comune ai branch desktop e single-view (Android), entrambi
    /// avviati da <see cref="OnFrameworkInitializationCompleted"/> dopo aver caricato le
    /// impostazioni, cosicché la scelta dell'utente persista tra i riavvii su ogni piattaforma.
    /// </summary>
    private void ApplySavedTheme()
    {
        ColorTheme? customTheme = AppSettingsStore.Current.CustomThemeId is { } themeId
            ? ThemeStore.Load(themeId)
            : null;
        if (customTheme is not null)
            ThemeService.Apply(customTheme);
        else
            RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);
    }

    private static ThemeVariant ParseThemeVariant(string value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
