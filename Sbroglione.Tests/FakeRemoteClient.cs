using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

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

    /// <summary>Percorsi remoti la cui operazione di cartella (create/delete/rename) deve fallire.</summary>
    public HashSet<string> FailingFolderOps { get; } = new();

    public Task<RemoteError?> CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingFolderOps.Contains(path))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.Generic, "Operazione fallita (simulata)."));
        if (Entries.ContainsKey(path))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.AlreadyExists, RemoteErrorMessageKeys.AlreadyExists));

        string name = path.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        Entries[path] = (new RemoteItem(name, path, true, 0, new DateTime(2026, 6, 1)), Array.Empty<byte>());
        return Task.FromResult<RemoteError?>(null);
    }

    public Task<RemoteError?> DeleteAsync(string path, bool isDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingFolderOps.Contains(path))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.Generic, "Operazione fallita (simulata)."));
        if (!Entries.Remove(path))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.NotFound, RemoteErrorMessageKeys.NotFound));

        if (isDirectory)
        {
            string prefix = path.TrimEnd('/') + "/";
            foreach (var key in Entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Entries.Remove(key);
        }
        return Task.FromResult<RemoteError?>(null);
    }

    /// <summary>
    /// Rinomina solo la voce diretta: a differenza dei client reali non ricalcola ricorsivamente
    /// i percorsi dei figli. Sufficiente per i test del ViewModel, che non rinominano cartelle
    /// non vuote — annotato qui perché è una semplificazione deliberata del double.
    /// </summary>
    public Task<RemoteError?> RenameAsync(string path, string newName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (FailingFolderOps.Contains(path))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.TransferFailed, RemoteErrorMessageKeys.Generic, "Operazione fallita (simulata)."));
        if (!Entries.TryGetValue(path, out var entry))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.NotFound, RemoteErrorMessageKeys.NotFound));

        int lastSlash = path.TrimEnd('/').LastIndexOf('/');
        string parent = lastSlash <= 0 ? "/" : path[..lastSlash];
        string newPath = parent.TrimEnd('/') + "/" + newName;
        if (Entries.ContainsKey(newPath))
            return Task.FromResult<RemoteError?>(new RemoteError(RemoteErrorKind.AlreadyExists, RemoteErrorMessageKeys.AlreadyExists));

        Entries.Remove(path);
        Entries[newPath] = (entry.Item with { Name = newName, FullPath = newPath }, entry.Content);
        return Task.FromResult<RemoteError?>(null);
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
