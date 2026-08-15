using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserDownloadTests : IDisposable
{
    private readonly string _root;
    private readonly string _dest;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserDownloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vmdl-" + Guid.NewGuid().ToString("N"));
        _dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<RemoteBrowserViewModel> CreateConnectedAsync(IRemoteFileClient? client = null)
    {
        var vm = new RemoteBrowserViewModel(
            _ => client ?? _client, new NullCredentialStore(), Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        vm.DestinationFolder = _dest;
        return vm;
    }

    [Fact]
    public async Task VisibleItems_FilterPattern_HidesNonMatching_KeepsDirectories()
    {
        _client.AddFile("/a.jpg", "IMG");
        _client.AddFile("/b.txt", "TXT");
        _client.AddDirectory("/docs");
        var vm = await CreateConnectedAsync();

        vm.FilterPattern = "*.jpg";

        Assert.Equal(2, vm.VisibleItems.Count); // docs + a.jpg
        Assert.Contains(vm.VisibleItems, i => i.Name == "docs");
        Assert.Contains(vm.VisibleItems, i => i.Name == "a.jpg");
    }

    [Fact]
    public async Task RefreshLocalStatuses_MarksPresentFile()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/a.txt", "AAA", modified);
        string local = Path.Combine(_dest, "a.txt");
        await File.WriteAllTextAsync(local, "AAA");
        File.SetLastWriteTime(local, modified);

        var vm = await CreateConnectedAsync();

        var entry = vm.Items.Single(i => i.Name == "a.txt");
        Assert.Equal(LocalFileStatus.Present, entry.LocalStatus);
    }

    [Fact]
    public async Task DownloadSelected_DownloadsOnlySelection()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddFile("/b.txt", "BBB");
        var vm = await CreateConnectedAsync();

        var selection = vm.Items.Where(i => i.Name == "a.txt").ToList();
        await vm.DownloadSelectedAsync(selection);

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_dest, "b.txt")));
        Assert.Contains("Scaricati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadSelected_DirectoryWithSubfolders_DownloadsRecursively()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/sub1.txt", "S1");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        var selection = vm.Items.Where(i => i.IsDirectory).ToList();
        await vm.DownloadSelectedAsync(selection);

        Assert.True(File.Exists(Path.Combine(_dest, "docs", "sub1.txt")));
    }

    [Fact]
    public async Task DownloadSelected_DirectoryWithoutSubfolders_IsIgnored()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/sub1.txt", "S1");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = false;

        var selection = vm.Items.Where(i => i.IsDirectory).ToList();
        await vm.DownloadSelectedAsync(selection);

        Assert.False(File.Exists(Path.Combine(_dest, "docs", "sub1.txt")));
        Assert.Contains("Scaricati 0", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadCurrentDirectory_NonRecursive_TopLevelOnly()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/deep.txt", "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = false;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_dest, "docs", "deep.txt")));
    }

    [Fact]
    public async Task DownloadCurrentDirectory_Recursive_IncludesSubfolders()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/deep.txt", "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_dest, "docs", "deep.txt")));
    }

    [Fact]
    public async Task DownloadCurrentDirectory_AppliesFilter()
    {
        _client.AddFile("/a.jpg", "IMG");
        _client.AddFile("/b.txt", "TXT");
        var vm = await CreateConnectedAsync();
        vm.FilterPattern = "*.jpg";

        await vm.DownloadCurrentDirectoryAsync();

        Assert.True(File.Exists(Path.Combine(_dest, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(_dest, "b.txt")));
    }

    [Fact]
    public async Task Download_ReportsSkippedInStatusMessage()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        _client.AddFile("/a.txt", "AAA", modified);
        string local = Path.Combine(_dest, "a.txt");
        await File.WriteAllTextAsync(local, "AAA");
        File.SetLastWriteTime(local, modified);
        var vm = await CreateConnectedAsync();

        await vm.DownloadCurrentDirectoryAsync();

        Assert.Contains("saltati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Download_SetsDestinationOnProfile()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        Assert.Equal(_dest, vm.SelectedProfile!.LastDestinationFolder);
    }

    [Fact]
    public async Task Connect_RestoresLastDestinationFolderFromProfile()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = new RemoteBrowserViewModel(
            _ => _client, new NullCredentialStore(), Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile
        {
            Name = "test",
            Host = "h",
            Username = "u",
            LastDestinationFolder = _dest
        });
        vm.SelectedProfile = vm.Profiles[0];
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.Equal(_dest, vm.DestinationFolder);
    }

    [Fact]
    public async Task Download_PersistsProfilesWithDestination()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        await vm.DownloadCurrentDirectoryAsync();

        var saved = await ProfileStore.LoadAsync(Path.Combine(_root, "profiles.json"));
        Assert.Equal(_dest, Assert.Single(saved).LastDestinationFolder);
    }

    [Fact]
    public async Task DownloadWithoutDestination_SetsError()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();
        vm.DestinationFolder = null;

        await vm.DownloadCurrentDirectoryAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task FilterMinSizeKb_NonNumeric_Ignored()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        vm.FilterMinSizeKb = "abc"; // non numerico: nessun filtro applicato

        Assert.Single(vm.VisibleItems);
    }

    [Fact]
    public async Task CancelDownload_StopsBatchAndReportsCancellation()
    {
        _client.AddFile("/a.txt", "AAA");
        var gated = new GatedDownloadClient(_client);
        var vm = await CreateConnectedAsync(gated);

        var download = vm.DownloadCurrentDirectoryAsync();
        await gated.FirstDownloadStarted;
        vm.CancelDownload();
        await download;

        Assert.Equal("Download annullato.", vm.StatusMessage);
        Assert.False(vm.IsDownloading);
        Assert.False(File.Exists(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task NavigateDuranteDownload_IsIgnored()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        var gated = new GatedDownloadClient(_client);
        var vm = await CreateConnectedAsync(gated);

        var download = vm.DownloadCurrentDirectoryAsync();
        await gated.FirstDownloadStarted;
        await vm.OpenDirectoryAsync(vm.Items.First(i => i.IsDirectory)); // ignorata: download in corso
        Assert.Equal("/", vm.CurrentPath);

        await vm.DisconnectAsync();                                     // ignorata per lo stesso motivo
        Assert.True(vm.IsConnected);

        gated.ReleaseDownloads();
        await download;

        Assert.True(File.Exists(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public async Task Disconnect_ClearsVisibleItems()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        var vm = await CreateConnectedAsync();
        Assert.Equal(2, vm.VisibleItems.Count);

        await vm.DisconnectAsync();

        Assert.Empty(vm.VisibleItems);   // è la collezione mostrata dalla lista
        Assert.Empty(vm.Items);
    }

    /// <summary>Client che sospende i download finché non vengono rilasciati o annullati.</summary>
    private sealed class GatedDownloadClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedDownloadClient(FakeRemoteClient inner) => _inner = inner;

        /// <summary>Completa quando il primo download è entrato nel client.</summary>
        public Task FirstDownloadStarted => _started.Task;

        public void ReleaseDownloads() => _gate.TrySetResult();

        public bool IsConnected => _inner.IsConnected;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
            => _inner.ListDirectoryAsync(path, ct);

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public async Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
        {
            _started.TrySetResult();
            using (ct.Register(() => _gate.TrySetCanceled(ct)))
                await _gate.Task;
            return await _inner.DownloadFileAsync(item, localPath, progress, ct);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
