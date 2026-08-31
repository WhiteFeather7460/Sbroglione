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

    [Fact]
    public void CreateDirectory_CreatesRealDirectory()
    {
        var accessor = new DefaultFileSystemAccessor();
        string dir = Path.Combine(Path.GetTempPath(), "accessor-test-" + Guid.NewGuid());
        try
        {
            accessor.CreateDirectory(dir);

            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeleteFile_RemovesRealFile()
    {
        var accessor = new DefaultFileSystemAccessor();
        string filePath = Path.GetTempFileName();
        try
        {
            accessor.DeleteFile(filePath);

            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void DeleteDirectory_RemovesRealDirectoryRecursively()
    {
        var accessor = new DefaultFileSystemAccessor();
        string dir = Path.Combine(Path.GetTempPath(), "accessor-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        try
        {
            accessor.DeleteDirectory(dir, recursive: true);

            Assert.False(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MoveDirectory_RenamesRealDirectory()
    {
        var accessor = new DefaultFileSystemAccessor();
        string dir = Path.Combine(Path.GetTempPath(), "accessor-test-" + Guid.NewGuid());
        string renamed = Path.Combine(Path.GetTempPath(), "accessor-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            accessor.MoveDirectory(dir, renamed);

            Assert.False(Directory.Exists(dir));
            Assert.True(Directory.Exists(renamed));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            if (Directory.Exists(renamed))
                Directory.Delete(renamed, recursive: true);
        }
    }

    [Fact]
    public void MoveFile_RenamesRealFile()
    {
        var accessor = new DefaultFileSystemAccessor();
        string filePath = Path.GetTempFileName();
        string renamed = filePath + ".renamed";
        try
        {
            accessor.MoveFile(filePath, renamed);

            Assert.False(File.Exists(filePath));
            Assert.True(File.Exists(renamed));
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
            if (File.Exists(renamed))
                File.Delete(renamed);
        }
    }
}
