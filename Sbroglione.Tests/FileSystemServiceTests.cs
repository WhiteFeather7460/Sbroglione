using System.Runtime.InteropServices;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _root;

    public FileSystemServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public async Task ListDirectoryAsync_ReturnsDirectoriesAndFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");

        var result = await FileSystemService.ListDirectoryAsync(_root, directoriesOnly: false);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.IsDirectory && i.Name == "sub");
        Assert.Contains(result.Items, i => !i.IsDirectory && i.Name == "a.txt");
    }

    [Fact]
    public async Task ListDirectoryAsync_DirectoriesOnly_ExcludesFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");

        var result = await FileSystemService.ListDirectoryAsync(_root, directoriesOnly: true);

        Assert.Null(result.Error);
        var item = Assert.Single(result.Items);
        Assert.True(item.IsDirectory);
    }

    [Fact]
    public async Task ListDirectoryAsync_MissingPath_ReportsNotFound()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        var result = await FileSystemService.ListDirectoryAsync(missing, directoriesOnly: false);

        Assert.Empty(result.Items);
        Assert.NotNull(result.Error);
        Assert.Equal(ListingErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task ListDirectoryAsync_AccessDenied_ReportsAccessDenied()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
            return; // chmod-based denial non applicabile.

        string denied = Path.Combine(_root, "denied");
        Directory.CreateDirectory(denied);
        File.SetUnixFileMode(denied, UnixFileMode.None);

        try
        {
            var result = await FileSystemService.ListDirectoryAsync(denied, directoriesOnly: false);

            Assert.Empty(result.Items);
            Assert.NotNull(result.Error);
            Assert.Equal(ListingErrorKind.AccessDenied, result.Error!.Kind);
        }
        finally
        {
            File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ListFilesRecursiveAsync_ReturnsNestedFilesOrderedByPath()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "sub", "a.txt"), "a");

        var result = await FileSystemService.ListFilesRecursiveAsync(_root);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(result.Items.OrderBy(i => i.FullPath).Select(i => i.FullPath), result.Items.Select(i => i.FullPath));
    }

    [Fact]
    public async Task ListFilesRecursiveAsync_MissingPath_ReportsNotFound()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        var result = await FileSystemService.ListFilesRecursiveAsync(missing);

        Assert.Empty(result.Items);
        Assert.NotNull(result.Error);
        Assert.Equal(ListingErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task GetPathTypeAsync_DistinguishesFileDirectoryUnknown()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "hello");

        Assert.Equal(PathType.File, await FileSystemService.GetPathTypeAsync(file));
        Assert.Equal(PathType.Directory, await FileSystemService.GetPathTypeAsync(_root));
        Assert.Equal(PathType.Unknown, await FileSystemService.GetPathTypeAsync(Path.Combine(_root, "nope")));
    }

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\folder", true)]
    [InlineData("/mnt/share", false)]
    [InlineData(@"C:\Users", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUncPath_DetectsUncPrefix(string? path, bool expected)
    {
        Assert.Equal(expected, FileSystemService.IsUncPath(path));
    }

    [Fact]
    public void CreateListingError_ClassifiesKnownExceptions()
    {
        Assert.Equal(ListingErrorKind.NotFound,
            FileSystemService.CreateListingError(new DirectoryNotFoundException()).Kind);
        Assert.Equal(ListingErrorKind.AccessDenied,
            FileSystemService.CreateListingError(new UnauthorizedAccessException()).Kind);
        Assert.Equal(ListingErrorKind.Unavailable,
            FileSystemService.CreateListingError(new IOException("rete non raggiungibile")).Kind);
    }

    [Fact]
    public async Task CreateDirectoryAsync_CreatesFolder()
    {
        var error = await FileSystemService.CreateDirectoryAsync(_root, "nuova");

        Assert.Null(error);
        Assert.True(Directory.Exists(Path.Combine(_root, "nuova")));
    }

    [Fact]
    public async Task CreateDirectoryAsync_NameAlreadyExists_ReportsAlreadyExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "esistente"));

        var error = await FileSystemService.CreateDirectoryAsync(_root, "esistente");

        Assert.NotNull(error);
        Assert.Equal(ListingErrorKind.AlreadyExists, error!.Kind);
    }

    [Fact]
    public async Task RenameAsync_RenamesFolder()
    {
        string original = Path.Combine(_root, "vecchio");
        Directory.CreateDirectory(original);

        var error = await FileSystemService.RenameAsync(original, "nuovo");

        Assert.Null(error);
        Assert.False(Directory.Exists(original));
        Assert.True(Directory.Exists(Path.Combine(_root, "nuovo")));
    }

    [Fact]
    public async Task RenameAsync_TargetNameAlreadyExists_ReportsAlreadyExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));

        var error = await FileSystemService.RenameAsync(Path.Combine(_root, "a"), "b");

        Assert.NotNull(error);
        Assert.Equal(ListingErrorKind.AlreadyExists, error!.Kind);
    }

    [Fact]
    public async Task DeleteAsync_DeletesFolderRecursively()
    {
        string dir = Path.Combine(_root, "dacancellare");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(dir, "sub", "f.txt"), "x");

        var error = await FileSystemService.DeleteAsync(dir);

        Assert.Null(error);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteAsync_DeletesFile()
    {
        string file = Path.Combine(_root, "f.txt");
        await File.WriteAllTextAsync(file, "x");

        var error = await FileSystemService.DeleteAsync(file);

        Assert.Null(error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task DeleteAsync_MissingPath_ReportsNotFound()
    {
        var error = await FileSystemService.DeleteAsync(Path.Combine(_root, "non-esiste"));

        Assert.NotNull(error);
        Assert.Equal(ListingErrorKind.NotFound, error!.Kind);
    }

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\server\share\sub\folder", @"\\server\share")]
    [InlineData(@"\\server", null)]
    [InlineData(@"\\server\", null)]
    [InlineData(@"C:\local\path", null)]
    [InlineData(null, null)]
    public void GetUncRoot_ExtractsServerAndShareOrNull(string? path, string? expected)
    {
        Assert.Equal(expected, FileSystemService.GetUncRoot(path));
    }

    [Fact]
    public async Task CheckUncRootAccessAsync_UsesOverride_WhenSet()
    {
        FileSystemService.CheckUncRootAccessOverride = root =>
            Task.FromResult(root == @"\\server\share" ? UncAccessResult.AccessDenied : UncAccessResult.Ok);
        try
        {
            Assert.Equal(UncAccessResult.AccessDenied, await FileSystemService.CheckUncRootAccessAsync(@"\\server\share"));
            Assert.Equal(UncAccessResult.Ok, await FileSystemService.CheckUncRootAccessAsync(@"\\other\share"));
        }
        finally
        {
            FileSystemService.CheckUncRootAccessOverride = null;
        }
    }

    [Fact]
    public async Task CheckUncRootAccessAsync_NonexistentRoot_ReturnsUnavailable_NotAccessDenied()
    {
        // Nessun override: percorso locale inesistente, così il test gira su qualunque OS.
        var result = await FileSystemService.CheckUncRootAccessAsync(Path.Combine(_root, "does-not-exist"));
        Assert.Equal(UncAccessResult.Unavailable, result);
    }

    [Fact]
    public void GetPathType_UsesConfiguredAccessor()
    {
        var fake = new FakeFileSystemAccessor(existingFiles: new[] { "/fake/a.txt" });
        FileSystemService.Accessor = fake;
        try
        {
            PathType result = FileSystemService.GetPathType("/fake/a.txt");

            Assert.Equal(PathType.File, result);
        }
        finally
        {
            FileSystemService.Accessor = new DefaultFileSystemAccessor();
        }
    }

    [Fact]
    public async Task CreateDirectoryAsync_UsesConfiguredAccessor()
    {
        var fake = new FakeFileSystemAccessor();
        FileSystemService.Accessor = fake;
        try
        {
            ListingError? error = await FileSystemService.CreateDirectoryAsync("/fake", "newdir");

            Assert.Null(error);
            Assert.Contains(Path.Combine("/fake", "newdir"), fake.CreateDirectoryCalls);
        }
        finally
        {
            FileSystemService.Accessor = new DefaultFileSystemAccessor();
        }
    }

    [Fact]
    public async Task RenameAsync_UsesConfiguredAccessor()
    {
        var fake = new FakeFileSystemAccessor(existingDirectories: new[] { "/fake/dir" });
        FileSystemService.Accessor = fake;
        try
        {
            ListingError? error = await FileSystemService.RenameAsync("/fake/dir", "renamed");

            Assert.Null(error);
            Assert.Contains(("/fake/dir", Path.Combine("/fake", "renamed")), fake.MoveDirectoryCalls);
        }
        finally
        {
            FileSystemService.Accessor = new DefaultFileSystemAccessor();
        }
    }

    [Fact]
    public async Task DeleteAsync_UsesConfiguredAccessor()
    {
        var fake = new FakeFileSystemAccessor(existingFiles: new[] { "/fake/a.txt" });
        FileSystemService.Accessor = fake;
        try
        {
            ListingError? error = await FileSystemService.DeleteAsync("/fake/a.txt");

            Assert.Null(error);
            Assert.Contains("/fake/a.txt", fake.DeleteFileCalls);
        }
        finally
        {
            FileSystemService.Accessor = new DefaultFileSystemAccessor();
        }
    }
}

internal sealed class FakeFileSystemAccessor : IFileSystemAccessor
{
    private readonly HashSet<string> _files;
    private readonly HashSet<string> _directories;

    public List<string> CreateDirectoryCalls { get; } = new();
    public List<string> DeleteFileCalls { get; } = new();
    public List<(string Path, bool Recursive)> DeleteDirectoryCalls { get; } = new();
    public List<(string Source, string Destination)> MoveDirectoryCalls { get; } = new();
    public List<(string Source, string Destination)> MoveFileCalls { get; } = new();

    public FakeFileSystemAccessor(IEnumerable<string>? existingFiles = null, IEnumerable<string>? existingDirectories = null)
    {
        _files = new HashSet<string>(existingFiles ?? Array.Empty<string>());
        _directories = new HashSet<string>(existingDirectories ?? Array.Empty<string>());
    }

    public bool FileExists(string path) => _files.Contains(path);
    public bool DirectoryExists(string path) => _directories.Contains(path);
    public string[] EnumerateFileNames(string directoryPath) => Array.Empty<string>();
    public string[] EnumerateDirectoryNames(string directoryPath) => Array.Empty<string>();

    public void CreateDirectory(string path)
    {
        CreateDirectoryCalls.Add(path);
        _directories.Add(path);
    }

    public void DeleteFile(string path)
    {
        DeleteFileCalls.Add(path);
        _files.Remove(path);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        DeleteDirectoryCalls.Add((path, recursive));
        _directories.Remove(path);
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        MoveDirectoryCalls.Add((sourcePath, destinationPath));
        _directories.Remove(sourcePath);
        _directories.Add(destinationPath);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        MoveFileCalls.Add((sourcePath, destinationPath));
        _files.Remove(sourcePath);
        _files.Add(destinationPath);
    }
}
