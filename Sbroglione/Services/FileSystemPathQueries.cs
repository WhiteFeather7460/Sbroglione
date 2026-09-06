using System;
using System.IO;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Query di percorso: tipo file/cartella, parent, riconoscimento e accesso UNC.</summary>
internal static class FileSystemPathQueries
{
    internal static PathType GetPathType(string? path)
    {
        if (path is null)
            return PathType.Unknown;

        if (FileSystemService.Accessor.FileExists(path))
            return PathType.File;

        if (FileSystemService.Accessor.DirectoryExists(path))
            return PathType.Directory;

        return PathType.Unknown;
    }

    internal static string? GetParentPath(string? path)
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

    internal static bool IsUncPath(string? path) =>
        path is not null && path.StartsWith(@"\\", StringComparison.Ordinal);

    internal static string? GetUncRoot(string? path)
    {
        if (!IsUncPath(path))
            return null;

        string[] segments = path!.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2 ? null : $@"\\{segments[0]}\{segments[1]}";
    }

    internal static Task<UncAccessResult> CheckUncRootAccessAsync(string uncRoot) =>
        Task.Run(() =>
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
