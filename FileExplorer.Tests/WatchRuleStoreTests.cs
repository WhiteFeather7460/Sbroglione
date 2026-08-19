using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class WatchRuleStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalCurrentPath;

    public WatchRuleStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watchrules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrentPath = WatchRuleStore.CurrentPath;
        WatchRuleStore.CurrentPath = Path.Combine(_root, "sub", "watch-rules.json");
    }

    public void Dispose()
    {
        WatchRuleStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static WatchRule CreateRule() => new()
    {
        SourcePath = "/tmp/src",
        DestinationPath = "/tmp/dst",
        Mode = WatchMode.Interval,
        IntervalMinutes = 15
    };

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRule()
    {
        WatchRule rule = CreateRule();

        await WatchRuleStore.SaveAsync(new[] { rule });
        var loaded = await WatchRuleStore.LoadAsync();

        WatchRule single = Assert.Single(loaded);
        Assert.Equal(rule.Id, single.Id);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("/tmp/dst", single.DestinationPath);
        Assert.True(single.Enabled);
        Assert.Equal(WatchMode.Interval, single.Mode);
        Assert.Equal(15, single.IntervalMinutes);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WatchRuleStore.CurrentPath)!);
        await File.WriteAllTextAsync(WatchRuleStore.CurrentPath, "{ json rotto");

        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_DiscardsRulesWithoutPaths()
    {
        var incomplete = new WatchRule { SourcePath = "", DestinationPath = "/tmp/dst" };
        var complete = CreateRule();

        await WatchRuleStore.SaveAsync(new[] { incomplete, complete });
        var loaded = await WatchRuleStore.LoadAsync();

        Assert.Equal(complete.Id, Assert.Single(loaded).Id);
    }

    [Fact]
    public async Task SaveAsync_ClampsIntervalMinutes()
    {
        WatchRule low = CreateRule();
        low.IntervalMinutes = 0;
        WatchRule high = CreateRule();
        high.IntervalMinutes = 99999;

        await WatchRuleStore.SaveAsync(new[] { low, high });
        var loaded = await WatchRuleStore.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(1, loaded[0].IntervalMinutes);
        Assert.Equal(1440, loaded[1].IntervalMinutes);
    }

    [Fact]
    public async Task Load_Sync_ReadsSavedRules()
    {
        await WatchRuleStore.SaveAsync(new[] { CreateRule() });

        Assert.Single(WatchRuleStore.Load());
    }

    [Fact]
    public void Sanitize_AssignsIdWhenMissing()
    {
        var rule = CreateRule();
        rule.Id = "";

        var sanitized = WatchRuleStore.Sanitize(new[] { rule });

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(sanitized).Id));
    }
}
