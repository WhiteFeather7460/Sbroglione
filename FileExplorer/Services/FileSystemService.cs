using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileExplorer.Models;

namespace FileExplorer.Services;

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
    /// Elenca le cartelle (ed eventualmente i file) contenuti direttamente in <paramref name="path"/>.
    /// Gli errori di accesso vengono ignorati: si ottiene un elenco parziale o vuoto.
    /// </summary>
    public static List<FileSystemItem> ListDirectory(string path, bool directoriesOnly)
    {
        var items = new List<FileSystemItem>();

        if (!Directory.Exists(path))
            return items;

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
        }
        catch
        {
            // Errori di accesso ignorati di proposito.
        }

        return items;
    }

    /// <summary>
    /// Ritorna ricorsivamente tutti i file sotto <paramref name="path"/>, ordinati per percorso completo.
    /// Gli errori di accesso producono un elenco vuoto.
    /// </summary>
    public static List<FileSystemItem> ListFilesRecursive(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => CreateFileItem(new FileInfo(file)))
                .OrderBy(item => item.FullPath)
                .ToList();
        }
        catch
        {
            return new List<FileSystemItem>();
        }
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
        LastModified = info.LastWriteTime,
        FullPath = info.FullName
    };
}
