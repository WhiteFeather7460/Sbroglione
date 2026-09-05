using Sbroglione.PluginContracts;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class PluginManifestReaderTests : IDisposable
{
    private readonly string _root;

    public PluginManifestReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-plugin-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string WriteManifest(string json)
    {
        string path = Path.Combine(_root, "plugin.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void TryRead_ValidManifest_ReturnsTrueAndParsedFields()
    {
        string path = WriteManifest("""
            {
              "id": "youtubedownloader",
              "displayName": "YouTube Downloader",
              "version": "1.0.0",
              "contractVersion": "1.0.0",
              "mainAssemblyFileName": "YoutubeDownloaderPlugin.dll"
            }
            """);

        bool ok = PluginManifestReader.TryRead(path, out PluginManifest? manifest);

        Assert.True(ok);
        Assert.NotNull(manifest);
        Assert.Equal("youtubedownloader", manifest!.Id);
        Assert.Equal("YouTube Downloader", manifest.DisplayName);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("1.0.0", manifest.ContractVersion);
        Assert.Equal("YoutubeDownloaderPlugin.dll", manifest.MainAssemblyFileName);
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        bool ok = PluginManifestReader.TryRead(Path.Combine(_root, "nope.json"), out PluginManifest? manifest);

        Assert.False(ok);
        Assert.Null(manifest);
    }

    [Fact]
    public void TryRead_MalformedJson_ReturnsFalse()
    {
        string path = WriteManifest("{ not valid json");

        bool ok = PluginManifestReader.TryRead(path, out PluginManifest? manifest);

        Assert.False(ok);
        Assert.Null(manifest);
    }

    [Theory]
    [InlineData("""{"id": "", "displayName": "X", "version": "1.0.0", "contractVersion": "1.0.0", "mainAssemblyFileName": "X.dll"}""")]
    [InlineData("""{"id": "x", "displayName": "", "version": "1.0.0", "contractVersion": "1.0.0", "mainAssemblyFileName": "X.dll"}""")]
    [InlineData("""{"id": "x", "displayName": "X", "version": "", "contractVersion": "1.0.0", "mainAssemblyFileName": "X.dll"}""")]
    [InlineData("""{"id": "x", "displayName": "X", "version": "1.0.0", "contractVersion": "", "mainAssemblyFileName": "X.dll"}""")]
    [InlineData("""{"id": "x", "displayName": "X", "version": "1.0.0", "contractVersion": "1.0.0", "mainAssemblyFileName": ""}""")]
    public void TryRead_MissingRequiredField_ReturnsFalse(string json)
    {
        string path = WriteManifest(json);

        bool ok = PluginManifestReader.TryRead(path, out PluginManifest? manifest);

        Assert.False(ok);
        Assert.Null(manifest);
    }
}
