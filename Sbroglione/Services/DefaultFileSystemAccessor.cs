using System.IO;

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
}
