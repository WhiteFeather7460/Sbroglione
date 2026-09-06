using System;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Facciata pubblica delle operazioni sul file system. Implementazione divisa per
/// responsabilità in <see cref="FileSystemPathQueries"/>, <see cref="FileSystemListingQueries"/>,
/// <see cref="FileSystemMutations"/> ed <see cref="FileSystemErrors"/>: questa classe resta
/// l'unico punto pubblico chiamato da ViewModels/Views, per non propagare il dettaglio
/// dello split ai chiamanti.
/// </summary>
public static class FileSystemService
{
    /// <summary>
    /// Accessor sostituibile nei test (stesso pattern di
    /// <see cref="UpdateCheckService.CurrentVersionOverride"/>). Produzione: <see cref="DefaultFileSystemAccessor"/>.
    /// </summary>
    public static IFileSystemAccessor Accessor { get; set; } = new DefaultFileSystemAccessor();

    public static PathType GetPathType(string? path) => FileSystemPathQueries.GetPathType(path);

    public static string? GetParentPath(string? path) => FileSystemPathQueries.GetParentPath(path);

    public static Task<PathType> GetPathTypeAsync(string? path) =>
        Task.Run(() => GetPathType(path));

    public static Task<DirectoryListingResult> ListDirectoryAsync(string path, bool directoriesOnly) =>
        FileSystemListingQueries.ListDirectoryAsync(path, directoriesOnly);

    public static Task<DirectoryListingResult> ListFilesRecursiveAsync(string path) =>
        FileSystemListingQueries.ListFilesRecursiveAsync(path);

    public static Task<ListingError?> CreateDirectoryAsync(string parentPath, string name) =>
        FileSystemMutations.CreateDirectoryAsync(parentPath, name);

    public static Task<ListingError?> RenameAsync(string path, string newName) =>
        FileSystemMutations.RenameAsync(path, newName);

    public static Task<ListingError?> DeleteAsync(string path) =>
        FileSystemMutations.DeleteAsync(path);

    public static bool IsUncPath(string? path) => FileSystemPathQueries.IsUncPath(path);

    public static string? GetUncRoot(string? path) => FileSystemPathQueries.GetUncRoot(path);

    /// <summary>Solo per i test: sostituisce <see cref="CheckUncRootAccessAsync"/>. Ripristinare a null in Dispose.</summary>
    internal static Func<string, Task<UncAccessResult>>? CheckUncRootAccessOverride { get; set; }

    public static Task<UncAccessResult> CheckUncRootAccessAsync(string uncRoot) =>
        CheckUncRootAccessOverride is { } fake ? fake(uncRoot) : FileSystemPathQueries.CheckUncRootAccessAsync(uncRoot);

    public static ListingError CreateListingError(Exception exception) => FileSystemErrors.Create(exception);
}
