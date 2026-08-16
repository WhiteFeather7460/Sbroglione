using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

/// <summary>
/// Client remoto in-memory per i test: file simulati per percorso, errori
/// configurabili per connessione e per singolo download.
/// </summary>
public sealed class FakeRemoteClient : IRemoteFileClient
{
    /// <summary>Percorso remoto → (voce, contenuto). Le directory hanno contenuto vuoto.</summary>
    public Dictionary<string, (RemoteItem Item, byte[] Content)> Entries { get; } = new();

    /// <summary>Errore da ritornare alla connessione (null = successo).</summary>
    public RemoteError? ConnectError { get; set; }

    /// <summary>Percorsi remoti il cui download deve fallire.</summary>
    public HashSet<string> FailingDownloads { get; } = new();

    /// <summary>Percorsi remoti (target) il cui upload simulato deve fallire.</summary>
    public HashSet<string> FailingUploads { get; } = new();

    public bool IsConnected { get; private set; }

    public void AddFile(string fullPath, string content, DateTime? modified = null)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        string name = fullPath[(fullPath.LastIndexOf('/') + 1)..];
        var item = new RemoteItem(name, fullPath, IsDirectory: false, bytes.Length,
            modified ?? new DateTime(2026, 6, 1, 12, 0, 0));
        Entries[fullPath] = (item, bytes);
    }

    public void AddDirectory(string fullPath)
    {
        string name = fullPath.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var item = new RemoteItem(name, fullPath, IsDirectory: true, 0, new DateTime(2026, 6, 1));
        Entries[fullPath] = (item, Array.Empty<byte>());
    }

    public Task<RemoteError?> ConnectAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        IsConnected = ConnectError is null;
        return Task.FromResult(ConnectError);
    }

    public Task<RemoteListingResult> ListDirectoryAsync(string path, CancellationToken ct)
    {
        string prefix = path.TrimEnd('/') + "/";
        var items = Entries.Values
            .Select(e => e.Item)
            .Where(i => i.FullPath.StartsWith(prefix, StringComparison.Ordinal)
                        && !i.FullPath[prefix.Length..].TrimEnd('/').Contains('/'))
            .OrderBy(i => i.FullPath)
            .ToList();
        return Task.FromResult(new RemoteListingResult(items, null));
    }

    public Task<RemoteListingResult> ListRecursiveAsync(string path, CancellationToken ct)
    {
        string prefix = path.TrimEnd('/') + "/";
        var items = Entries.Values
            .Select(e => e.Item)
            .Where(i => !i.IsDirectory && i.FullPath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(i => i.FullPath)
            .ToList();
        return Task.FromResult(new RemoteListingResult(items, null));
    }

    public async Task<RemoteError?> DownloadFileAsync(RemoteItem item, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingDownloads.Contains(item.FullPath))
            return new RemoteError(RemoteErrorKind.TransferFailed, "Trasferimento fallito (simulato).");

        if (!Entries.TryGetValue(item.FullPath, out var entry))
            return new RemoteError(RemoteErrorKind.NotFound, "File remoto inesistente.");

        await File.WriteAllBytesAsync(localPath, entry.Content, ct);
        File.SetLastWriteTime(localPath, entry.Item.Modified);
        progress?.Report(entry.Content.Length);
        return null;
    }

    public async Task<RemoteError?> UploadFileAsync(string localPath, string remoteFullPath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingUploads.Contains(remoteFullPath))
            return new RemoteError(RemoteErrorKind.TransferFailed, "Trasferimento fallito (simulato).");

        byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
        string name = remoteFullPath[(remoteFullPath.LastIndexOf('/') + 1)..];
        var modified = File.GetLastWriteTime(localPath);
        Entries[remoteFullPath] = (new RemoteItem(name, remoteFullPath, false, bytes.Length, modified), bytes);
        progress?.Report(bytes.Length);
        return null;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
