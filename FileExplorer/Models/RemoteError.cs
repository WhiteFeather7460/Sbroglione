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
/// Identificatori stabili e indipendenti dalla lingua per <see cref="RemoteError.MessageKey"/> e
/// per <see cref="DownloadFailure.MessageKey"/>/<see cref="UploadFailure.MessageKey"/>: più
/// granulari di <see cref="RemoteErrorKind"/> (es. due varianti diverse di AuthFailed su FTP/FTPS).
/// La traduzione avviene al confine ViewModel (<c>RemoteBrowserViewModel</c>), mai nei client
/// Sftp/Ftp né in DownloadService/UploadService — stesso pattern di
/// <see cref="WatchFolderService.StatusSyncing"/> e soci.
/// </summary>
public static class RemoteErrorMessageKeys
{
    public const string NotConnected = "NotConnected";
    public const string AuthFailed = "AuthFailed";
    public const string FtpsNotSupported = "FtpsNotSupported";
    public const string NotFound = "NotFound";
    public const string PermissionDenied = "PermissionDenied";
    public const string Timeout = "Timeout";
    public const string HostUnreachable = "HostUnreachable";

    /// <summary>Detail: host. Primo contatto con un server mai visto prima.</summary>
    public const string HostKeyFirstConnection = "HostKeyFirstConnection";

    /// <summary>Detail: host. Host key nota ma diversa da quella accettata (possibile attacco).</summary>
    public const string HostKeyChanged = "HostKeyChanged";

    /// <summary>Detail: nome del file remoto (FTP: esito diverso da FtpStatus.Success).</summary>
    public const string DownloadFailed = "DownloadFailed";

    /// <summary>Detail: nome del file locale (FTP: esito diverso da FtpStatus.Success).</summary>
    public const string UploadFailed = "UploadFailed";

    /// <summary>Detail: messaggio dell'eccezione nel sostituire il file locale scaricato.</summary>
    public const string LocalReplaceFailed = "LocalReplaceFailed";

    /// <summary>
    /// Detail: <c>ex.Message</c> di un'eccezione non riconosciuta (I/O, framework). Dinamico e già
    /// in linguaggio naturale (prodotto dal runtime/OS, non hardcoded da noi): il confine lo mostra
    /// così com'è, senza ulteriore traduzione — stesso trattamento di <c>Str.Common.ErrorFormat</c>.
    /// </summary>
    public const string Generic = "Generic";
}

/// <summary>
/// Errore remoto. <paramref name="MessageKey"/> è un identificatore stabile (vedi
/// <see cref="RemoteErrorMessageKeys"/>), <paramref name="Detail"/> il dato dinamico associato
/// (percorso, messaggio d'eccezione). <paramref name="Fingerprint"/> è valorizzata solo per
/// <see cref="RemoteErrorKind.HostKeyMismatch"/> (fingerprint SHA-256 ricevuta).
/// </summary>
public sealed record RemoteError(RemoteErrorKind Kind, string MessageKey, string? Detail = null, string? Fingerprint = null);
