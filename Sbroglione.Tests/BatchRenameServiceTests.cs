using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public sealed class BatchRenameServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _journalPath;
    private readonly string _originalJournalPath;

    public BatchRenameServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _originalJournalPath = RenameJournalStore.CurrentPath;
        _journalPath = Path.Combine(_tempDir, "rename-journal.json");
        RenameJournalStore.CurrentPath = _journalPath;
    }

    public void Dispose()
    {
        RenameJournalStore.CurrentPath = _originalJournalPath;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ExecuteAsync_ValidPlan_RenamesFilesAndRecordsJournal()
    {
        string filePath = Path.Combine(_tempDir, "old.txt");
        File.WriteAllText(filePath, "content");
        var plan = new List<RenamePlanItem>
        {
            new(filePath, "old.txt", "new.txt", Path.Combine(_tempDir, "new.txt"), HasConflict: false, Error: null),
        };

        BatchRenameResult result = await BatchRenameService.ExecuteAsync(plan);

        Assert.Equal(1, result.SuccessCount);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(_tempDir, "new.txt")));
        Assert.False(File.Exists(filePath));

        RenameBatchRecord? journal = await RenameJournalStore.LoadLastBatchAsync();
        Assert.NotNull(journal);
        Assert.Single(journal!.Renames);
    }

    [Fact]
    public async Task ExecuteAsync_ConflictingItem_IsSkipped()
    {
        string filePath = Path.Combine(_tempDir, "old.txt");
        File.WriteAllText(filePath, "content");
        var plan = new List<RenamePlanItem>
        {
            new(filePath, "old.txt", "new.txt", Path.Combine(_tempDir, "new.txt"), HasConflict: true, Error: null),
        };

        BatchRenameResult result = await BatchRenameService.ExecuteAsync(plan);

        Assert.Equal(0, result.SuccessCount);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task UndoLastBatchAsync_AfterExecute_RestoresOriginalNames()
    {
        string filePath = Path.Combine(_tempDir, "old.txt");
        File.WriteAllText(filePath, "content");
        var plan = new List<RenamePlanItem>
        {
            new(filePath, "old.txt", "new.txt", Path.Combine(_tempDir, "new.txt"), HasConflict: false, Error: null),
        };
        await BatchRenameService.ExecuteAsync(plan);

        BatchRenameResult undoResult = await BatchRenameService.UndoLastBatchAsync();

        Assert.Equal(1, undoResult.SuccessCount);
        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists(Path.Combine(_tempDir, "new.txt")));
        Assert.Null(await RenameJournalStore.LoadLastBatchAsync());
    }

    [Fact]
    public async Task UndoLastBatchAsync_NoJournal_ReturnsZeroSuccessesNoFailures()
    {
        BatchRenameResult result = await BatchRenameService.UndoLastBatchAsync();

        Assert.Equal(0, result.SuccessCount);
        Assert.Empty(result.Failures);
    }
}
