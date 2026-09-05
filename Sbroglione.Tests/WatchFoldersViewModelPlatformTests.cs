using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public class WatchFoldersViewModelPlatformTests
{
    [Fact]
    public void AndroidRuntime_OnTestHost_IsNotAndroid()
    {
        // La suite xunit gira su net10.0 desktop: verifica che il wrapper rifletta
        // correttamente OperatingSystem.IsAndroid() anche fuori da un TFM Android.
        Assert.True(AndroidRuntime.IsNotAndroid);
    }

    [Fact]
    public void AndroidRuntime_OnTestHost_IsAndroidIsFalse()
    {
        Assert.False(AndroidRuntime.IsAndroid);
    }
}
