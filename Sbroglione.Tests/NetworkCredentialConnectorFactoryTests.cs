using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class NetworkCredentialConnectorFactoryTests
{
    [Fact]
    public void Create_ReturnsSupportedConnector_OnlyOnWindows()
    {
        var connector = NetworkCredentialConnectorFactory.Create();
        Assert.Equal(OperatingSystem.IsWindows(), connector.IsSupported);
    }

    [Fact]
    public void Create_UsesOverrideFactory_WhenSet()
    {
        var fake = new FakeConnector();
        NetworkCredentialConnectorFactory.OverrideFactory = () => fake;
        try
        {
            Assert.Same(fake, NetworkCredentialConnectorFactory.Create());
        }
        finally
        {
            NetworkCredentialConnectorFactory.OverrideFactory = null;
        }
    }

    private sealed class FakeConnector : INetworkCredentialConnector
    {
        public bool IsSupported => true;
        public int Connect(string uncRoot, string username, string password, bool persist) => 0;
    }
}
