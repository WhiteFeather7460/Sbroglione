using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sbroglione.Models;

namespace Sbroglione.Services;

public sealed class DefaultFileSystemAccessor : IFileSystemAccessor
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string[] EnumerateFileNames(string directoryPath) => Directory.GetFiles(directoryPath);

    public string[] EnumerateDirectoryNames(string directoryPath) => Directory.GetDirectories(directoryPath);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);

    public void MoveFile(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

    public IReadOnlyList<FileSystemItem> EnumerateEntries(string directoryPath, bool directoriesOnly)
    {
        var items = new List<FileSystemItem>();

        foreach (var directory in Directory.GetDirectories(directoryPath))
            items.Add(CreateDirectoryItem(new DirectoryInfo(directory)));

        if (!directoriesOnly)
        {
            foreach (var file in Directory.GetFiles(directoryPath))
                items.Add(CreateFileItem(new FileInfo(file)));
        }

        return items;
    }

    public IReadOnlyList<FileSystemItem> EnumerateEntriesRecursive(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Select(file => CreateFileItem(new FileInfo(file)))
            .OrderBy(item => item.FullPath)
            .ToList();

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
