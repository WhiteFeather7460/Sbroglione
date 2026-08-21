using System;

namespace Sbroglione.Models;

/// <summary>
/// Profilo di connessione salvato su disco. Non contiene MAI la password:
/// quella vive nel keyring del sistema operativo, indicizzata da <see cref="Id"/>.
/// </summary>
public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.Sftp;

    /// <summary>Ultima cartella di destinazione scelta per i download.</summary>
    public string? LastDestinationFolder { get; set; }

    /// <summary>Fingerprint SHA-256 della host key SFTP accettata dall'utente.</summary>
    public string? AcceptedHostKeyFingerprint { get; set; }
}
