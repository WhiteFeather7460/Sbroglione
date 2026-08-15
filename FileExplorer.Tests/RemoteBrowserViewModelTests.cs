using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class RemoteBrowserViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRemoteClient _client = new();

    public RemoteBrowserViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private RemoteBrowserViewModel CreateViewModel(ICredentialStore? store = null, IRemoteFileClient? client = null)
        => CreateViewModel(_ => client ?? _client, store ?? new NullCredentialStore());

    private RemoteBrowserViewModel CreateViewModel(
        Func<ConnectionProfile, IRemoteFileClient> clientFactory, ICredentialStore store)
    {
        var vm = new RemoteBrowserViewModel(
            clientFactory,
            store,
            Path.Combine(_root, "profiles.json"));
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        return vm;
    }

    [Fact]
    public async Task ConnectAsync_NoStoredPassword_ShowsPasswordPrompt()
    {
        var vm = CreateViewModel();

        await vm.ConnectAsync();

        Assert.True(vm.IsPasswordPromptVisible);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithPasswordInput_ConnectsAndLists()
    {
        _client.AddFile("/a.txt", "AAA");
        _client.AddDirectory("/docs");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.True(vm.IsConnected);
        Assert.False(vm.IsPasswordPromptVisible);
        Assert.Equal("/", vm.CurrentPath);
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.Items[0].IsDirectory);          // directory prima dei file
        Assert.Equal("docs", vm.Items[0].Name);
        Assert.Equal("a.txt", vm.Items[1].Name);
    }

    [Fact]
    public async Task ConnectAsync_AuthError_SetsErrorMessage()
    {
        _client.ConnectError = new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita.");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw-sbagliata";

        await vm.ConnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Contains("Autenticazione", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ConnectAsync_HostKeyMismatch_ShowsPendingFingerprint()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";

        await vm.ConnectAsync();

        Assert.Equal("SHA256:xyz", vm.PendingFingerprint);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task AcceptFingerprint_SavesToProfileAndReconnects()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        _client.ConnectError = null; // il server ora è "fidato"
        await vm.AcceptFingerprintAsync();

        Assert.Equal("SHA256:xyz", vm.SelectedProfile!.AcceptedHostKeyFingerprint);
        Assert.Null(vm.PendingFingerprint);
        Assert.True(vm.IsConnected);
    }

    [Fact]
    public async Task RejectFingerprint_ClearsPendingAndStaysDisconnected()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        vm.RejectFingerprint();

        Assert.Null(vm.PendingFingerprint);
        Assert.False(vm.IsConnected);
        Assert.Null(vm.SelectedProfile!.AcceptedHostKeyFingerprint);
    }

    [Fact]
    public async Task OpenDirectory_NavigatesAndLists()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/b.txt", "BBB");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        await vm.OpenDirectoryAsync(vm.Items.First(i => i.IsDirectory));

        Assert.Equal("/docs", vm.CurrentPath);
        var entry = Assert.Single(vm.Items);
        Assert.Equal("b.txt", entry.Name);
    }

    [Fact]
    public async Task NavigateUp_FromSubdir_GoesToParent_AndStopsAtRoot()
    {
        _client.AddDirectory("/docs");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        await vm.OpenDirectoryAsync(vm.Items[0]);

        await vm.NavigateUpAsync();
        Assert.Equal("/", vm.CurrentPath);

        await vm.NavigateUpAsync();
        Assert.Equal("/", vm.CurrentPath); // dalla radice non si sale
    }

    [Fact]
    public async Task DisconnectAsync_ClearsState()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        await vm.DisconnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task ConnectAsync_CalledTwiceDuringKeyringLookup_CreatesOnlyOneClient()
    {
        _client.AddFile("/a.txt", "AAA");
        var store = new BlockingCredentialStore();
        int createdClients = 0;
        var vm = CreateViewModel(
            _ => { createdClients++; return _client; },
            store);

        var first = vm.ConnectAsync();          // resta in attesa della lettura dal keyring
        var second = vm.ConnectAsync();         // deve essere ignorata: operazione già in corso
        store.ReleasePassword("pw");
        await Task.WhenAll(first, second);

        Assert.Equal(1, createdClients);
        Assert.True(vm.IsConnected);
        Assert.False(vm.IsBusy);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task NavigateUp_WhileListingInCorso_IsIgnored()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/b.txt", "BBB");
        var gated = new GatedListingClient(_client);
        var vm = CreateViewModel(client: gated);
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        gated.BlockListings();
        var open = vm.OpenDirectoryAsync(vm.Items.First(i => i.IsDirectory));
        var up = vm.NavigateUpAsync();          // deve essere ignorata: elenco già in corso
        gated.ReleaseListings();
        await Task.WhenAll(open, up);

        Assert.Equal("/docs", vm.CurrentPath);
        Assert.Equal("b.txt", Assert.Single(vm.Items).Name);
        Assert.False(vm.IsBusy);
    }

    /// <summary>Keyring simulato la cui lettura si sblocca solo su richiesta esplicita.</summary>
    private sealed class BlockingCredentialStore : ICredentialStore
    {
        private readonly TaskCompletionSource<string?> _password = new();

        public bool IsAvailable => true;

        public Task<string?> GetPasswordAsync(Guid profileId) => _password.Task;

        public Task SetPasswordAsync(Guid profileId, string password) => Task.CompletedTask;

        public Task DeletePasswordAsync(Guid profileId) => Task.CompletedTask;

        public void ReleasePassword(string? password) => _password.SetResult(password);
    }

    /// <summary>Client che sospende gli elenchi finché non vengono rilasciati esplicitamente.</summary>
    private sealed class GatedListingClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private TaskCompletionSource? _gate;

        public GatedListingClient(FakeRemoteClient inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        public void BlockListings() => _gate = new TaskCompletionSource();

        public void ReleaseListings()
        {
            var gate = _gate;
            _gate = null;
            gate?.SetResult();
        }

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public async Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
        {
            var gate = _gate;
            if (gate is not null)
                await gate.Task;
            return await _inner.ListDirectoryAsync(path, ct);
        }

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
