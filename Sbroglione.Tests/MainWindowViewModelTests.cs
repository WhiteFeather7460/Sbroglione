using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public MainWindowViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-mainvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Constructor_ReadsNavExpandedFromSettings()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = false };
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsNavExpanded);
    }

    [Fact]
    public async Task ToggleNavAsync_FlipsStateAndPersists()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = true };
        var vm = new MainWindowViewModel();

        await vm.ToggleNavAsync();

        Assert.False(vm.IsNavExpanded);
        Assert.False(AppSettingsStore.Current.NavExpanded);
        var reloaded = await AppSettingsStore.LoadAsync(AppSettingsStore.CurrentPath);
        Assert.False(reloaded.NavExpanded);
    }

    [Fact]
    public async Task ToggleNavAsync_Twice_ReturnsToExpanded()
    {
        AppSettingsStore.Current = new AppSettings { NavExpanded = true };
        var vm = new MainWindowViewModel();

        await vm.ToggleNavAsync();
        await vm.ToggleNavAsync();

        Assert.True(vm.IsNavExpanded);
        Assert.True(AppSettingsStore.Current.NavExpanded);
    }
}
