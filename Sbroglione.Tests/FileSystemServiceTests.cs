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
}
