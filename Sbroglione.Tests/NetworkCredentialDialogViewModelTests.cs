using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class NetworkCredentialDialogViewModelTests
{
    [Fact]
    public void CanConfirm_FalseUntilBothUsernameAndPasswordSet()
    {
        var vm = new NetworkCredentialDialogViewModel(@"\\server\share");

        Assert.Equal(@"\\server\share", vm.Server);
        Assert.False(vm.CanConfirm);

        vm.Username = "alice";
        Assert.False(vm.CanConfirm);

        vm.Password = "secret";
        Assert.True(vm.CanConfirm);

        vm.Password = "";
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void Remember_DefaultsFalse()
    {
        var vm = new NetworkCredentialDialogViewModel(@"\\server\share");
        Assert.False(vm.Remember);
    }
}
