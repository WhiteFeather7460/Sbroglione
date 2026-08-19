using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CopyProfileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalPath;

    public CopyProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-copyprofiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalPath = CopyProfileStore.CurrentPath;
        CopyProfileStore.CurrentPath = Path.Combine(_root, "copy-profiles.json");
    }

    public void Dispose()
    {
        CopyProfileStore.CurrentPath = _originalPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(CopyProfileStore.CurrentPath, "{ non-json");
        Assert.Empty(await CopyProfileStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProfiles()
    {
        var profile = new CopyProfile
        {
            Name = "Backup foto",
            Pairs =
            {
                new CopyProfilePair
                {
                    SourcePath = "/dati/foto",
                    DestinationPath = "/backup/foto",
                    ExtraDestinations = { "/nas/foto" },
                    SkipUnchanged = true
                }
            }
        };

        await CopyProfileStore.SaveAsync(new[] { profile });
        List<CopyProfile> loaded = await CopyProfileStore.LoadAsync();

        var restored = Assert.Single(loaded);
        Assert.Equal(profile.Id, restored.Id);
        Assert.Equal("Backup foto", restored.Name);
        var pair = Assert.Single(restored.Pairs);
        Assert.Equal("/dati/foto", pair.SourcePath);
        Assert.Equal("/backup/foto", pair.DestinationPath);
        Assert.Equal("/nas/foto", Assert.Single(pair.ExtraDestinations));
        Assert.True(pair.SkipUnchanged);
    }

    [Fact]
    public async Task LoadAsync_SortsByNameCaseInsensitive()
    {
        await CopyProfileStore.SaveAsync(new[]
        {
            new CopyProfile { Name = "zeta" },
            new CopyProfile { Name = "Alfa" },
            new CopyProfile { Name = "beta" }
        });

        List<CopyProfile> loaded = await CopyProfileStore.LoadAsync();

        Assert.Equal(new[] { "Alfa", "beta", "zeta" }, loaded.Select(p => p.Name));
    }

    [Fact]
    public void Sanitize_EmptyName_AssignsDefaultName()
    {
        var profile = new CopyProfile { Name = "   " };

        CopyProfileStore.Sanitize(profile);

        Assert.Equal("Profilo senza nome", profile.Name);
    }

    [Fact]
    public void Sanitize_PairWithoutPaths_IsRemoved()
    {
        var profile = new CopyProfile
        {
            Name = "Test",
            Pairs =
            {
                new CopyProfilePair(),
                new CopyProfilePair { SourcePath = "/src", DestinationPath = "/dst" }
            }
        };

        CopyProfileStore.Sanitize(profile);

        var pair = Assert.Single(profile.Pairs);
        Assert.Equal("/src", pair.SourcePath);
    }
}
