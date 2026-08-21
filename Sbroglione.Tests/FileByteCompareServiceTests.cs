using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class FileByteCompareServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fe-bytecmp-" + Guid.NewGuid().ToString("N"));

    public FileByteCompareServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<string> WriteFileAsync(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    [Fact]
    public async Task CompareAsync_IdenticalFiles_AreIdentical()
    {
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };
        string left = await WriteFileAsync("l.bin", data);
        string right = await WriteFileAsync("r.bin", data);

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.True(result.AreIdentical);
        Assert.Null(result.FirstDifferenceOffset);
        Assert.Equal(8, result.IdenticalBytes);
        Assert.Empty(result.DifferentRanges);
        Assert.False(result.RangesTruncated);
        Assert.Equal(1.0, result.IdenticalFraction);
    }

    [Fact]
    public async Task CompareAsync_SingleByteDifference_ReportsOffsetAndSingleRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 3, 4, 99, 6, 7, 8, 9 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.False(result.AreIdentical);
        Assert.Equal(5, result.FirstDifferenceOffset);
        Assert.Equal(9, result.IdenticalBytes);
        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(5, 1), range);
    }

    [Fact]
    public async Task CompareAsync_ContiguousDifferences_MergedIntoOneRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 90, 91, 92, 93, 7 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(3, 4), range);
        Assert.Equal(4, result.IdenticalBytes);
    }

    [Fact]
    public async Task CompareAsync_DifferenceAcrossBlockBoundary_MergedIntoOneRange()
    {
        // bufferSize = 4: la differenza (offset 2..5) attraversa il confine tra blocco 0 e blocco 1.
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 82, 83, 84, 85, 6, 7 });

        var result = await FileByteCompareService.CompareAsync(
            left, right, null, CancellationToken.None, bufferSize: 4);

        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(2, 4), range);
        Assert.Equal(2, result.FirstDifferenceOffset);
    }

    [Fact]
    public async Task CompareAsync_DifferentLengths_TailIsSingleRange()
    {
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        string right = await WriteFileAsync("r.bin", new byte[] { 0, 1, 2, 3, 4, 5 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.False(result.AreIdentical);
        Assert.Equal(6, result.FirstDifferenceOffset);
        Assert.Equal(6, result.IdenticalBytes);
        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(6, 4), range);
        Assert.Equal(0.6, result.IdenticalFraction, precision: 10);
    }

    [Fact]
    public async Task CompareAsync_EmptyFiles_AreIdenticalWithFractionOne()
    {
        string left = await WriteFileAsync("l.bin", Array.Empty<byte>());
        string right = await WriteFileAsync("r.bin", Array.Empty<byte>());

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.True(result.AreIdentical);
        Assert.Equal(1.0, result.IdenticalFraction);
        Assert.Empty(result.DifferentRanges);
    }

    [Fact]
    public async Task CompareAsync_LeftEmptyRightNonEmpty_TailIsSingleRangeFromZero()
    {
        string left = await WriteFileAsync("l.bin", Array.Empty<byte>());
        string right = await WriteFileAsync("r.bin", new byte[] { 1, 2, 3, 4, 5 });

        var result = await FileByteCompareService.CompareAsync(left, right, null, CancellationToken.None);

        Assert.False(result.AreIdentical);
        Assert.Equal(0, result.FirstDifferenceOffset);
        Assert.Equal(0, result.IdenticalBytes);
        var range = Assert.Single(result.DifferentRanges);
        Assert.Equal(new ByteRangeDiff(0, 5), range);
        Assert.Equal(0.0, result.IdenticalFraction);
    }

    [Fact]
    public async Task CompareAsync_MaxRangesExceeded_SetsTruncatedButKeepsCounts()
    {
        // Differenze alternate agli offset 0, 2, 4, 6 → 4 intervalli, maxRanges = 2.
        string left = await WriteFileAsync("l.bin", new byte[] { 0, 1, 0, 1, 0, 1, 0, 1 });
        string right = await WriteFileAsync("r.bin", new byte[] { 9, 1, 9, 1, 9, 1, 9, 1 });

        var result = await FileByteCompareService.CompareAsync(
            left, right, null, CancellationToken.None, maxRanges: 2);

        Assert.True(result.RangesTruncated);
        Assert.Equal(2, result.DifferentRanges.Count);
        Assert.Equal(new[] { new ByteRangeDiff(0, 1), new ByteRangeDiff(2, 1) }, result.DifferentRanges.ToArray());
        Assert.Equal(0, result.FirstDifferenceOffset);
        Assert.Equal(4, result.IdenticalBytes);
    }

    [Fact]
    public async Task CompareAsync_ReportsBlockProgress()
    {
        string left = await WriteFileAsync("l.bin", new byte[10]);
        string right = await WriteFileAsync("r.bin", new byte[10]);

        var seen = new System.Collections.Generic.List<CompareProgress>();
        await FileByteCompareService.CompareAsync(
            left, right, p => { lock (seen) seen.Add(p); }, CancellationToken.None, bufferSize: 4);

        // 10 byte / blocchi da 4 → Total = 3; prima invocazione (0,3), ultima (3,3).
        Assert.Equal(new CompareProgress(0, 3), seen.First());
        Assert.Equal(new CompareProgress(3, 3), seen.Last());
    }

    [Fact]
    public async Task CompareAsync_CancelledToken_ThrowsOperationCanceled()
    {
        string left = await WriteFileAsync("l.bin", new byte[1024]);
        string right = await WriteFileAsync("r.bin", new byte[1024]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileByteCompareService.CompareAsync(left, right, null, cts.Token));
    }
}
