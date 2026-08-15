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

    private RemoteBrowserViewModel CreateViewModel(ICredentialStore? store = null)
    {
        var vm = new RemoteBrowserViewModel(
            _ => _client,
            store ?? new NullCredentialStore(),
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
}
