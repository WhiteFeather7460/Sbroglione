using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Interrogazioni sul file system: tipo di percorso, elenchi di file/cartelle, navigazione.
/// </summary>
public static class FileSystemService
{
    /// <summary>
    /// Ritorna il tipo di elemento corrispondente al percorso.
    /// </summary>
    public static PathType GetPathType(string? path)
    {
        if (File.Exists(path))
            return PathType.File;

        if (Directory.Exists(path))
            return PathType.Directory;

        return PathType.Unknown;
    }

    /// <summary>
    /// Ritorna il percorso della cartella superiore, o il percorso stesso se si è già alla radice.
    /// </summary>
    public static string? GetParentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string? parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent) ? path : parent;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// Variante asincrona di <see cref="GetPathType"/> (il controllo di esistenza su
    /// percorsi di rete irraggiungibili può bloccare per diversi secondi).
    /// </summary>
    public static Task<PathType> GetPathTypeAsync(string? path) =>
        Task.Run(() => GetPathType(path));

    /// <summary>
    /// Elenco asincrono del contenuto diretto di <paramref name="path"/>, con errore esplicito.
    /// </summary>
    public static Task<DirectoryListingResult> ListDirectoryAsync(string path, bool directoriesOnly) =>
        Task.Run(() =>
        {
            var items = new List<FileSystemItem>();

            try
            {
                foreach (var directory in Directory.GetDirectories(path))
                {
                    items.Add(CreateDirectoryItem(new DirectoryInfo(directory)));
                }

                if (!directoriesOnly)
                {
                    foreach (var file in Directory.GetFiles(path))
                    {
                        items.Add(CreateFileItem(new FileInfo(file)));
                    }
                }

                return new DirectoryListingResult(items, null);
            }
            catch (Exception ex)
            {
                return new DirectoryListingResult(new List<FileSystemItem>(), CreateListingError(ex));
            }
        });

    /// <summary>
    /// Elenco asincrono ricorsivo dei file sotto <paramref name="path"/>, con errore esplicito.
    /// </summary>
    public static Task<DirectoryListingResult> ListFilesRecursiveAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                var items = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Select(file => CreateFileItem(new FileInfo(file)))
                    .OrderBy(item => item.FullPath)
                    .ToList();

                return new DirectoryListingResult(items, null);
            }
            catch (Exception ex)
            {
                return new DirectoryListingResult(new List<FileSystemItem>(), CreateListingError(ex));
            }
        });

    /// <summary>
    /// Crea la sottocartella <paramref name="name"/> dentro <paramref name="parentPath"/>. Null = successo.
    /// </summary>
    public static Task<ListingError?> CreateDirectoryAsync(string parentPath, string name) =>
        Task.Run(() =>
        {
            try
            {
                string target = Path.Combine(parentPath, name);
                if (Directory.Exists(target) || File.Exists(target))
                    return new ListingError(ListingErrorKind.AlreadyExists, ListingErrorMessageKeys.AlreadyExists);

                Directory.CreateDirectory(target);
                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return CreateListingError(ex);
            }
        });

    /// <summary>
    /// Rinomina il file o la cartella in <paramref name="path"/> in <paramref name="newName"/>,
    /// restando nella stessa cartella padre. Null = successo.
    /// </summary>
    public static Task<ListingError?> RenameAsync(string path, string newName) =>
        Task.Run(() =>
        {
            try
            {
                string? parent = Path.GetDirectoryName(path);
                if (parent is null)
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                string target = Path.Combine(parent, newName);
                if (Directory.Exists(target) || File.Exists(target))
                    return new ListingError(ListingErrorKind.AlreadyExists, ListingErrorMessageKeys.AlreadyExists);

                if (Directory.Exists(path))
                    Directory.Move(path, target);
                else if (File.Exists(path))
                    File.Move(path, target);
                else
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return CreateListingError(ex);
            }
        });

    /// <summary>
    /// Elimina il file o la cartella (ricorsivamente) in <paramref name="path"/>. Null = successo.
    /// Nessun cestino: cancellazione diretta, la conferma va chiesta prima dal chiamante.
    /// </summary>
    public static Task<ListingError?> DeleteAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
                else
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return CreateListingError(ex);
            }
        });

    /// <summary>
    /// True se il percorso è in forma UNC (<c>\\server\condivisione</c>).
    /// </summary>
    public static bool IsUncPath(string? path) =>
        path is not null && path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// Estrae la radice UNC (<c>\\server\condivisione</c>, senza sottocartelle) da un
    /// percorso UNC. Null se <paramref name="path"/> non è UNC o non ha almeno server+condivisione
    /// (es. <c>\\server</c> da solo).
    /// </summary>
    public static string? GetUncRoot(string? path)
    {
        if (!IsUncPath(path))
            return null;

        string[] segments = path!.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2 ? null : $@"\\{segments[0]}\{segments[1]}";
    }

    /// <summary>Solo per i test: sostituisce <see cref="CheckUncRootAccessAsync"/>. Ripristinare a null in Dispose.</summary>
    internal static Func<string, Task<UncAccessResult>>? CheckUncRootAccessOverride { get; set; }

    /// <summary>
    /// Prova ad accedere alla radice UNC (che esiste sempre come cartella, a differenza di un
    /// eventuale sottopercorso di destinazione non ancora creato): distingue un accesso negato
    /// (credenziali mancanti/scadute) da qualunque altro problema, cosa che <c>Directory.Exists</c>
    /// non permette (ritorna false in entrambi i casi).
    /// </summary>
    public static Task<UncAccessResult> CheckUncRootAccessAsync(string uncRoot)
    {
        if (CheckUncRootAccessOverride is { } fake)
            return fake(uncRoot);

        return Task.Run(() =>
        {
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(uncRoot).GetEnumerator();
                entries.MoveNext();
                return UncAccessResult.Ok;
            }
            catch (UnauthorizedAccessException)
            {
                return UncAccessResult.AccessDenied;
            }
            catch (Exception)
            {
                return UncAccessResult.Unavailable;
            }
        });
    }

    /// <summary>
    /// Traduce un'eccezione di I/O in un <see cref="ListingError"/> presentabile.
    /// </summary>
    public static ListingError CreateListingError(Exception exception) => exception switch
    {
        DirectoryNotFoundException or FileNotFoundException =>
            new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound),
        UnauthorizedAccessException =>
            new ListingError(ListingErrorKind.AccessDenied, ListingErrorMessageKeys.AccessDenied),
        IOException =>
            new ListingError(ListingErrorKind.Unavailable, ListingErrorMessageKeys.Unavailable, exception.Message),
        _ => new ListingError(ListingErrorKind.Unavailable, ListingErrorMessageKeys.Generic, exception.Message)
    };

    private static FileSystemItem CreateDirectoryItem(DirectoryInfo info) => new()
    {
        Name = info.Name,
        IsDirectory = true,
        Size = "",
        LastModified = info.LastWriteTime,
        FullPath = info.FullName
    };

    private static FileSystemItem CreateFileItem(FileInfo info) => new()
    {
        Name = info.Name,
        IsDirectory = false,
        Size = $"{info.Length / 1024} KB",
        SizeBytes = info.Length,
        LastModified = info.LastWriteTime,
        FullPath = info.FullName
    };
}
