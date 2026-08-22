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
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            AppSettingsStore.LoadCurrent();
            LocalizationService.Apply(AppSettingsStore.Current.Language);

            singleView.MainView = new TextBlock
            {
                Text = "Sbroglione — Android smoke test",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
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

    internal void SetApplicationLifetimeForTests(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime lifetime)
        => ApplicationLifetime = lifetime;
}
