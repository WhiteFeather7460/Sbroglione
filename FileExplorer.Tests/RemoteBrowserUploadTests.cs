using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserUploadTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserUploadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vmup-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
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
        return vm;
    }

    private string CreateSourceFile(string relativeName, string content)
    {
        string path = Path.Combine(_source, relativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task UploadFiles_UploadsSelectedLocalFiles()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        string b = CreateSourceFile("b.txt", "BBB");
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a, b });

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.True(_client.Entries.ContainsKey("/b.txt"));
        Assert.Contains("Caricati 2", vm.StatusMessage);
    }

    [Fact]
    public async Task UploadFiles_TargetsCurrentPath()
    {
        _client.AddDirectory("/docs");
        string a = CreateSourceFile("a.txt", "AAA");
        var vm = await CreateConnectedAsync();
        await vm.OpenDirectoryAsync(vm.Items.Single(i => i.Name == "docs"));

        await vm.UploadFilesAsync(new[] { a });

        Assert.True(_client.Entries.ContainsKey("/docs/a.txt"));
    }

    [Fact]
    public async Task UploadFolder_NonRecursive_TopLevelOnly()
    {
        CreateSourceFile("a.txt", "AAA");
        CreateSourceFile(Path.Combine("sub", "deep.txt"), "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = false;

        await vm.UploadFolderAsync(_source);

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.False(_client.Entries.ContainsKey("/sub/deep.txt"));
    }

    [Fact]
    public async Task UploadFolder_Recursive_PreservesStructure()
    {
        CreateSourceFile("a.txt", "AAA");
        CreateSourceFile(Path.Combine("sub", "deep.txt"), "DEEP");
        var vm = await CreateConnectedAsync();
        vm.IncludeSubfolders = true;

        await vm.UploadFolderAsync(_source);

        Assert.True(_client.Entries.ContainsKey("/a.txt"));
        Assert.True(_client.Entries.ContainsKey("/sub/deep.txt"));
    }

    [Fact]
    public async Task Upload_SkipsIdenticalRemoteFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateSourceFile("a.txt", "AAA");
        File.SetLastWriteTime(a, modified);
        _client.AddFile("/a.txt", "AAA", modified);
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains("saltati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Upload_OverwriteAlways_ReplacesIdenticalRemoteFiles()
    {
        var modified = new DateTime(2026, 6, 1, 12, 0, 0);
        string a = CreateSourceFile("a.txt", "AAA");
        File.SetLastWriteTime(a, modified);
        _client.AddFile("/a.txt", "AAA", modified);
        var vm = await CreateConnectedAsync();
        vm.UploadOverwriteAlways = true;

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains("Caricati 1", vm.StatusMessage);
    }

    [Fact]
    public async Task Upload_RefreshesListingAfterCompletion()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        var vm = await CreateConnectedAsync();

        await vm.UploadFilesAsync(new[] { a });

        Assert.Contains(vm.Items, i => i.Name == "a.txt");
    }

    [Fact]
    public async Task CancelUpload_StopsBatchAndReportsCancellation()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        var gated = new GatedUploadClient(_client);
        var vm = await CreateConnectedAsync(gated);

        var upload = vm.UploadFilesAsync(new[] { a });
        await gated.FirstUploadStarted;
        vm.CancelUpload();
        await upload;

        Assert.Equal("Caricamento annullato.", vm.StatusMessage);
        Assert.False(vm.IsUploading);
        Assert.False(_client.Entries.ContainsKey("/a.txt"));
    }

    [Fact]
    public async Task Upload_WithFailures_FreshListingErrorTakesPrecedenceOverStaleUploadError()
    {
        string a = CreateSourceFile("a.txt", "AAA");
        _client.FailingUploads.Add("/a.txt");
        var listingFailsAfterUpload = new ListingFailsAfterUploadClient(_client);
        var vm = await CreateConnectedAsync(listingFailsAfterUpload);

        await vm.UploadFilesAsync(new[] { a });

        // Il refresh successivo all'upload fallisce nell'elencare la cartella: quell'errore è
        // più recente e più rilevante del vecchio "file falliti" e non deve esserne coperto.
        Assert.Contains("Cartella non più raggiungibile", vm.ErrorMessage);
        Assert.DoesNotContain("file falliti", vm.ErrorMessage);
    }

    /// <summary>
    /// Client che inoltra tutto a un <see cref="FakeRemoteClient"/> interno, ma fa fallire
    /// <see cref="ListDirectoryAsync"/> non appena avviene un upload: simula il refresh
    /// post-upload che trova la cartella non più raggiungibile.
    /// </summary>
    private sealed class ListingFailsAfterUploadClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private bool _uploadHappened;

        public ListingFailsAfterUploadClient(FakeRemoteClient inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
        {
            if (_uploadHappened)
            {
                return Task.FromResult(new RemoteListingResult(
                    Array.Empty<RemoteItem>(),
                    new RemoteError(RemoteErrorKind.TransferFailed, "Cartella non più raggiungibile (simulato).")));
            }
            return _inner.ListDirectoryAsync(path, ct);
        }

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public async Task<RemoteError?> UploadFileAsync(
            string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
        {
            var result = await _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);
            _uploadHappened = true;
            return result;
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    /// <summary>Client che sospende gli upload finché non vengono rilasciati o annullati.</summary>
    private sealed class GatedUploadClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedUploadClient(FakeRemoteClient inner) => _inner = inner;

        public Task FirstUploadStarted => _started.Task;

        public bool IsConnected => _inner.IsConnected;

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
            => _inner.ListDirectoryAsync(path, ct);

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public async Task<RemoteError?> UploadFileAsync(
            string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
        {
            _started.TrySetResult();
            using (ct.Register(() => _gate.TrySetCanceled(ct)))
                await _gate.Task;
            return await _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
