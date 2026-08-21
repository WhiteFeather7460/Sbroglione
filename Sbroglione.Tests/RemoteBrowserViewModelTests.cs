using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

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
        var vm = new RemoteBrowserViewModel(clientFactory, store, ProfilesPath);
        vm.Profiles.Add(new ConnectionProfile { Name = "test", Host = "h", Username = "u" });
        vm.SelectedProfile = vm.Profiles[0];
        return vm;
    }

    private string ProfilesPath => Path.Combine(_root, "profiles.json");

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
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
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
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
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
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
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

    [Fact]
    public async Task DisconnectAsync_WhileListingInCorso_IsIgnored()
    {
        _client.AddDirectory("/docs");
        _client.AddFile("/docs/b.txt", "BBB");
        var gated = new GatedListingClient(_client);
        var vm = CreateViewModel(client: gated);
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        gated.BlockListings();
        var open = vm.OpenDirectoryAsync(vm.Items.First(i => i.IsDirectory));
        await vm.DisconnectAsync();             // deve essere ignorata: il client è in uso
        gated.ReleaseListings();
        await open;

        Assert.True(vm.IsConnected);
        Assert.Equal("b.txt", Assert.Single(vm.Items).Name);
    }

    [Fact]
    public async Task AcceptFingerprint_PersistsFingerprintOnDisk()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
        var vm = CreateViewModel();
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();

        _client.ConnectError = null;
        await vm.AcceptFingerprintAsync();

        var persisted = await ProfileStore.LoadAsync(ProfilesPath);
        Assert.Equal("SHA256:xyz", Assert.Single(persisted).AcceptedHostKeyFingerprint);
    }

    [Fact]
    public async Task AcceptFingerprint_AfterProfileSwitch_DoesNotWriteOnAnyProfile()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
        var vm = CreateViewModel();
        var other = new ConnectionProfile { Name = "altro", Host = "h2", Username = "u" };
        vm.Profiles.Add(other);
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        Assert.Equal("SHA256:xyz", vm.PendingFingerprint);

        vm.SelectedProfile = other;   // cambio profilo con il banner host key ancora aperto
        _client.ConnectError = null;
        await vm.AcceptFingerprintAsync();

        Assert.Null(vm.PendingFingerprint);                          // stato pending azzerato
        Assert.Null(other.AcceptedHostKeyFingerprint);               // niente TOFU sul profilo B
        Assert.Null(vm.Profiles[0].AcceptedHostKeyFingerprint);      // né sul profilo A senza conferma
        Assert.Empty(await ProfileStore.LoadAsync(ProfilesPath));
    }

    [Fact]
    public async Task LoadProfilesAsync_CalledTwice_DoesNotReloadNorResetSelection()
    {
        await ProfileStore.SaveAsync(ProfilesPath, new List<ConnectionProfile>
        {
            new() { Name = "uno", Host = "h1", Username = "u" },
            new() { Name = "due", Host = "h2", Username = "u" }
        });
        var vm = new RemoteBrowserViewModel(_ => _client, new NullCredentialStore(), ProfilesPath);

        await vm.LoadProfilesAsync();
        vm.SelectedProfile = vm.Profiles[1];

        await vm.LoadProfilesAsync();   // secondo Loaded: cambio scheda, non deve ricaricare

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal("due", vm.SelectedProfile!.Name);
    }

    [Fact]
    public async Task ConnectAsync_AuthFailedWithStoredPassword_ShowsPasswordPrompt()
    {
        var store = new FakeCredentialStore();
        var vm = CreateViewModel(store);
        store.Store(vm.SelectedProfile!.Id, "pw-obsoleta");
        _client.ConnectError = new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita.");

        await vm.ConnectAsync();

        Assert.True(vm.IsPasswordPromptVisible);   // niente vicolo cieco: si può reinserire
        Assert.False(vm.IsConnected);
        Assert.Null(vm.PasswordInput);
        Assert.Contains("reinserisci", vm.StatusMessage);
    }

    [Fact]
    public async Task ConnectAsync_KeyringWriteThrows_DoesNotPropagateAndWarnsWithoutLeakingPassword()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = CreateViewModel(new FakeCredentialStore { ThrowOnSet = true });
        vm.PasswordInput = "s3gr3t0";
        vm.SavePassword = true;

        await vm.ConnectAsync();   // l'handler della view è async void: non deve propagare

        Assert.True(vm.IsConnected);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("keyring", vm.ErrorMessage);
        Assert.DoesNotContain("s3gr3t0", vm.ErrorMessage);
        Assert.Null(vm.PasswordInput);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesProfilePersistsAndClearsKeyring()
    {
        var store = new FakeCredentialStore();
        var vm = CreateViewModel(store);
        var profile = vm.SelectedProfile!;
        await ProfileStore.SaveAsync(ProfilesPath, vm.Profiles.ToList());

        await vm.DeleteProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.Equal(profile.Id, Assert.Single(store.DeletedProfiles));
        Assert.Empty(await ProfileStore.LoadAsync(ProfilesPath));
    }

    [Fact]
    public async Task DeleteProfileAsync_KeyringFailure_StillRemovesProfile()
    {
        var vm = CreateViewModel(new FakeCredentialStore { ThrowOnDelete = true });

        await vm.DeleteProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Empty(await ProfileStore.LoadAsync(ProfilesPath));
    }

    [Fact]
    public async Task DeleteProfileAsync_ConnectedProfile_DisconnectsFirst()
    {
        _client.AddFile("/a.txt", "AAA");
        var vm = CreateViewModel(new FakeCredentialStore());
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        Assert.True(vm.IsConnected);

        await vm.DeleteProfileAsync();

        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Items);
        Assert.Empty(vm.Profiles);
    }

    [Fact]
    public async Task ConnectAsync_ProfiloCambiatoDuranteLaConnessione_SalvaLaPasswordSulProfiloOriginale()
    {
        _client.AddFile("/a.txt", "AAA");
        var store = new FakeCredentialStore();
        var gated = new GatedConnectClient(_client);
        var vm = CreateViewModel(_ => gated, store);
        var original = vm.SelectedProfile!;
        var other = new ConnectionProfile { Name = "altro", Host = "h2", Username = "u" };
        vm.Profiles.Add(other);
        vm.PasswordInput = "s3gr3t0";
        vm.SavePassword = true;

        var connect = vm.ConnectAsync();   // sospesa dentro ConnectAsync del client
        vm.SelectedProfile = other;        // cambio combo a connessione già avviata
        gated.ReleaseConnect();
        await connect;

        Assert.Same(original, gated.ConnectedProfile);              // connesso al profilo A
        Assert.Equal("s3gr3t0", await store.GetPasswordAsync(original.Id));
        Assert.Null(await store.GetPasswordAsync(other.Id));        // niente password sul profilo B
    }

    [Fact]
    public async Task ConnectAsync_ProfiloCambiatoDuranteLaConnessione_NonAccettaLaFingerprintSulNuovoProfilo()
    {
        _client.ConnectError = new RemoteError(
            RemoteErrorKind.HostKeyMismatch, "Prima connessione.", Fingerprint: "SHA256:xyz");
        var gated = new GatedConnectClient(_client);
        var vm = CreateViewModel(_ => gated, new NullCredentialStore());
        var original = vm.SelectedProfile!;
        var other = new ConnectionProfile { Name = "altro", Host = "h2", Username = "u" };
        vm.Profiles.Add(other);
        vm.PasswordInput = "pw";

        var connect = vm.ConnectAsync();
        vm.SelectedProfile = other;        // cambio combo a connessione già avviata
        gated.ReleaseConnect();
        await connect;

        Assert.Equal("SHA256:xyz", vm.PendingFingerprint);
        _client.ConnectError = null;
        await vm.AcceptFingerprintAsync();  // la fingerprint è del profilo A, selezionato è B: no-op

        Assert.Null(other.AcceptedHostKeyFingerprint);
        Assert.Null(original.AcceptedHostKeyFingerprint);
        Assert.Empty(await ProfileStore.LoadAsync(ProfilesPath));
    }

    [Fact]
    public async Task DeleteProfileAsync_DoppioClic_EliminaUnaVoltaSolaSenzaDoppioDispose()
    {
        _client.AddFile("/a.txt", "AAA");
        var gated = new GatedDisposeClient(_client);
        var store = new FakeCredentialStore();
        var vm = CreateViewModel(_ => gated, store);
        var profile = vm.SelectedProfile!;
        vm.PasswordInput = "pw";
        await vm.ConnectAsync();
        Assert.True(vm.IsConnected);

        gated.BlockDispose();
        var first = vm.DeleteProfileAsync();
        var second = vm.DeleteProfileAsync();   // secondo clic: deve cadere sulla guardia
        gated.ReleaseDispose();
        await Task.WhenAll(first, second);

        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.Equal(1, gated.DisposeCount);                        // niente doppia dispose
        Assert.Equal(profile.Id, Assert.Single(store.DeletedProfiles));
        Assert.Empty(await ProfileStore.LoadAsync(ProfilesPath));
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

        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    /// <summary>
    /// Client la cui connessione resta sospesa finché non viene rilasciata: serve a cambiare
    /// profilo mentre la connessione è ancora in volo. Registra il profilo ricevuto.
    /// </summary>
    private sealed class GatedConnectClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private readonly TaskCompletionSource _gate = new();

        public GatedConnectClient(FakeRemoteClient inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        /// <summary>Profilo passato a <see cref="ConnectAsync"/>.</summary>
        public ConnectionProfile? ConnectedProfile { get; private set; }

        public void ReleaseConnect() => _gate.SetResult();

        public async Task<RemoteError?> ConnectAsync(
            ConnectionProfile profile, string password, CancellationToken ct)
        {
            await _gate.Task;
            ConnectedProfile = profile;
            return await _inner.ConnectAsync(profile, password, ct);
        }

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
            => _inner.ListDirectoryAsync(path, ct);

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    /// <summary>Client che sospende la dispose su richiesta e conta quante volte è stata chiamata.</summary>
    private sealed class GatedDisposeClient : IRemoteFileClient
    {
        private readonly FakeRemoteClient _inner;
        private TaskCompletionSource? _gate;

        public GatedDisposeClient(FakeRemoteClient inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;

        public int DisposeCount { get; private set; }

        public void BlockDispose() => _gate = new TaskCompletionSource();

        public void ReleaseDispose()
        {
            var gate = _gate;
            _gate = null;
            gate?.SetResult();
        }

        public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
            => _inner.ConnectAsync(profile, password, ct);

        public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
            => _inner.ListDirectoryAsync(path, ct);

        public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
            => _inner.ListRecursiveAsync(path, ct);

        public Task<RemoteError?> DownloadFileAsync(
            RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.DownloadFileAsync(item, localPath, progress, ct);

        public Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
            => _inner.UploadFileAsync(localPath, remoteFullPath, progress, ct);

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            var gate = _gate;
            if (gate is not null)
                await gate.Task;
            await _inner.DisposeAsync();
        }
    }
}
