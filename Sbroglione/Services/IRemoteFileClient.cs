using System;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Client unificato verso un server remoto (FTP/FTPS/SFTP). Gli errori sono
/// ritornati come <see cref="RemoteError"/>, mai lanciati come eccezioni
/// (eccetto <see cref="OperationCanceledException"/> su annullamento).
/// </summary>
public interface IRemoteFileClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Connette e autentica. Null = successo.</summary>
    Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct);

    /// <summary>Elenco del contenuto diretto di <paramref name="path"/> remoto.</summary>
    Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct);

    /// <summary>Elenco ricorsivo dei soli file sotto <paramref name="path"/> remoto.</summary>
    Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct);

    /// <summary>Scarica un file remoto su <paramref name="localPath"/>. Null = successo.</summary>
    Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct);

    /// <summary>Carica un file locale su <paramref name="remoteFullPath"/>, creando le cartelle remote mancanti. Null = successo.</summary>
    Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct);
}
