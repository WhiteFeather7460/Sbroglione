using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>Elenco diretto e ricorsivo del contenuto di una cartella.</summary>
internal static class FileSystemListingQueries
{
    internal static Task<DirectoryListingResult> ListDirectoryAsync(string path, bool directoriesOnly) =>
        Task.Run(() =>
        {
            try
            {
                var items = FileSystemService.Accessor.EnumerateEntries(path, directoriesOnly);
                return new DirectoryListingResult(items, null);
            }
            catch (Exception ex)
            {
                return new DirectoryListingResult(new List<FileSystemItem>(), FileSystemErrors.Create(ex));
            }
        });

    internal static Task<DirectoryListingResult> ListFilesRecursiveAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                var items = FileSystemService.Accessor.EnumerateEntriesRecursive(path);
                return new DirectoryListingResult(items, null);
            }
            catch (Exception ex)
            {
                return new DirectoryListingResult(new List<FileSystemItem>(), FileSystemErrors.Create(ex));
            }
        });
}
