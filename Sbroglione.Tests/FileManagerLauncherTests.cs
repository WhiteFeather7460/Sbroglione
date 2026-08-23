using System.Diagnostics;
using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class FileManagerLauncherTests
{
    [Fact]
    public void BuildOpenFolderStartInfo_Windows_UsesExplorer()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildOpenFolderStartInfo(@"C:\Foo\Bar", FileManagerLauncher.Platform.Windows);

        Assert.Equal("explorer.exe", psi.FileName);
        Assert.Equal("\"C:\\Foo\\Bar\"", psi.Arguments);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void BuildOpenFolderStartInfo_MacOs_UsesOpen()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildOpenFolderStartInfo("/Users/foo/bar", FileManagerLauncher.Platform.MacOs);

        Assert.Equal("open", psi.FileName);
        Assert.Equal("\"/Users/foo/bar\"", psi.Arguments);
    }

    [Fact]
    public void BuildOpenFolderStartInfo_Linux_UsesXdgOpen()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildOpenFolderStartInfo("/home/foo/bar", FileManagerLauncher.Platform.Linux);

        Assert.Equal("xdg-open", psi.FileName);
        Assert.Equal("\"/home/foo/bar\"", psi.Arguments);
    }

    [Fact]
    public void BuildRevealStartInfo_Windows_UsesExplorerSelect()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildRevealStartInfo(@"C:\Foo\Bar\file.txt", FileManagerLauncher.Platform.Windows);

        Assert.Equal("explorer.exe", psi.FileName);
        Assert.Equal("/select,\"C:\\Foo\\Bar\\file.txt\"", psi.Arguments);
    }

    [Fact]
    public void BuildRevealStartInfo_MacOs_UsesOpenReveal()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildRevealStartInfo("/Users/foo/bar/file.txt", FileManagerLauncher.Platform.MacOs);

        Assert.Equal("open", psi.FileName);
        Assert.Equal("-R \"/Users/foo/bar/file.txt\"", psi.Arguments);
    }

    [Fact]
    public void BuildRevealStartInfo_Linux_OpensParentFolder()
    {
        ProcessStartInfo psi = FileManagerLauncher.BuildRevealStartInfo("/home/foo/bar/file.txt", FileManagerLauncher.Platform.Linux);

        Assert.Equal("xdg-open", psi.FileName);
        Assert.Equal("\"/home/foo/bar\"", psi.Arguments);
    }
}
