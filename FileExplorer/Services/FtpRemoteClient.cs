using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Models;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileExplorer.Services;

/// <summary>Client FTP/FTPS basato su FluentFTP.</summary>
public sealed class FtpRemoteClient : IRemoteFileClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private AsyncFtpClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        try
        {
            _client = new AsyncFtpClient(profile.Host, profile.Username, password, profile.Port);
            _client.Config.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
            _client.Config.EncryptionMode = profile.Protocol == RemoteProtocol.Ftps
                ? FtpEncryptionMode.Explicit
                : FtpEncryptionMode.None;

            await _client.Connect(ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            DisposeClientBestEffort();
            throw;
        }
        catch (Exception ex)
        {
            DisposeClientBestEffort();
            return TranslateError(ex);
        }
    }

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
            var listing = await _client.GetListing(path, ct);
            var items = new List<RemoteItem>();
            foreach (var entry in listing)
            {
                if (entry.Type is not (FtpObjectType.File or FtpObjectType.Directory))
                    continue;
                items.Add(new RemoteItem(
                    entry.Name,
                    entry.FullName,
                    entry.Type == FtpObjectType.Directory,
                    entry.Size < 0 ? 0 : entry.Size,
                    ToLocalTime(entry.Modified)));
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
        if (_client is null)
            return NotConnectedResult();

        try
        {
            var listing = await _client.GetListing(path, FtpListOption.Recursive, ct);
            var items = new List<RemoteItem>();
            foreach (var entry in listing)
            {
                if (entry.Type != FtpObjectType.File)
                    continue;
                items.Add(new RemoteItem(entry.Name, entry.FullName, false,
                    entry.Size < 0 ? 0 : entry.Size, ToLocalTime(entry.Modified)));
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

    public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

        try
        {
            var ftpProgress = progress is null
                ? null
                : new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes));

            var status = await _client.DownloadFile(localPath, item.FullPath,
                FtpLocalExists.Overwrite, progress: ftpProgress, token: ct);

            if (status != FtpStatus.Success)
                return new RemoteError(RemoteErrorKind.TransferFailed, $"Download di {item.Name} non riuscito.");

            // Il confronto "Present"/"Different" (DownloadService.GetLocalStatus) si basa sulla
            // data di modifica: allineiamo il file locale a quella remota (già convertita in ora locale).
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
        if (_client is null)
            return new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso.");

        try
        {
            var ftpProgress = progress is null
                ? null
                : new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes));

            var status = await _client.UploadFile(localPath, remoteFullPath, FtpRemoteExists.Overwrite,
                createRemoteDir: true, progress: ftpProgress, token: ct);

            if (status != FtpStatus.Success)
                return new RemoteError(RemoteErrorKind.TransferFailed, $"Caricamento di {Path.GetFileName(localPath)} non riuscito.");

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

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { await _client.Disconnect(); } catch (Exception) { /* già disconnesso */ }
            _client.Dispose();
            _client = null;
        }
    }

    /// <summary>
    /// FluentFTP può ritornare l'orario di modifica in UTC (Kind=Utc) o non specificato.
    /// DownloadService confronta <see cref="RemoteItem.Modified"/> con File.GetLastWriteTime
    /// (ora locale) senza normalizzare Kind: qui convertiamo esplicitamente in ora locale.
    /// </summary>
    private static DateTime ToLocalTime(DateTime modified) =>
        modified.Kind == DateTimeKind.Utc ? modified.ToLocalTime() : modified;

    private static RemoteListingResult NotConnectedResult() =>
        new(Array.Empty<RemoteItem>(), new RemoteError(RemoteErrorKind.TransferFailed, "Non connesso."));

    private static RemoteError TranslateError(Exception ex) => ex switch
    {
        FtpAuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, "Autenticazione fallita: utente o password errati."),
        FtpSecurityNotAvailableException or AuthenticationException =>
            new RemoteError(RemoteErrorKind.AuthFailed, "Il server non supporta la cifratura richiesta (FTPS)."),
        FtpMissingObjectException =>
            new RemoteError(RemoteErrorKind.NotFound, "Percorso remoto inesistente."),
        FtpCommandException cmd when cmd.CompletionCode == "550" =>
            new RemoteError(RemoteErrorKind.PermissionDenied, "Permesso negato dal server."),
        TimeoutException =>
            new RemoteError(RemoteErrorKind.Timeout, "Timeout di connessione al server."),
        SocketException =>
            new RemoteError(RemoteErrorKind.HostUnreachable, "Server non raggiungibile."),
        _ => new RemoteError(RemoteErrorKind.TransferFailed, ex.Message)
    };
}
