using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

namespace Sbroglione.Tests;

public static class AvaloniaTestHelper
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        var app = new Application();
        app.Styles.Add(new FluentTheme());

        // Register the icon provider
        IconProvider.Current.Register<FontAwesomeIconProvider>();
    }
}
