using System.Net;
using System.Reflection;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

/// <summary>HttpMessageHandler che risponde sempre con lo stesso payload/status, per test deterministici senza rete.</summary>
file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    public FakeHttpMessageHandler(HttpStatusCode status, string content)
    {
        _status = status;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
        return Task.FromResult(response);
    }
}

public sealed class UpdateCheckServiceTests : IDisposable
{
    private readonly HttpClient _originalClient;
    private readonly Version? _originalCurrentVersion;
    private readonly string? _originalPlatformSuffix;

    public UpdateCheckServiceTests()
    {
        _originalClient = UpdateCheckService.Client;
        _originalCurrentVersion = UpdateCheckService.CurrentVersionOverride;
        _originalPlatformSuffix = UpdateCheckService.PlatformAssetSuffixOverride;
    }

    public void Dispose()
    {
        UpdateCheckService.Client = _originalClient;
        UpdateCheckService.CurrentVersionOverride = _originalCurrentVersion;
        UpdateCheckService.PlatformAssetSuffixOverride = _originalPlatformSuffix;
    }

    private static void UseFakeResponse(HttpStatusCode status, string content)
    {
        UpdateCheckService.Client = new HttpClient(new FakeHttpMessageHandler(status, content));
    }

    private const string ReleaseJsonWithAssets = """
        {
          "tag_name": "v2.0.0",
          "html_url": "https://github.com/WhiteFeather7460/Sbroglione/releases/tag/v2.0.0",
          "assets": [
            { "name": "Sbroglione.Desktop.exe", "browser_download_url": "https://example.test/Sbroglione.Desktop.exe" },
            { "name": "Sbroglione-x86_64.AppImage", "browser_download_url": "https://example.test/Sbroglione-x86_64.AppImage" }
          ]
        }
        """;

    [Fact]
    public async Task CheckAsync_NewerTagWithMatchingAsset_ReturnsAvailableWithAssetUrl()
    {
        UseFakeResponse(HttpStatusCode.OK, ReleaseJsonWithAssets);
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = ".exe";

        UpdateCheckResult result = await UpdateCheckService.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(new Version(2, 0, 0), result.Info!.Version);
        Assert.Equal("https://example.test/Sbroglione.Desktop.exe", result.Info.AssetDownloadUrl);
        Assert.Equal("Sbroglione.Desktop.exe", result.Info.AssetFileName);
    }

    [Fact]
    public async Task CheckAsync_SameOrOlderTag_ReturnsUpToDate()
    {
        UseFakeResponse(HttpStatusCode.OK, ReleaseJsonWithAssets);
        UpdateCheckService.CurrentVersionOverride = new Version(2, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = ".exe";

        UpdateCheckResult result = await UpdateCheckService.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedPlatform_ReturnsAvailableWithNullAssetUrl()
    {
        UseFakeResponse(HttpStatusCode.OK, ReleaseJsonWithAssets);
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);
        UpdateCheckService.PlatformAssetSuffixOverride = null;

        UpdateCheckResult result = await UpdateCheckService.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Null(result.Info!.AssetDownloadUrl);
        Assert.Equal("https://github.com/WhiteFeather7460/Sbroglione/releases/tag/v2.0.0", result.Info.ReleaseUrl);
    }

    [Fact]
    public async Task CheckAsync_HttpError_ReturnsError()
    {
        UseFakeResponse(HttpStatusCode.InternalServerError, "boom");
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);

        UpdateCheckResult result = await UpdateCheckService.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Error, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_MalformedTag_ReturnsError()
    {
        UseFakeResponse(HttpStatusCode.OK, """{ "tag_name": "not-a-version", "html_url": "x", "assets": [] }""");
        UpdateCheckService.CurrentVersionOverride = new Version(1, 0, 0);

        UpdateCheckResult result = await UpdateCheckService.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Error, result.Status);
    }
}
