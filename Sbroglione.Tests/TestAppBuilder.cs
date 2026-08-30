using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

using Sbroglione;
using Sbroglione.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Sbroglione.Tests;

// Configura l'app Avalonia headless usata da [AvaloniaFact] per costruire le view
// (es. MainView) senza bisogno di un display reale.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
