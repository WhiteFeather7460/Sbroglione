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
}
