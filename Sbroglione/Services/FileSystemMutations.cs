using System;
using System.IO;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Operazioni che modificano il file system: creazione, rinomina, eliminazione.</summary>
internal static class FileSystemMutations
{
    internal static Task<ListingError?> CreateDirectoryAsync(string parentPath, string name) =>
        Task.Run(() =>
        {
            try
            {
                string target = Path.Combine(parentPath, name);
                if (FileSystemService.Accessor.DirectoryExists(target) || FileSystemService.Accessor.FileExists(target))
                    return new ListingError(ListingErrorKind.AlreadyExists, ListingErrorMessageKeys.AlreadyExists);

                FileSystemService.Accessor.CreateDirectory(target);
                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return FileSystemErrors.Create(ex);
            }
        });

    internal static Task<ListingError?> RenameAsync(string path, string newName) =>
        Task.Run(() =>
        {
            try
            {
                string? parent = Path.GetDirectoryName(path);
                if (parent is null)
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                string target = Path.Combine(parent, newName);
                if (FileSystemService.Accessor.DirectoryExists(target) || FileSystemService.Accessor.FileExists(target))
                    return new ListingError(ListingErrorKind.AlreadyExists, ListingErrorMessageKeys.AlreadyExists);

                if (FileSystemService.Accessor.DirectoryExists(path))
                    FileSystemService.Accessor.MoveDirectory(path, target);
                else if (FileSystemService.Accessor.FileExists(path))
                    FileSystemService.Accessor.MoveFile(path, target);
                else
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return FileSystemErrors.Create(ex);
            }
        });

    internal static Task<ListingError?> DeleteAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                if (FileSystemService.Accessor.DirectoryExists(path))
                    FileSystemService.Accessor.DeleteDirectory(path, recursive: true);
                else if (FileSystemService.Accessor.FileExists(path))
                    FileSystemService.Accessor.DeleteFile(path);
                else
                    return new ListingError(ListingErrorKind.NotFound, ListingErrorMessageKeys.NotFound);

                return (ListingError?)null;
            }
            catch (Exception ex)
            {
                return FileSystemErrors.Create(ex);
            }
        });
}
