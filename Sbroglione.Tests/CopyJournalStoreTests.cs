using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class CopyJournalStoreTests : IDisposable
{
    private static readonly string[] ExpectedExtraDestinations = { "/tmp/dst2" };
    private readonly string _root;
    private readonly string _originalCurrentPath;

    public CopyJournalStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCurrentPath = CopyJournalStore.CurrentPath;
        CopyJournalStore.CurrentPath = Path.Combine(_root, "sub", "copy-journal.json");
    }

    public void Dispose()
    {
        CopyJournalStore.CurrentPath = _originalCurrentPath;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        Assert.Empty(await CopyJournalStore.LoadAsync());
    }

    [Fact]
    public async Task AddAsync_ThenLoad_RoundTripsRecord()
    {
        var record = new CopyJobRecord
        {
            SourcePath = "/tmp/src",
            DestinationPath = "/tmp/dst",
            ExtraDestinations = { "/tmp/dst2" },
            StartedUtc = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc)
        };

        await CopyJournalStore.AddAsync(record);
        var loaded = await CopyJournalStore.LoadAsync();

        var single = Assert.Single(loaded);
        Assert.Equal(record.Id, single.Id);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal(ExpectedExtraDestinations, single.ExtraDestinations);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyMatchingRecord()
    {
        var first = new CopyJobRecord { SourcePath = "/a", DestinationPath = "/b" };
        var second = new CopyJobRecord { SourcePath = "/c", DestinationPath = "/d" };
        await CopyJournalStore.AddAsync(first);
        await CopyJournalStore.AddAsync(second);

        await CopyJournalStore.RemoveAsync(first.Id);

        var loaded = await CopyJournalStore.LoadAsync();
        Assert.Equal(second.Id, Assert.Single(loaded).Id);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CopyJournalStore.CurrentPath)!);
        await File.WriteAllTextAsync(CopyJournalStore.CurrentPath, "{ json rotto");

        Assert.Empty(await CopyJournalStore.LoadAsync());
    }
}
