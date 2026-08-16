using FileExplorer.Models;
using FileExplorer.Services;
using FileExplorer.ViewModels;

namespace FileExplorer.Tests;

public sealed class ProfileEditorViewModelTests
{
    private static ProfileEditorViewModel Create(ConnectionProfile? profile = null) =>
        new(profile ?? new ConnectionProfile(), new NullCredentialStore());

    [Fact]
    public void Validate_EmptyNameOrHost_Fails()
    {
        var vm = Create();
        vm.Name = "";
        vm.Host = "host";
        Assert.False(vm.Validate());
        Assert.NotNull(vm.ValidationError);

        vm.Name = "nome";
        vm.Host = "";
        Assert.False(vm.Validate());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("")]
    public void Validate_InvalidPort_Fails(string port)
    {
        var vm = Create();
        vm.Name = "nome";
        vm.Host = "host";
        vm.PortText = port;
        Assert.False(vm.Validate());
    }

    [Fact]
    public void Validate_ValidInput_Passes()
    {
        var vm = Create();
        vm.Name = "nome";
        vm.Host = "host";
        vm.PortText = "2222";
        Assert.True(vm.Validate());
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void Protocol_Switch_UpdatesDefaultPort()
    {
        var vm = Create(new ConnectionProfile { Protocol = RemoteProtocol.Sftp, Port = 22 });
        Assert.Equal("22", vm.PortText);

        vm.Protocol = RemoteProtocol.Ftp;
        Assert.Equal("21", vm.PortText);

        vm.Protocol = RemoteProtocol.Sftp;
        Assert.Equal("22", vm.PortText);
    }

    [Fact]
    public void Protocol_Switch_KeepsCustomPort()
    {
        var vm = Create(new ConnectionProfile { Protocol = RemoteProtocol.Sftp, Port = 2222 });
        vm.Protocol = RemoteProtocol.Ftp;
        Assert.Equal("2222", vm.PortText); // porta personalizzata: non toccata
    }

    [Fact]
    public void ShowFtpWarning_OnlyForPlainFtp()
    {
        var vm = Create();
        vm.Protocol = RemoteProtocol.Ftp;
        Assert.True(vm.ShowFtpWarning);
        vm.Protocol = RemoteProtocol.Ftps;
        Assert.False(vm.ShowFtpWarning);
        vm.Protocol = RemoteProtocol.Sftp;
        Assert.False(vm.ShowFtpWarning);
    }

    [Fact]
    public async Task SaveAsync_AppliesFieldsToProfile()
    {
        var profile = new ConnectionProfile();
        var vm = Create(profile);
        vm.Name = "NAS";
        vm.Host = "nas.local";
        vm.PortText = "2222";
        vm.Username = "utente";
        vm.Protocol = RemoteProtocol.Sftp;

        Assert.True(await vm.SaveAsync());

        Assert.Equal("NAS", profile.Name);
        Assert.Equal("nas.local", profile.Host);
        Assert.Equal(2222, profile.Port);
        Assert.Equal("utente", profile.Username);
        Assert.Equal(RemoteProtocol.Sftp, profile.Protocol);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public async Task SaveAsync_KeyringWriteThrows_ReturnsFalseWithoutLeakingPassword()
    {
        var profile = new ConnectionProfile();
        var vm = new ProfileEditorViewModel(profile, new FakeCredentialStore { ThrowOnSet = true });
        vm.Name = "NAS";
        vm.Host = "nas.local";
        vm.PortText = "22";
        vm.Password = "s3gr3t0";

        // L'handler del pulsante Salva è async void: un'eccezione qui farebbe cadere il processo.
        Assert.False(await vm.SaveAsync());

        Assert.NotNull(vm.ValidationError);
        Assert.DoesNotContain("s3gr3t0", vm.ValidationError);
        Assert.Equal("NAS", profile.Name);   // i campi del profilo restano applicati
    }
}
