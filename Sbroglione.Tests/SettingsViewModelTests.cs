using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

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

        await vm.LastSaveTask!;

        var saved = await AppSettingsStore.LoadAsync(AppSettingsStore.CurrentPath);
        Assert.Equal(8, saved.ManualParallelism);
    }

    [Fact]
    public async Task ThrottleMBps_ClampsAndPersists()
    {
        var viewModel = new SettingsViewModel();

        viewModel.ThrottleMBps = 5000;
        Assert.Equal(1000, AppSettingsStore.Current.ThrottleMBps);

        viewModel.ThrottleMBps = 0;
        Assert.Equal(1, AppSettingsStore.Current.ThrottleMBps);

        if (viewModel.LastSaveTask is not null)
            await viewModel.LastSaveTask;
    }

    [Fact]
    public void ThrottleEnabled_WritesSetting()
    {
        var viewModel = new SettingsViewModel();

        viewModel.ThrottleEnabled = true;
        Assert.True(AppSettingsStore.Current.ThrottleEnabled);
    }

    [Fact]
    public void Language_defaults_to_italian()
    {
        SettingsViewModel vm = new() { ApplyThemesToApplication = false };
        Assert.Equal("it", vm.Language);
        Assert.True(vm.IsLanguageItalian);
        Assert.False(vm.IsLanguageEnglish);
    }

    [Fact]
    public void Setting_IsLanguageEnglish_updates_Language_and_persists()
    {
        SettingsViewModel vm = new() { ApplyThemesToApplication = false };
        vm.IsLanguageEnglish = true;
        Assert.Equal("en", vm.Language);
        Assert.Equal("en", AppSettingsStore.Current.Language);
    }

    [Fact]
    public void Dispose_UnsubscribesFromThrottleChanged()
    {
        var vm = new SettingsViewModel();
        vm.Dispose();

        bool raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(SettingsViewModel.ThrottleEnabled);
        AppSettingsStore.RaiseThrottleChanged();

        Assert.False(raised);
    }
}
