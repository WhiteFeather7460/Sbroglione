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
            _ = Task.Run(() =>
            {
                foreach (WatchRule rule in rules)
                {
                    if (!rule.Enabled)
                        continue;
                    try
                    {
                        WatchFolderService.Start(rule);
                    }
                    catch (Exception)
                    {
                        // Difesa in profondità: Start non lancia più, ma una singola regola
                        // malata non deve fermare le altre.
                    }
                }
            });

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

            var mainViewModel = new MainWindowViewModel
            {
                IsWatchFolderSupported = false
            };
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
