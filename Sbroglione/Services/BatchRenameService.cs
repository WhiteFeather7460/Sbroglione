using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

public sealed record BatchRenameFailure(string Path, string Error);

public sealed record BatchRenameResult(int SuccessCount, IReadOnlyList<BatchRenameFailure> Failures);

/// <summary>
/// Esegue/annulla un piano di rinomina calcolato da <see cref="BatchRenameEngine"/>,
/// passando sempre per la facciata <see cref="FileSystemService"/> per la rinomina
/// effettiva e registrando l'esito su <see cref="RenameJournalStore"/> per l'undo.
/// </summary>
public static class BatchRenameService
{
    public static async Task<BatchRenameResult> ExecuteAsync(IReadOnlyList<RenamePlanItem> plan)
    {
        var succeeded = new List<RenamedPair>();
        var failures = new List<BatchRenameFailure>();

        foreach (RenamePlanItem item in plan)
        {
            if (item.HasConflict || item.Error is not null)
                continue;

            ListingError? error = await FileSystemService.RenameAsync(item.OriginalPath, item.NewName);
            if (error is null)
                succeeded.Add(new RenamedPair(item.OriginalPath, item.NewPath));
            else
                failures.Add(new BatchRenameFailure(item.OriginalPath, DescribeError(error)));
        }

        if (succeeded.Count > 0)
        {
            await RenameJournalStore.SaveLastBatchAsync(
                new RenameBatchRecord(Guid.NewGuid(), DateTime.UtcNow, succeeded));
        }

        return new BatchRenameResult(succeeded.Count, failures);
    }

    public static async Task<BatchRenameResult> UndoLastBatchAsync()
    {
        RenameBatchRecord? batch = await RenameJournalStore.LoadLastBatchAsync();
        if (batch is null)
            return new BatchRenameResult(0, Array.Empty<BatchRenameFailure>());

        var remaining = new List<RenamedPair>();
        int successCount = 0;

        foreach (RenamedPair pair in batch.Renames.Reverse())
        {
            string originalName = Path.GetFileName(pair.OldPath);
            ListingError? error = await FileSystemService.RenameAsync(pair.NewPath, originalName);
            if (error is null)
                successCount++;
            else
                remaining.Add(pair);
        }

        if (remaining.Count == 0)
            await RenameJournalStore.ClearAsync();
        else
            await RenameJournalStore.SaveLastBatchAsync(batch with { Renames = remaining });

        var failures = remaining.Select(p => new BatchRenameFailure(p.NewPath, "Rename back failed")).ToList();
        return new BatchRenameResult(successCount, failures);
    }

    private static string DescribeError(ListingError error) => error.Detail ?? error.MessageKey;
}
