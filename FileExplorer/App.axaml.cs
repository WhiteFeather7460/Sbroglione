using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

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
            AppSettingsStore.LoadCurrentAsync().GetAwaiter().GetResult();
            RequestedThemeVariant = ParseThemeVariant(AppSettingsStore.Current.ThemeVariant);

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
