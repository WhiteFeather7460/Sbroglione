using System.IO;

namespace Sbroglione.Services;

public sealed class DefaultFileSystemAccessor : IFileSystemAccessor
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string[] EnumerateFileNames(string directoryPath) => Directory.GetFiles(directoryPath);

    public string[] EnumerateDirectoryNames(string directoryPath) => Directory.GetDirectories(directoryPath);
}
