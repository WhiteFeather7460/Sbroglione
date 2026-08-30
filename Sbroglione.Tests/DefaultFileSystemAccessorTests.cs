using System;
using System.IO;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public class DefaultFileSystemAccessorTests
{
    [Fact]
    public void FileExists_ReturnsTrueForExistingFile_FalseOtherwise()
    {
        var accessor = new DefaultFileSystemAccessor();
        string tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(accessor.FileExists(tempFile));
            Assert.False(accessor.FileExists(tempFile + ".missing"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DirectoryExists_ReturnsTrueForExistingDirectory_FalseOtherwise()
    {
        var accessor = new DefaultFileSystemAccessor();

        Assert.True(accessor.DirectoryExists(Path.GetTempPath()));
        Assert.False(accessor.DirectoryExists(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    [Fact]
    public void EnumerateFileNames_ReturnsFilesInDirectory()
    {
        var accessor = new DefaultFileSystemAccessor();
        string dir = Path.Combine(Path.GetTempPath(), "accessor-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string filePath = Path.Combine(dir, "a.txt");
            File.WriteAllText(filePath, "x");

            string[] names = accessor.EnumerateFileNames(dir);

            Assert.Contains(filePath, names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
