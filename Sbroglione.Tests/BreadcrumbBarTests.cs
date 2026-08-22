using Sbroglione.Views.Controls;

namespace Sbroglione.Tests;

public sealed class BreadcrumbBarTests
{
    [Fact]
    public void BuildSegments_UnixPath_ReturnsRootPlusSegmentsWithCumulativePaths()
    {
        var segments = BreadcrumbBar.BuildSegments("/home/user/docs");

        Assert.Equal(new[] { "/", "home", "user", "docs" }, segments.Select(s => s.Label));
        Assert.Equal("/", segments[0].FullPath);
        Assert.Equal("/home", segments[1].FullPath);
        Assert.Equal("/home/user", segments[2].FullPath);
        Assert.Equal("/home/user/docs", segments[3].FullPath);
    }

    [Fact]
    public void BuildSegments_UnixRoot_ReturnsOnlyRoot()
    {
        var segments = BreadcrumbBar.BuildSegments("/");

        Assert.Single(segments);
        Assert.Equal("/", segments[0].FullPath);
    }

    [Fact]
    public void BuildSegments_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(BreadcrumbBar.BuildSegments(null));
        Assert.Empty(BreadcrumbBar.BuildSegments(""));
    }

    [Fact]
    public void BuildSegments_WindowsPath_ReturnsSegmentsWithCumulativePaths()
    {
        var segments = BreadcrumbBar.BuildSegments(@"C:\Users\me\Documents");

        Assert.Equal(new[] { "C:", "Users", "me", "Documents" }, segments.Select(s => s.Label));
        Assert.EndsWith("Documents", segments[3].FullPath);
    }
}
