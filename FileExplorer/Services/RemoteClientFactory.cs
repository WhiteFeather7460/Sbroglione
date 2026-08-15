using System;
using FileExplorer.Models;

namespace FileExplorer.Services;

/// <summary>Crea il client giusto per il protocollo del profilo.</summary>
public static class RemoteClientFactory
{
    public static IRemoteFileClient Create(ConnectionProfile profile) => profile.Protocol switch
    {
        RemoteProtocol.Ftp or RemoteProtocol.Ftps => new FtpRemoteClient(),
        RemoteProtocol.Sftp => new SftpRemoteClient(),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
}
