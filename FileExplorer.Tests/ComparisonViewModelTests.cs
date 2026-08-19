// FileExplorer.Tests/ComparisonViewModelTests.cs
using System;
using System.IO;
using System.Threading.Tasks;
using FileExplorer.Services;
using FileExplorer.ViewModels;
using Xunit;

namespace FileExplorer.Tests;

public sealed class ComparisonViewModelTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "fe-comparevm-" + Guid.NewGuid().ToString("N"));
    private readonly string _left;
    private readonly string _right;

    public ComparisonViewModelTests()
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
    public async Task CompareAsync_PopulatesCountsAndStatus()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_right, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_left, "b.txt"), "solo sx");

        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };

        await viewModel.CompareAsync();

        Assert.True(viewModel.HasResult);
        Assert.Equal(1, viewModel.IdenticalCount);
        Assert.Equal(1, viewModel.LeftOnlyCount);
        Assert.Equal(0, viewModel.DifferentCount);
        Assert.False(viewModel.IsComparing);
        Assert.Contains("1 identici", viewModel.StatusText);
    }

    [Fact]
    public async Task CompareAsync_InvalidPaths_SetsErrorStatus()
    {
        using var viewModel = new ComparisonViewModel { LeftPath = null, RightPath = _right };

        await viewModel.CompareAsync();

        Assert.False(viewModel.HasResult);
        Assert.Contains("Selezionare", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportAsync_WritesFileInTargetDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.CompareAsync();

        string exportDir = Path.Combine(_tempDir, "export");
        Directory.CreateDirectory(exportDir);

        string? written = await viewModel.ExportAsync(ComparisonReportFormat.Csv, exportDir);

        Assert.NotNull(written);
        Assert.True(File.Exists(written));
        Assert.Contains("solo-a-sinistra;a.txt", await File.ReadAllTextAsync(written!));
    }

    [Fact]
    public async Task ExportAsync_UsesPathsCapturedAtCompareTime()
    {
        await File.WriteAllTextAsync(Path.Combine(_left, "a.txt"), "1");
        using var viewModel = new ComparisonViewModel { LeftPath = _left, RightPath = _right };
        await viewModel.CompareAsync();

        // L'utente cambia i path dopo il confronto: l'export deve usare quelli confrontati.
        viewModel.LeftPath = "/altro/path";
        viewModel.RightPath = null;

        string exportDir = Path.Combine(_tempDir, "export2");
        Directory.CreateDirectory(exportDir);
        string? written = await viewModel.ExportAsync(ComparisonReportFormat.Json, exportDir);

        Assert.NotNull(written);
        string json = await File.ReadAllTextAsync(written!);
        Assert.Contains(_left.Replace("\\", "\\\\"), json);
        Assert.DoesNotContain("/altro/path", json);
    }

    [Fact]
    public async Task CompareFilesAsync_IdenticalFiles_SetsAreIdenticalAndStatus()
    {
        string leftFile = Path.Combine(_left, "f.bin");
        string rightFile = Path.Combine(_right, "f.bin");
        await File.WriteAllBytesAsync(leftFile, new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(rightFile, new byte[] { 1, 2, 3, 4 });

        using var viewModel = new ComparisonViewModel { LeftFilePath = leftFile, RightFilePath = rightFile };

        await viewModel.CompareFilesAsync();

        Assert.True(viewModel.HasFileResult);
        Assert.True(viewModel.FileResult!.AreIdentical);
        Assert.False(viewModel.IsFileComparing);
        Assert.Equal("File identici", viewModel.FileCompareStatus);
        Assert.Equal("Nessuna differenza", viewModel.FirstDiffText);
        Assert.Equal("100 % identico", viewModel.IdenticalPercentText);
        Assert.Equal("0 intervalli differenti", viewModel.RangeCountText);
        Assert.Equal("Lunghezze: 4 byte vs 4 byte", viewModel.LengthsText);
    }

    [Fact]
    public async Task CompareFilesAsync_DifferentFiles_ReportsOffsetAndPercent()
    {
        string leftFile = Path.Combine(_left, "f.bin");
        string rightFile = Path.Combine(_right, "f.bin");
        // 10 byte, prefisso identico di 6 → primo diverso a offset 6, 60 % identico.
        await File.WriteAllBytesAsync(leftFile, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        await File.WriteAllBytesAsync(rightFile, new byte[] { 0, 1, 2, 3, 4, 5 });

        using var viewModel = new ComparisonViewModel { LeftFilePath = leftFile, RightFilePath = rightFile };

        await viewModel.CompareFilesAsync();

        Assert.True(viewModel.HasFileResult);
        Assert.False(viewModel.FileResult!.AreIdentical);
        Assert.Equal("Primo byte diverso: offset 6 (0x6)", viewModel.FirstDiffText);
        Assert.Equal("60 % identico", viewModel.IdenticalPercentText);
        Assert.Equal("1 intervallo differente", viewModel.RangeCountText);
        Assert.Equal("Lunghezze: 10 byte vs 6 byte (lunghezze diverse)", viewModel.LengthsText);
        Assert.Contains("diversi", viewModel.FileCompareStatus);
    }

    [Fact]
    public async Task CompareFilesAsync_NearIdenticalLargeFiles_PercentClampedBelow100()
    {
        // 100.000 byte, un solo byte diverso: 99.999/100.000 = 99,999 % che arrotonderebbe
        // a "100 % identico" con {0:0.##} se non venisse clampato quando AreIdentical è false.
        string leftFile = Path.Combine(_left, "big.bin");
        string rightFile = Path.Combine(_right, "big.bin");
        byte[] left = new byte[100_000];
        byte[] right = new byte[100_000];
        right[50_000] = 1;
        await File.WriteAllBytesAsync(leftFile, left);
        await File.WriteAllBytesAsync(rightFile, right);

        using var viewModel = new ComparisonViewModel { LeftFilePath = leftFile, RightFilePath = rightFile };

        await viewModel.CompareFilesAsync();

        Assert.True(viewModel.HasFileResult);
        Assert.False(viewModel.FileResult!.AreIdentical);
        Assert.Equal("99,99 % identico", viewModel.IdenticalPercentText);
    }

    [Fact]
    public async Task CompareFilesAsync_InvalidPaths_SetsErrorStatus()
    {
        using var viewModel = new ComparisonViewModel
        {
            LeftFilePath = Path.Combine(_left, "manca.bin"),
            RightFilePath = null
        };

        await viewModel.CompareFilesAsync();

        Assert.False(viewModel.HasFileResult);
        Assert.Contains("Selezionare", viewModel.FileCompareStatus);
    }
}
