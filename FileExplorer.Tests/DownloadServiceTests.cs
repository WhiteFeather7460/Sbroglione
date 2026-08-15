using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class DownloadServiceTests : IDisposable
{
    private readonly string _dest;
    private readonly FakeRemoteClient _client = new();

    public DownloadServiceTests()
    {
        _dest = Path.Combine(Path.GetTempPath(), "fe-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dest, recursive: true); } catch { /* best effort */ }
    }

    private Task<DownloadReport> RunAsync(
        IReadOnlyList<RemoteItem> files,
        DownloadFilter? filter = null,
        bool overwriteAlways = false,
        CancellationToken ct = default) =>
        DownloadService.DownloadAsync(
            _client, files, "/srv", _dest, filter ?? new DownloadFilter(),
            overwriteAlways, progress: null, ct);

    private List<RemoteItem> AllRemoteFiles() =>
        _client.Entries.Values.Select(e => e.Item).Where(i => !i.IsDirectory).ToList();

    [Fact]
    public void GetRelativeLocalPath_StripsBaseAndConvertsSeparators()
    {
        var item = new RemoteItem("c.txt", "/srv/sub/c.txt", false, 1, DateTime.Now);
        string expected = Path.Combine("sub", "c.txt");
        Assert.Equal(expected, DownloadService.GetRelativeLocalPath(item, "/srv"));
    }

    [Fact]
    public void GetRelativeLocalPath_OutsideBase_FallsBackToName()
    {
        var item = new RemoteItem("c.txt", "/altro/c.txt", false, 1, DateTime.Now);
        Assert.Equal("c.txt", DownloadService.GetRelativeLocalPath(item, "/srv"));
    }

    [Fact]
    public async Task DownloadAsync_DownloadsMissingFiles()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        _client.AddFile("/srv/b.txt", "BBB");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Equal(2, report.Downloaded.Count);
        Assert.Empty(report.Skipped);
        Assert.Empty(report.Failed);
        Assert.Equal("AAA", await File.ReadAllTextAsync(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task DownloadAsync_RecreatesSubfolders()
    {
        _client.AddFile("/srv/sub/deep/c.txt", "CCC");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Equal("CCC", await File.ReadAllTextAsync(Path.Combine(_dest, "sub", "deep", "c.txt")));
    }

    [Fact]
    public async Task DownloadAsync_SkipsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/srv/a.txt", "AAA", modified);
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "AAA");
        File.SetLastWriteTime(Path.Combine(_dest, "a.txt"), modified);

        var report = await RunAsync(AllRemoteFiles());

        Assert.Empty(report.Downloaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_OverwritesDifferentFiles()
    {
        _client.AddFile("/srv/a.txt", "NUOVO CONTENUTO");
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "vecchio");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Equal("NUOVO CONTENUTO", await File.ReadAllTextAsync(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task DownloadAsync_OverwriteAlways_DownloadsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/srv/a.txt", "AAA", modified);
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "AAA");
        File.SetLastWriteTime(Path.Combine(_dest, "a.txt"), modified);

        var report = await RunAsync(AllRemoteFiles(), overwriteAlways: true);

        Assert.Single(report.Downloaded);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_OnlyMissing_SkipsDifferentToo()
    {
        _client.AddFile("/srv/a.txt", "NUOVO");
        await File.WriteAllTextAsync(Path.Combine(_dest, "a.txt"), "vecchio diverso");

        var report = await RunAsync(AllRemoteFiles(), new DownloadFilter { OnlyMissing = true });

        Assert.Empty(report.Downloaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task DownloadAsync_FilterExcluded_GoesToSkipped()
    {
        _client.AddFile("/srv/a.jpg", "IMG");
        _client.AddFile("/srv/b.txt", "TXT");

        var report = await RunAsync(AllRemoteFiles(), new DownloadFilter { NamePattern = "*.jpg" });

        Assert.Single(report.Downloaded);
        Assert.Single(report.Skipped);
        Assert.Equal("a.jpg", report.Downloaded[0].Name);
    }

    [Fact]
    public async Task DownloadAsync_FailedFile_DoesNotStopBatch()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        _client.AddFile("/srv/b.txt", "BBB");
        _client.FailingDownloads.Add("/srv/a.txt");

        var report = await RunAsync(AllRemoteFiles());

        Assert.Single(report.Downloaded);
        Assert.Single(report.Failed);
        Assert.Equal("a.txt", report.Failed[0].Item.Name);
        Assert.False(string.IsNullOrWhiteSpace(report.Failed[0].Reason));
    }

    [Fact]
    public async Task DownloadAsync_IgnoresDirectoriesInList()
    {
        _client.AddDirectory("/srv/sub");
        _client.AddFile("/srv/a.txt", "AAA");
        var all = _client.Entries.Values.Select(e => e.Item).ToList();

        var report = await RunAsync(all);

        Assert.Single(report.Downloaded);
    }

    [Fact]
    public async Task DownloadAsync_Cancellation_Throws()
    {
        _client.AddFile("/srv/a.txt", "AAA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(AllRemoteFiles(), ct: cts.Token));
    }

    [Fact]
    public async Task DownloadAsync_CancellationDuringTransfer_DeletesPartialFile()
    {
        var item = new RemoteItem("a.txt", "/srv/a.txt", IsDirectory: false, 100, new DateTime(2026, 6, 1, 12, 0, 0));
        using var cts = new CancellationTokenSource();
        var client = new CancellingRemoteClient(cts);
        string localPath = Path.Combine(_dest, "a.txt");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DownloadService.DownloadAsync(
                client, new[] { item }, "/srv", _dest, new DownloadFilter(),
                overwriteAlways: false, progress: null, cts.Token));

        Assert.False(File.Exists(localPath));
    }

    /// <summary>Client che scrive un file parziale e poi annulla, per verificare la pulizia.</summary>
    private sealed class CancellingRemoteClient(CancellationTokenSource cts) : IRemoteFileClient
    {
        public bool IsConnected => true;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(Array.Empty<RemoteItem>(), null));

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(Array.Empty<RemoteItem>(), null));

        public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
        {
            await File.WriteAllTextAsync(localPath, "parziale", CancellationToken.None);
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
