using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public sealed class RenameJournalStoreTests : IDisposable
{
    private readonly string _tempFile;
    private readonly string _originalPath;

    public RenameJournalStoreTests()
    {
        _originalPath = RenameJournalStore.CurrentPath;
        _tempFile = Path.Combine(Path.GetTempPath(), $"fe-tests-rename-journal-{Guid.NewGuid():N}.json");
        RenameJournalStore.CurrentPath = _tempFile;
    }

    public void Dispose()
    {
        RenameJournalStore.CurrentPath = _originalPath;
        try { File.Delete(_tempFile); } catch { /* best effort */ }
    }

    [Fact]
    public async Task LoadLastBatchAsync_NoFile_ReturnsNull()
    {
        RenameBatchRecord? result = await RenameJournalStore.LoadLastBatchAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveLastBatchAsync_ThenLoad_RoundTripsRecord()
    {
        var batch = new RenameBatchRecord(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new List<RenamedPair> { new("/tmp/a.jpg", "/tmp/photo-001.jpg") });

        await RenameJournalStore.SaveLastBatchAsync(batch);
        RenameBatchRecord? loaded = await RenameJournalStore.LoadLastBatchAsync();

        Assert.NotNull(loaded);
        Assert.Equal(batch.Id, loaded!.Id);
        Assert.Single(loaded.Renames);
        Assert.Equal("/tmp/a.jpg", loaded.Renames[0].OldPath);
    }

    [Fact]
    public async Task ClearAsync_AfterSave_LoadReturnsNull()
    {
        var batch = new RenameBatchRecord(Guid.NewGuid(), DateTime.UtcNow, new List<RenamedPair>());
        await RenameJournalStore.SaveLastBatchAsync(batch);

        await RenameJournalStore.ClearAsync();

        Assert.Null(await RenameJournalStore.LoadLastBatchAsync());
    }
}
