using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DownloadServiceStatusTests : IDisposable
{
    private readonly string _root;

    public DownloadServiceStatusTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreateLocalFile(string name, string content, DateTime modified)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, modified);
        return path;
    }

    private static RemoteItem Remote(string name, long size, DateTime modified) =>
        new(name, "/srv/" + name, IsDirectory: false, size, modified);

    [Fact]
    public void GetLocalStatus_FileDoesNotExist_Missing()
    {
        var status = DownloadService.GetLocalStatus(
            Remote("a.txt", 5, DateTime.Now), Path.Combine(_root, "a.txt"));
        Assert.Equal(LocalFileStatus.Missing, status);
    }

    [Fact]
    public void GetLocalStatus_SameSizeAndDate_Present()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified), path);
        Assert.Equal(LocalFileStatus.Present, status);
    }

    [Fact]
    public void GetLocalStatus_DateWithinTwoSeconds_Present()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified.AddSeconds(2)), path);
        Assert.Equal(LocalFileStatus.Present, status);
    }

    [Fact]
    public void GetLocalStatus_DifferentSize_Different()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 999, modified), path);
        Assert.Equal(LocalFileStatus.Different, status);
    }

    [Fact]
    public void GetLocalStatus_DifferentDate_Different()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string path = CreateLocalFile("a.txt", "hello", modified);

        var status = DownloadService.GetLocalStatus(Remote("a.txt", 5, modified.AddMinutes(5)), path);
        Assert.Equal(LocalFileStatus.Different, status);
    }
}
