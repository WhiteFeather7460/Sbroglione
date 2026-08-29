using System;

namespace Sbroglione.Services;

/// <summary>Sceglie il connector adatto al sistema operativo corrente.</summary>
public static class NetworkCredentialConnectorFactory
{
    /// <summary>Solo per i test: sostituisce <see cref="Create"/>. Ripristinare a null in Dispose.</summary>
    internal static Func<INetworkCredentialConnector>? OverrideFactory { get; set; }

    public static INetworkCredentialConnector Create()
    {
        if (OverrideFactory is { } fake)
            return fake();

        return OperatingSystem.IsWindows()
            ? new WindowsNetworkCredentialConnector()
            : new NullNetworkCredentialConnector();
    }
}
