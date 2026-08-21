using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Sbroglione.Services;

/// <summary>
/// Client SFTP basato su SSH.NET con verifica della host key:
/// la fingerprint SHA-256 deve corrispondere a quella accettata nel profilo.
/// </summary>
public sealed class SftpRemoteClient : IRemoteFileClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private SftpClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Valorizzate dal callback sincrono HostKeyReceived: SSH.NET non permette di
        // interrompere l'handshake con un valore di ritorno, si nega solo la fiducia
        // (CanTrust=false) e la connessione fallisce con un'eccezione. Il motivo reale
        // del fallimento va quindi trasportato fuori dal callback.
        string? receivedFingerprint = null;
        var hostKeyRejected = false;

        try
        {
            var client = new SftpClient(profile.Host, profile.Port, profile.Username, password);
            _client = client;
            client.ConnectionInfo.Timeout = ConnectTimeout;

            client.HostKeyReceived += (_, e) =>
            {
                receivedFingerprint = ComputeSha256Fingerprint(e.HostKey);
                e.CanTrust = profile.AcceptedHostKeyFingerprint is not null
                    && string.Equals(receivedFingerprint, profile.AcceptedHostKeyFingerprint, StringComparison.Ordinal);
                hostKeyRejected = !e.CanTrust;
            };

            await client.ConnectAsync(ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            DisposeClientBestEffort();
            throw;
        }
        catch (Exception) when (hostKeyRejected)
        {
            // Host key sconosciuta (primo accesso) o cambiata: mai connettersi in silenzio.
            DisposeClientBestEffort();
            string messageKey = profile.AcceptedHostKeyFingerprint is null
                ? RemoteErrorMessageKeys.HostKeyFirstConnection
                : RemoteErrorMessageKeys.HostKeyChanged;
            return new RemoteError(RemoteErrorKind.HostKeyMismatch, messageKey, profile.Host, receivedFingerprint);
        }
        catch (Exception ex)
        {
            DisposeClientBestEffort();
            return TranslateError(ex);
        }
    }

    /// <summary>Fingerprint in formato "SHA256:&lt;base64 senza padding&gt;" (stesso formato di OpenSSH).</summary>
    internal static string ComputeSha256Fingerprint(byte[] hostKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    /// <summary>Rilascia un client parzialmente connesso (fallito/annullato) evitando leak di socket.</summary>
    private void DisposeClientBestEffort()
    {
        try { _client?.Dispose(); } catch (Exception) { /* best effort */ }
        _client = null;
    }

    public async Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
    {
        if (_client is null)
            return NotConnectedResult();

        try
        {
            var items = new List<RemoteItem>();
            await foreach (var entry in _client.ListDirectoryAsync(path, ct))
            {
                if (entry.Name is "." or "..")
                    continue;
                if (!entry.IsRegularFile && !entry.IsDirectory)
                    continue;
                items.Add(new RemoteItem(
                    entry.Name,
                    entry.FullName,
                    entry.IsDirectory,
                    entry.IsDirectory ? 0 : entry.Length,
                    ToLocalTime(entry.LastWriteTime)));
            }
            return new RemoteListingResult(items, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RemoteListingResult(Array.Empty<RemoteItem>(), TranslateError(ex));
        }
    }

    public async Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
    {
        var all = new List<RemoteItem>();
        var pending = new Queue<string>();
        pending.Enqueue(path);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ListDirectoryAsync(pending.Dequeue(), ct);
            if (result.Error is not null)
                return new RemoteListingResult(Array.Empty<RemoteItem>(), result.Error);

            foreach (var item in result.Items)
            {
                if (item.IsDirectory)
                    pending.Enqueue(item.FullPath);
                else
                    all.Add(item);
            }
        }

        all.Sort((a, b) => string.CompareOrdinal(a.FullPath, b.FullPath));
        return new RemoteListingResult(all, null);
    }

    public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.NotConnected);

        try
        {
            var sftpProgress = progress is null
                ? null
                : new Progress<DownloadFileProgressReport>(p => progress.Report((long)p.TotalBytesDownloaded));

            // Lo stream va chiuso PRIMA di riscrivere la data di modifica: la Dispose
            // del FileStream aggiorna l'mtime e annullerebbe File.SetLastWriteTime.
            await using (var stream = File.Create(localPath))
            {
                await _client.DownloadFileAsync(item.FullPath, stream, sftpProgress, ct);
            }

            // Il confronto "Present"/"Different" (DownloadService.GetLocalStatus) si basa sulla
            // data di modifica: allineiamo il file locale a quella remota (già in ora locale).
            File.SetLastWriteTime(localPath, item.Modified);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TranslateError(ex);
        }
    }

    public async Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(localPath);
        ArgumentNullException.ThrowIfNull(remoteFullPath);

        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.NotConnected);

        try
        {
            int lastSlash = remoteFullPath.LastIndexOf('/');
            string remoteDir = lastSlash <= 0 ? "/" : remoteFullPath[..lastSlash];
            await EnsureRemoteDirectoryAsync(remoteDir, ct);

            var sftpProgress = progress is null
                ? null
                : new Progress<UploadFileProgressReport>(p => progress.Report((long)p.TotalBytesUploaded));

            // Lo stream va aperto in read e chiuso dopo l'upload: SSH.NET legge da qui, non serve
            // riscrivere alcuna data locale (a differenza del download, che scrive su disco).
            await using (var stream = File.OpenRead(localPath))
            {
                await _client.UploadFileAsync(stream, remoteFullPath, canOverride: true, sftpProgress, ct);
            }

            // Speculare a DownloadFileAsync: il server stampa l'orario di upload, mentre lo skip
            // "già presente e identico" (UploadService) confronta la data di modifica locale.
            // SSH.NET non offre una variante async di questa chiamata: è un singolo round-trip
            // di metadati, non un trasferimento.
            try
            {
                _client.SetLastWriteTime(remoteFullPath, File.GetLastWriteTime(localPath));
            }
            catch (Exception)
            {
                // Best effort: alcuni server SFTP rifiutano SETSTAT sull'mtime. Non deve far fallire l'upload.
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TranslateError(ex);
        }
    }

    /// <summary>
    /// Crea <paramref name="remoteDir"/> se manca, risalendo ricorsivamente i genitori mancanti:
    /// SSH.NET non offre una CreateDirectory ricorsiva ("mkdir -p") nativa.
    /// </summary>
    private async Task EnsureRemoteDirectoryAsync(string remoteDir, CancellationToken ct)
    {
        if (_client is null || remoteDir is "/" || remoteDir.Length == 0)
            return;

        if (await _client.ExistsAsync(remoteDir, ct))
            return;

        int lastSlash = remoteDir.LastIndexOf('/');
        string parent = lastSlash <= 0 ? "/" : remoteDir[..lastSlash];
        await EnsureRemoteDirectoryAsync(parent, ct);
        await _client.CreateDirectoryAsync(remoteDir, ct);
    }

    public ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { _client.Disconnect(); } catch (Exception) { /* già disconnesso */ }
            _client.Dispose();
            _client = null;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// SSH.NET espone <c>LastWriteTime</c> già in ora locale, ma normalizziamo comunque
    /// perché DownloadService confronta <see cref="RemoteItem.Modified"/> con
    /// File.GetLastWriteTime (ora locale) senza guardare il Kind.
    /// </summary>
    private static DateTime ToLocalTime(DateTime modified) =>
        modified.Kind == DateTimeKind.Utc ? modified.ToLocalTime() : modified;

    private static RemoteListingResult NotConnectedResult() =>
        new(Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.NotConnected));

    private static RemoteError TranslateError(Exception ex) => ex switch
    {
        SshAuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, RemoteErrorMessageKeys.AuthFailed),
        SftpPathNotFoundException =>
            new RemoteError(RemoteErrorKind.NotFound, RemoteErrorMessageKeys.NotFound),
        SftpPermissionDeniedException =>
            new RemoteError(RemoteErrorKind.PermissionDenied, RemoteErrorMessageKeys.PermissionDenied),
        SshOperationTimeoutException =>
            new RemoteError(RemoteErrorKind.Timeout, RemoteErrorMessageKeys.Timeout),
        SocketException =>
            new RemoteError(RemoteErrorKind.HostUnreachable, RemoteErrorMessageKeys.HostUnreachable),
        _ => new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.Generic, ex.Message)
    };
}
