using System.Net;
using System.Text;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

file sealed class FakeDownloadHandler : HttpMessageHandler
{
    private readonly byte[] _content;

    public FakeDownloadHandler(string content) => _content = Encoding.UTF8.GetBytes(content);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_content)
        };
        response.Content.Headers.ContentLength = _content.Length;
        return Task.FromResult(response);
    }
}

public sealed class SelfUpdateServiceTests : IDisposable
{
    private readonly string _root;
    private readonly HttpClient _originalClient;
    private readonly string? _originalExePath;
    private readonly Action<string> _originalLaunch;
    private readonly Action<string> _originalOpenUrl;
    private readonly Action _originalExit;
    private readonly Action<string, string> _originalMove;

    public SelfUpdateServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-selfupdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _originalClient = SelfUpdateService.Client;
        _originalExePath = SelfUpdateService.CurrentExecutablePathOverride;
        _originalLaunch = SelfUpdateService.LaunchProcess;
        _originalOpenUrl = SelfUpdateService.OpenUrl;
        _originalExit = SelfUpdateService.ExitProcess;
        _originalMove = SelfUpdateService.MoveFileOverwrite;
    }

    public void Dispose()
    {
        SelfUpdateService.Client = _originalClient;
        SelfUpdateService.CurrentExecutablePathOverride = _originalExePath;
        SelfUpdateService.LaunchProcess = _originalLaunch;
        SelfUpdateService.OpenUrl = _originalOpenUrl;
        SelfUpdateService.ExitProcess = _originalExit;
        SelfUpdateService.MoveFileOverwrite = _originalMove;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ApplyUpdateAsync_NullAssetUrl_OpensReleaseUrlAndReturnsFalse()
    {
        string? openedUrl = null;
        SelfUpdateService.OpenUrl = url => openedUrl = url;
        bool launched = false;
        SelfUpdateService.LaunchProcess = _ => launched = true;

        var info = new UpdateInfo(new Version(2, 0, 0), "https://example.test/releases/tag/v2.0.0", null, null);

        bool result = await SelfUpdateService.ApplyUpdateAsync(info, progress: null);

        Assert.False(result);
        Assert.Equal("https://example.test/releases/tag/v2.0.0", openedUrl);
        Assert.False(launched);
    }

    [Fact]
    public async Task ApplyUpdateAsync_DownloadsAndReplacesExecutable_ThenLaunchesAndExits()
    {
        string exePath = Path.Combine(_root, "Sbroglione.Desktop.exe");
        File.WriteAllText(exePath, "OLD");
        SelfUpdateService.CurrentExecutablePathOverride = exePath;
        SelfUpdateService.Client = new HttpClient(new FakeDownloadHandler("NEW"));

        string? launchedPath = null;
        SelfUpdateService.LaunchProcess = path => launchedPath = path;
        bool exited = false;
        SelfUpdateService.ExitProcess = () => exited = true;

        var info = new UpdateInfo(new Version(2, 0, 0), "https://example.test/releases/tag/v2.0.0", "https://example.test/app.exe", "Sbroglione.Desktop.exe");

        var progressValues = new List<double>();
        bool result = await SelfUpdateService.ApplyUpdateAsync(info, new Progress<double>(progressValues.Add));

        Assert.True(result);
        Assert.Equal("NEW", File.ReadAllText(exePath));
        Assert.Equal("OLD", File.ReadAllText(exePath + ".old"));
        Assert.Equal(exePath, launchedPath);
        Assert.True(exited);
        Assert.Contains(1.0, progressValues);
    }

    [Fact]
    public async Task ApplyUpdateAsync_ReplaceFailure_RestoresBackupAndThrows()
    {
        string exePath = Path.Combine(_root, "Sbroglione.Desktop.exe");
        File.WriteAllText(exePath, "OLD");
        SelfUpdateService.CurrentExecutablePathOverride = exePath;
        SelfUpdateService.Client = new HttpClient(new FakeDownloadHandler("NEW"));

        int moveCallCount = 0;
        SelfUpdateService.MoveFileOverwrite = (src, dest) =>
        {
            moveCallCount++;
            // La 2a chiamata è quella che installa il file scaricato al posto dell'exe: la
            // facciamo fallire per verificare il ripristino dal backup.
            if (moveCallCount == 2)
                throw new IOException("simulated failure");
            File.Move(src, dest, overwrite: true);
        };

        var info = new UpdateInfo(new Version(2, 0, 0), "https://example.test/releases/tag/v2.0.0", "https://example.test/app.exe", "Sbroglione.Desktop.exe");

        await Assert.ThrowsAsync<IOException>(() => SelfUpdateService.ApplyUpdateAsync(info, progress: null));

        Assert.Equal("OLD", File.ReadAllText(exePath));
    }

    [Fact]
    public void CleanupOrphanBackup_DeletesLeftoverOldFile()
    {
        string exePath = Path.Combine(_root, "Sbroglione.Desktop.exe");
        File.WriteAllText(exePath, "CURRENT");
        File.WriteAllText(exePath + ".old", "ORPHAN");
        SelfUpdateService.CurrentExecutablePathOverride = exePath;

        SelfUpdateService.CleanupOrphanBackup();

        Assert.False(File.Exists(exePath + ".old"));
    }

    [Fact]
    public void CleanupOrphanBackup_NoOrphanFile_DoesNotThrow()
    {
        string exePath = Path.Combine(_root, "Sbroglione.Desktop.exe");
        File.WriteAllText(exePath, "CURRENT");
        SelfUpdateService.CurrentExecutablePathOverride = exePath;

        SelfUpdateService.CleanupOrphanBackup();
    }

    [Fact]
    public async Task ApplyUpdateAsync_NonHttpsReleaseUrl_ThrowsAndDoesNotOpenUrl()
    {
        bool opened = false;
        SelfUpdateService.OpenUrl = _ => opened = true;

        var info = new UpdateInfo(new Version(2, 0, 0), "http://example.test/releases/tag/v2.0.0", null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SelfUpdateService.ApplyUpdateAsync(info, progress: null));
        Assert.False(opened);
    }

    [Fact]
    public async Task ApplyUpdateAsync_NonHttpsAssetUrl_ThrowsAndDoesNotDownload()
    {
        string exePath = Path.Combine(_root, "Sbroglione.Desktop.exe");
        File.WriteAllText(exePath, "OLD");
        SelfUpdateService.CurrentExecutablePathOverride = exePath;
        SelfUpdateService.Client = new HttpClient(new FakeDownloadHandler("NEW"));

        var info = new UpdateInfo(new Version(2, 0, 0), "https://example.test/releases/tag/v2.0.0", "http://example.test/app.exe", "Sbroglione.Desktop.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SelfUpdateService.ApplyUpdateAsync(info, progress: null));
        Assert.Equal("OLD", File.ReadAllText(exePath));
    }
}
