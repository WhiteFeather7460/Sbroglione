using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DirectoryVerificationServiceTests : IDisposable
{
    private static readonly string[] ExpectedMissingSingleA = { "a.txt" };
    private readonly string _root;

    public DirectoryVerificationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(string Source, string Destination)> CreateCopiedTreeAsync()
    {
        string source = Path.Combine(_root, "src");
        string destination = Path.Combine(_root, "dst");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "contenuto a");
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "b.txt"), "contenuto b");
        await FileCopyService.CopyDirectoryAsync(source, destination, 2, null, CancellationToken.None);
        return (source, destination);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_IdenticalTrees_ReportsSuccess()
    {
        var (source, destination) = await CreateCopiedTreeAsync();

        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 2, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalFiles);
        Assert.Empty(result.MismatchedFiles);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_CorruptedDestinationFile_ReportsMismatchRelativePath()
    {
        var (source, destination) = await CreateCopiedTreeAsync();
        await File.WriteAllTextAsync(Path.Combine(destination, "sub", "b.txt"), "CORROTTO!!");

        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 2, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { Path.Combine("sub", "b.txt") }, result.MismatchedFiles);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_MissingDestinationFile_ReportsMissingAndProgress()
    {
        var (source, destination) = await CreateCopiedTreeAsync();
        File.Delete(Path.Combine(destination, "a.txt"));

        var progressEvents = new List<VerifyProgress>();
        var result = await DirectoryVerificationService.VerifyDirectoryAsync(
            source, destination, 1, progressEvents.Add, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ExpectedMissingSingleA, result.MissingFiles);
        Assert.Equal(2, progressEvents.Count);
        Assert.Equal(new VerifyProgress(2, 2), progressEvents[^1]);
    }
}
