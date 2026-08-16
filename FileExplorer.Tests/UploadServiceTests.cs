using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class UploadServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRemoteClient _client = new();

    public UploadServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-up-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreateLocalFile(string relativeName, string content, DateTime? modified = null)
    {
        string path = Path.Combine(_root, relativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } m)
            File.SetLastWriteTime(path, m);
        return path;
    }

    private Task<UploadReport> RunAsync(
        IReadOnlyList<UploadEntry> entries,
        bool overwriteAlways = false,
        CancellationToken ct = default) =>
        UploadService.UploadAsync(_client, entries, "/srv", overwriteAlways, progress: null, ct);

    [Fact]
    public void CombineRemotePath_JoinsBaseAndRelative()
    {
        Assert.Equal("/srv/sub/c.txt", UploadService.CombineRemotePath("/srv", "sub/c.txt"));
    }

    [Fact]
    public void CombineRemotePath_TrimsSlashesButPreservesBackslashes()
    {
        Assert.Equal(@"/srv/sub\c.txt", UploadService.CombineRemotePath("/srv/", @"sub\c.txt"));
    }

    [Fact]
    public async Task UploadAsync_UploadsMissingFiles()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        string b = CreateLocalFile("b.txt", "BBB");

        var report = await RunAsync(new[]
        {
            new UploadEntry(a, "a.txt"),
            new UploadEntry(b, "b.txt"),
        });

        Assert.Equal(2, report.Uploaded.Count);
        Assert.Empty(report.Skipped);
        Assert.Empty(report.Failed);
        Assert.True(_client.Entries.ContainsKey("/srv/a.txt"));
        Assert.Equal("AAA", System.Text.Encoding.UTF8.GetString(_client.Entries["/srv/a.txt"].Content));
    }

    [Fact]
    public async Task UploadAsync_CreatesRemoteSubfolders()
    {
        string c = CreateLocalFile(Path.Combine("sub", "deep", "c.txt"), "CCC");

        var report = await RunAsync(new[] { new UploadEntry(c, "sub/deep/c.txt") });

        Assert.Single(report.Uploaded);
        Assert.True(_client.Entries.ContainsKey("/srv/sub/deep/c.txt"));
    }

    [Fact]
    public async Task UploadAsync_SkipsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateLocalFile("a.txt", "AAA", modified);
        _client.AddFile("/srv/a.txt", "AAA", modified);

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") });

        Assert.Empty(report.Uploaded);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public async Task UploadAsync_OverwritesDifferentFiles()
    {
        string a = CreateLocalFile("a.txt", "NUOVO CONTENUTO");
        _client.AddFile("/srv/a.txt", "vecchio");

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") });

        Assert.Single(report.Uploaded);
        Assert.Equal("NUOVO CONTENUTO", System.Text.Encoding.UTF8.GetString(_client.Entries["/srv/a.txt"].Content));
    }

    [Fact]
    public async Task UploadAsync_OverwriteAlways_UploadsPresentFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateLocalFile("a.txt", "AAA", modified);
        _client.AddFile("/srv/a.txt", "AAA", modified);

        var report = await RunAsync(new[] { new UploadEntry(a, "a.txt") }, overwriteAlways: true);

        Assert.Single(report.Uploaded);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public async Task UploadAsync_FailedFile_DoesNotStopBatch()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        string b = CreateLocalFile("b.txt", "BBB");
        _client.FailingUploads.Add("/srv/a.txt");

        var report = await RunAsync(new[]
        {
            new UploadEntry(a, "a.txt"),
            new UploadEntry(b, "b.txt"),
        });

        Assert.Single(report.Uploaded);
        Assert.Single(report.Failed);
        Assert.Equal("a.txt", Path.GetFileName(report.Failed[0].Entry.LocalPath));
        Assert.False(string.IsNullOrWhiteSpace(report.Failed[0].Reason));
    }

    [Fact]
    public async Task UploadAsync_ListingFails_TreatsAllAsMissing()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        var failingListClient = new FailingListClient();

        var report = await UploadService.UploadAsync(
            failingListClient, new[] { new UploadEntry(a, "a.txt") }, "/srv",
            overwriteAlways: false, progress: null, CancellationToken.None);

        Assert.Single(report.Uploaded);
    }

    [Fact]
    public async Task UploadAsync_Cancellation_Throws()
    {
        string a = CreateLocalFile("a.txt", "AAA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(new[] { new UploadEntry(a, "a.txt") }, ct: cts.Token));
    }

    /// <summary>Client il cui ListRecursiveAsync fallisce sempre, per testare il fallback "nessun file esistente".</summary>
    private sealed class FailingListClient : IRemoteFileClient
    {
        public bool IsConnected => true;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(Array.Empty<RemoteItem>(), null));

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct) =>
            Task.FromResult(new RemoteListingResult(
                Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, "boom")));

        public Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct) =>
            Task.FromResult<RemoteError?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
