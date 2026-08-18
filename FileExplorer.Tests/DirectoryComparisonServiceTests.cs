using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class DirectoryComparisonServiceTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-compare-" + Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public DirectoryComparisonServiceTests()
    {
        _left = Path.Combine(_tempDir, "left");
        _right = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task CompareAsync_ClassifiesAllFourCategories()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "only-left.txt"), "L");
        await File.WriteAllTextAsync(Path.Combine(_right, "only-right.txt"), "R");
        await File.WriteAllTextAsync(Path.Combine(_left, "same.txt"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_right, "same.txt"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_left, "diff.txt"), "AAAA");
        await File.WriteAllTextAsync(Path.Combine(_right, "diff.txt"), "BBBB");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, maxDegreeOfParallelism: 2, onProgress: null, CancellationToken.None);

        string[] expectedLeftOnly = { "only-left.txt" };
        string[] expectedRightOnly = { "only-right.txt" };
        string[] expectedDifferent = { "diff.txt" };
        string[] expectedIdentical = { "same.txt" };
        Assert.Equal(expectedLeftOnly, result.LeftOnly);
        Assert.Equal(expectedRightOnly, result.RightOnly);
        Assert.Equal(expectedDifferent, result.Different);
        Assert.Equal(expectedIdentical, result.Identical);
    }

    [Fact]
    public async Task CompareAsync_SameSizeDifferentContent_IsDifferent()
    {
        // Stessa dimensione: il confronto deve arrivare all'hash.
        await File.WriteAllTextAsync(Path.Combine(_left, "tricky.txt"), "AAAA");
        await File.WriteAllTextAsync(Path.Combine(_right, "tricky.txt"), "ZZZZ");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, CancellationToken.None);

        string[] expectedDifferent = { "tricky.txt" };
        Assert.Equal(expectedDifferent, result.Different);
        Assert.Empty(result.Identical);
    }

    [Fact]
    public async Task CompareAsync_NestedPaths_UseRelativePaths()
    {
        Directory.CreateDirectory(Path.Combine(_left, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_left, "sub", "deep.txt"), "X");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, CancellationToken.None);

        string[] expectedLeftOnly = { Path.Combine("sub", "deep.txt") };
        Assert.Equal(expectedLeftOnly, result.LeftOnly);
    }

    [Fact]
    public async Task CompareAsync_ReportsProgress()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_right, "a.txt"), "1");

        int lastProcessed = 0;
        var progressLock = new object();
        await DirectoryComparisonService.CompareAsync(_left, _right, 1,
            progress => { lock (progressLock) lastProcessed = progress.Processed; },
            CancellationToken.None);

        Assert.Equal(1, lastProcessed);
    }

    [Fact]
    public async Task CompareAsync_CaseInsensitiveComparer_MatchesDifferentCase()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "Same.TXT"), "uguale");
        await File.WriteAllTextAsync(Path.Combine(_right, "same.txt"), "uguale");

        var result = await DirectoryComparisonService.CompareAsync(
            _left, _right, 1, null, StringComparer.OrdinalIgnoreCase, CancellationToken.None);

        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
        Assert.Single(result.Identical);
    }

    [Fact]
    public void DefaultPathComparer_MatchesPlatform()
    {
        bool caseInsensitiveFs = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var comparer = DirectoryComparisonService.DefaultPathComparer;

        Assert.Equal(caseInsensitiveFs, comparer.Equals("A.TXT", "a.txt"));
    }
}
