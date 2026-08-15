namespace FileExplorer.Models;

/// <summary>Categoria di errore di un'operazione remota.</summary>
public enum RemoteErrorKind
{
    AuthFailed,
    HostUnreachable,
    Timeout,
    PermissionDenied,
    NotFound,
    TransferFailed,

    /// <summary>Host key SFTP sconosciuta o diversa da quella accettata (possibile MITM).</summary>
    HostKeyMismatch
}

/// <summary>
/// Errore remoto con messaggio presentabile. <paramref name="Fingerprint"/> è valorizzata
/// solo per <see cref="RemoteErrorKind.HostKeyMismatch"/> (fingerprint SHA-256 ricevuta).
/// </summary>
public sealed record RemoteError(RemoteErrorKind Kind, string Message, string? Fingerprint = null);
