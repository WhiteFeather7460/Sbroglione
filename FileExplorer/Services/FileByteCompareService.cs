using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Intervallo di byte differenti tra due file (offset assoluto e lunghezza).</summary>
public sealed record ByteRangeDiff(long Offset, long Length);

/// <summary>
/// Esito del confronto binario di due file: primo offset diverso, byte identici
/// nella zona sovrapposta, intervalli differenti (eventualmente troncati).
/// La coda oltre il file più corto conta come un unico intervallo differente.
/// </summary>
public sealed record FileCompareResult(
    long LeftLength,
    long RightLength,
    long? FirstDifferenceOffset,
    long IdenticalBytes,
    IReadOnlyList<ByteRangeDiff> DifferentRanges,
    bool RangesTruncated)
{
    /// <summary>Frazione identica rispetto al file più lungo (1.0 per due file vuoti).</summary>
    public double IdenticalFraction =>
        Math.Max(LeftLength, RightLength) == 0
            ? 1.0
            : (double)IdenticalBytes / Math.Max(LeftLength, RightLength);

    /// <summary>Vero se i due file sono byte-per-byte identici.</summary>
    public bool AreIdentical => FirstDifferenceOffset is null && LeftLength == RightLength;
}

/// <summary>
/// Confronto binario in streaming di due file, a blocchi. Gli intervalli differenti
/// contigui vengono uniti anche attraverso i confini di blocco; oltre
/// <c>maxRanges</c> intervalli l'elenco è troncato ma i contatori restano esatti.
/// Il progresso è in blocchi: Total = ceil(max(len) / bufferSize).
/// </summary>
public static class FileByteCompareService
{
    private const int DefaultBufferSize = 1024 * 1024;

    public static async Task<FileCompareResult> CompareAsync(
        string leftPath,
        string rightPath,
        Action<CompareProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize,
        int maxRanges = 256)
    {
        ct.ThrowIfCancellationRequested();

        var left = new FileStream(
            leftPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var leftScope = left.ConfigureAwait(false);
        var right = new FileStream(
            rightPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var rightScope = right.ConfigureAwait(false);

        long leftLength = left.Length;
        long rightLength = right.Length;
        long minLength = Math.Min(leftLength, rightLength);
        long maxLength = Math.Max(leftLength, rightLength);
        int totalBlocks = (int)((maxLength + bufferSize - 1) / bufferSize);

        byte[] leftBuffer = new byte[bufferSize];
        byte[] rightBuffer = new byte[bufferSize];

        long? firstDifference = null;
        long identicalBytes = 0;
        var ranges = new List<ByteRangeDiff>();
        bool truncated = false;
        long openRangeStart = -1; // inizio dell'intervallo differente aperto, -1 = nessuno

        long position = 0;
        int processedBlocks = 0;
        onProgress?.Invoke(new CompareProgress(0, totalBlocks));

        while (position < minLength)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(bufferSize, minLength - position);
            await left.ReadAtLeastAsync(leftBuffer.AsMemory(0, toRead), toRead, throwOnEndOfStream: true, ct)
                .ConfigureAwait(false);
            await right.ReadAtLeastAsync(rightBuffer.AsMemory(0, toRead), toRead, throwOnEndOfStream: true, ct)
                .ConfigureAwait(false);

            if (openRangeStart < 0 && leftBuffer.AsSpan(0, toRead).SequenceEqual(rightBuffer.AsSpan(0, toRead)))
            {
                // Fast path: blocco interamente identico e nessun intervallo aperto.
                identicalBytes += toRead;
            }
            else
            {
                for (int i = 0; i < toRead; i++)
                {
                    if (leftBuffer[i] == rightBuffer[i])
                    {
                        identicalBytes++;
                        if (openRangeStart >= 0)
                        {
                            CloseRange(ranges, openRangeStart, position + i, maxRanges, ref truncated);
                            openRangeStart = -1;
                        }
                    }
                    else
                    {
                        firstDifference ??= position + i;
                        if (openRangeStart < 0)
                            openRangeStart = position + i;
                    }
                }
            }

            position += toRead;
            processedBlocks++;
            onProgress?.Invoke(new CompareProgress(processedBlocks, totalBlocks));
        }

        if (maxLength > minLength)
        {
            // Coda oltre il file più corto: unico intervallo differente
            // (fuso con l'eventuale intervallo aperto che termina a minLength).
            firstDifference ??= minLength;
            if (openRangeStart < 0)
                openRangeStart = minLength;
            CloseRange(ranges, openRangeStart, maxLength, maxRanges, ref truncated);
            openRangeStart = -1;

            if (processedBlocks < totalBlocks)
            {
                processedBlocks = totalBlocks;
                onProgress?.Invoke(new CompareProgress(processedBlocks, totalBlocks));
            }
        }
        else if (openRangeStart >= 0)
        {
            CloseRange(ranges, openRangeStart, minLength, maxRanges, ref truncated);
            openRangeStart = -1;
        }

        return new FileCompareResult(
            leftLength, rightLength, firstDifference, identicalBytes, ranges, truncated);
    }

    private static void CloseRange(
        List<ByteRangeDiff> ranges, long start, long endExclusive, int maxRanges, ref bool truncated)
    {
        if (ranges.Count >= maxRanges)
        {
            truncated = true;
            return;
        }

        ranges.Add(new ByteRangeDiff(start, endExclusive - start));
    }
}
