using System;

namespace FileExplorer.Services;

/// <summary>Sceglie il backend keyring adatto al sistema operativo corrente.</summary>
public static class CredentialStoreFactory
{
    public static ICredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialStore();

        if (OperatingSystem.IsMacOS())
        {
            var mac = new MacKeychainCredentialStore();
            return mac.IsAvailable ? mac : new NullCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            var linux = new SecretToolCredentialStore();
            return linux.IsAvailable ? linux : new NullCredentialStore();
        }

        return new NullCredentialStore();
    }
}
