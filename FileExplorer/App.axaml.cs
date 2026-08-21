using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;
using FileExplorer.Views;

namespace FileExplorer;

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
            ColorTheme? customTheme = AppSettingsStore.Current.CustomThemeId is { } themeId
                ? ThemeStore.Load(themeId)
                : null;
            if (customTheme is not null)
                ThemeService.Apply(customTheme);
            else
                RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);

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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ThemeVariant ParseThemeVariant(string value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
