using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly AppSettings _originalCurrent;
    private readonly string _originalCurrentPath;

    public SettingsViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-settingsvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrent = AppSettingsStore.Current;
        _originalCurrentPath = AppSettingsStore.CurrentPath;
        AppSettingsStore.CurrentPath = Path.Combine(_root, "settings.json");
        AppSettingsStore.Current = new AppSettings();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void AutoParallelism_Set_UpdatesCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.AutoParallelism = false;
        Assert.False(AppSettingsStore.Current.AutoParallelism);
    }

    [Fact]
    public void ManualParallelism_SetOutOfRange_ClampsTo1To32()
    {
        var vm = new SettingsViewModel();
        vm.ManualParallelism = 100;
        Assert.Equal(32, AppSettingsStore.Current.ManualParallelism);

        vm.ManualParallelism = 0;
        Assert.Equal(1, AppSettingsStore.Current.ManualParallelism);
    }

    [Fact]
    public void BufferSizeKb_Set_UpdatesBufferSizeBytesOnCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.BufferSizeKb = 4096;
        Assert.Equal(4096 * 1024, AppSettingsStore.Current.BufferSizeBytes);
    }

    [Fact]
    public void VerifyChecksumAfterCopy_Toggle_UpdatesCurrentSettings()
    {
        var vm = new SettingsViewModel();
        vm.VerifyChecksumAfterCopy = false;
        Assert.False(AppSettingsStore.Current.VerifyChecksumAfterCopy);
    }

    [Fact]
    public void IsThemeDark_SetTrue_UpdatesThemeVariantAndPeers()
    {
        var vm = new SettingsViewModel();
        vm.IsThemeDark = true;

        Assert.Equal("Dark", AppSettingsStore.Current.ThemeVariant);
        Assert.True(vm.IsThemeDark);
        Assert.False(vm.IsThemeLight);
        Assert.False(vm.IsThemeDefault);
    }

    [Fact]
    public async Task PropertyChange_PersistsToDiskAsynchronously()
    {
        var vm = new SettingsViewModel();
        vm.ManualParallelism = 8;

        for (int i = 0; i < 50 && !File.Exists(AppSettingsStore.CurrentPath); i++)
            await Task.Delay(20);

        var saved = await AppSettingsStore.LoadAsync(AppSettingsStore.CurrentPath);
        Assert.Equal(8, saved.ManualParallelism);
    }
}
