using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root;

    public ProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string StorePath => Path.Combine(_root, "sub", "profiles.json");

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        var profiles = await ProfileStore.LoadAsync(StorePath);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsAllFields()
    {
        var profile = new ConnectionProfile
        {
            Name = "NAS",
            Host = "nas.local",
            Port = 2222,
            Username = "utente",
            Protocol = RemoteProtocol.Sftp,
            LastDestinationFolder = "/tmp/dl",
            AcceptedHostKeyFingerprint = "SHA256:abc"
        };

        await ProfileStore.SaveAsync(StorePath, new[] { profile });
        var loaded = await ProfileStore.LoadAsync(StorePath);

        var round = Assert.Single(loaded);
        Assert.Equal(profile.Id, round.Id);
        Assert.Equal("NAS", round.Name);
        Assert.Equal("nas.local", round.Host);
        Assert.Equal(2222, round.Port);
        Assert.Equal("utente", round.Username);
        Assert.Equal(RemoteProtocol.Sftp, round.Protocol);
        Assert.Equal("/tmp/dl", round.LastDestinationFolder);
        Assert.Equal("SHA256:abc", round.AcceptedHostKeyFingerprint);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, "{ non-json !!!");

        var profiles = await ProfileStore.LoadAsync(StorePath);
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_NeverWritesPasswordProperty()
    {
        await ProfileStore.SaveAsync(StorePath, new[] { new ConnectionProfile { Name = "x" } });
        string json = await File.ReadAllTextAsync(StorePath);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
