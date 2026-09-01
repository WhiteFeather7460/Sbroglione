using Sbroglione;
using Sbroglione.ViewModels;
using Xunit;

namespace Sbroglione.Tests;

public class MainWindowViewModelStorageTests
{
    [Fact]
    public void IsStorageAccessGranted_DefaultsToTrue()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.IsStorageAccessGranted);
    }

    [Fact]
    public void RequestStorageAccessCommand_InvokesAppSeamWhenSet()
    {
        bool invoked = false;
        App.RequestStorageAccess = () => invoked = true;
        try
        {
            var vm = new MainWindowViewModel();
            vm.RequestStorageAccessCommand.Execute().Subscribe();

            Assert.True(invoked);
        }
        finally
        {
            App.RequestStorageAccess = null;
        }
    }
}
