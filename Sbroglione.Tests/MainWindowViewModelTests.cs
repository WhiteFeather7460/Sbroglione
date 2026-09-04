using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
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
        UiDispatch.Override = action => action();
    }

    public void Dispose()
    {
        AppSettingsStore.Current = _originalCurrent;
        AppSettingsStore.CurrentPath = _originalCurrentPath;
        UiDispatch.Override = null;
        UpdateCheckService.Client = new HttpClient();
        UpdateCheckService.CurrentVersionOverride = null;
        UpdateCheckService.PlatformAssetSuffixOverride = null;
        SelfUpdateService.Client = new HttpClient();
        SelfUpdateService.OpenUrl = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
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

    [Fact]
    public async Task StartUpdateCheckAsync_NewVersionAvailable_ShowsBanner()
    {
        AppSettingsStore.Current = new AppSettings();
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = null;
        UpdateCheckService.Client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{ "tag_name": "v9.0.0", "html_url": "https://example.test/releases/tag/v9.0.0", "assets": [] }"""));

        var vm = new MainWindowViewModel();
        await vm.StartUpdateCheckAsync();

        Assert.True(vm.ShowUpdateBanner);
        Assert.Equal("9.0.0", vm.UpdateVersionText);
    }

    [Fact]
    public async Task StartUpdateCheckAsync_IgnoredVersion_DoesNotShowBanner()
    {
        AppSettingsStore.Current = new AppSettings { IgnoredUpdateVersion = "9.0.0" };
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = null;
        UpdateCheckService.Client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{ "tag_name": "v9.0.0", "html_url": "https://example.test/releases/tag/v9.0.0", "assets": [] }"""));

        var vm = new MainWindowViewModel();
        await vm.StartUpdateCheckAsync();

        Assert.False(vm.ShowUpdateBanner);
    }

    [Fact]
    public async Task DismissUpdateCommand_PersistsIgnoredVersionAndHidesBanner()
    {
        AppSettingsStore.Current = new AppSettings();
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = null;
        UpdateCheckService.Client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{ "tag_name": "v9.0.0", "html_url": "https://example.test/releases/tag/v9.0.0", "assets": [] }"""));

        var vm = new MainWindowViewModel();
        await vm.StartUpdateCheckAsync();
        await vm.DismissUpdateCommand.Execute();

        Assert.False(vm.ShowUpdateBanner);
        Assert.Equal("9.0.0", AppSettingsStore.Current.IgnoredUpdateVersion);
    }

    [Fact]
    public async Task UpdateCommand_Failure_SetsErrorMessageAndStopsUpdating()
    {
        AppSettingsStore.Current = new AppSettings();
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = ".exe";
        UpdateCheckService.Client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{ "tag_name": "v9.0.0", "html_url": "https://example.test/releases/tag/v9.0.0", "assets": [ { "name": "app.exe", "browser_download_url": "https://example.test/app.exe" } ] }"""));
        SelfUpdateService.Client = new HttpClient(new ThrowingHandler());

        var vm = new MainWindowViewModel();
        await vm.StartUpdateCheckAsync();
        await vm.UpdateCommand.Execute();

        Assert.False(vm.IsUpdating);
        Assert.NotNull(vm.UpdateErrorMessage);
    }

    [Fact]
    public async Task UpdateCommand_NoPlatformAsset_ClearsIsUpdating()
    {
        AppSettingsStore.Current = new AppSettings();
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = ".exe";
        UpdateCheckService.Client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{ "tag_name": "v9.0.0", "html_url": "https://example.test/releases/tag/v9.0.0", "assets": [] }"""));
        SelfUpdateService.OpenUrl = _ => { /* no-op: avoid actually launching a browser in tests */ };

        var vm = new MainWindowViewModel();
        await vm.StartUpdateCheckAsync();
        await vm.UpdateCommand.Execute();

        Assert.False(vm.IsUpdating);
    }

    [Fact]
    public void IsWatchFolderSupported_DefaultsToTrue()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.IsWatchFolderSupported);
    }

    [Fact]
    public void IsWatchFolderSupported_CanBeDisabled()
    {
        var vm = new MainWindowViewModel { IsWatchFolderSupported = false };

        Assert.False(vm.IsWatchFolderSupported);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        public StubHandler(HttpStatusCode status, string content) { _status = status; _content = content; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_content) });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated network failure");
    }
}
