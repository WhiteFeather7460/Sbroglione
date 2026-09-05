using Avalonia.Controls;
using Sbroglione.PluginContracts;

namespace Sbroglione.TestPluginFixture;

/// <summary>Plugin minimo usato solo dai test di <c>PluginLoader</c> (Sbroglione.Tests).</summary>
public sealed class FixtureTabPlugin : ITabPlugin
{
    public static int UnloadCallCount;

    public string Id => "fixture-plugin";
    public string Header => "Fixture";
    public string IconGlyph => "fa-solid fa-flask";

    public Control CreateView() => new TextBlock { Text = "fixture plugin view" };

    public void OnUnload() => UnloadCallCount++;
}
