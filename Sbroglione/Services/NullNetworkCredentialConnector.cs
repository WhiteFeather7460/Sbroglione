using System;

namespace Sbroglione.Services;

/// <summary>Fallback non-Windows: mai chiamato davvero, perché IsSupported è false.</summary>
public sealed class NullNetworkCredentialConnector : INetworkCredentialConnector
{
    public bool IsSupported => false;

    public int Connect(string uncRoot, string username, string password, bool persist) =>
        throw new PlatformNotSupportedException(
            "NullNetworkCredentialConnector.Connect non va mai chiamato: controllare IsSupported prima.");
}
