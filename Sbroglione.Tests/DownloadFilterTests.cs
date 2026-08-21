using Sbroglione.Models;

namespace Sbroglione.Tests;

public sealed class DownloadFilterTests
{
    private static RemoteItem File(string name, long size = 100, DateTime? modified = null) =>
        new(name, "/dir/" + name, IsDirectory: false, size, modified ?? new DateTime(2026, 6, 1, 12, 0, 0));

    [Fact]
    public void Matches_EmptyFilter_MatchesEverything()
    {
        var filter = new DownloadFilter();
        Assert.True(filter.Matches(File("a.txt")));
    }

    [Theory]
    [InlineData("*.jpg", "foto.jpg", true)]
    [InlineData("*.jpg", "foto.png", false)]
    [InlineData("*.JPG", "foto.jpg", true)]           // case-insensitive
    [InlineData("report*", "report_2026.pdf", true)]
    [InlineData("report*", "old_report.pdf", false)]
    [InlineData("*.jpg;*.png", "foto.png", true)]     // pattern multipli separati da ';'
    [InlineData(" *.jpg ; *.png ", "foto.png", true)] // spazi tollerati
    [InlineData("*.jpg;*.png", "doc.pdf", false)]
    public void Matches_NamePattern(string pattern, string fileName, bool expected)
    {
        var filter = new DownloadFilter { NamePattern = pattern };
        Assert.Equal(expected, filter.Matches(File(fileName)));
    }

    [Fact]
    public void Matches_SizeRange()
    {
        var filter = new DownloadFilter { MinSize = 50, MaxSize = 150 };
        Assert.True(filter.Matches(File("a", size: 100)));
        Assert.True(filter.Matches(File("a", size: 50)));   // estremi inclusi
        Assert.True(filter.Matches(File("a", size: 150)));
        Assert.False(filter.Matches(File("a", size: 49)));
        Assert.False(filter.Matches(File("a", size: 151)));
    }

    [Fact]
    public void Matches_DateRange()
    {
        var filter = new DownloadFilter
        {
            ModifiedAfter = new DateTime(2026, 1, 1),
            ModifiedBefore = new DateTime(2026, 12, 31)
        };
        Assert.True(filter.Matches(File("a", modified: new DateTime(2026, 6, 1))));
        Assert.False(filter.Matches(File("a", modified: new DateTime(2025, 6, 1))));
        Assert.False(filter.Matches(File("a", modified: new DateTime(2027, 6, 1))));
    }

    [Fact]
    public void Matches_CombinedCriteria_AllMustPass()
    {
        var filter = new DownloadFilter { NamePattern = "*.jpg", MinSize = 50 };
        Assert.True(filter.Matches(File("a.jpg", size: 100)));
        Assert.False(filter.Matches(File("a.jpg", size: 10)));
        Assert.False(filter.Matches(File("a.png", size: 100)));
    }

    [Fact]
    public void Matches_Directory_IgnoresSizeAndPattern()
    {
        // Le cartelle passano sempre: i filtri si applicano ai file.
        var filter = new DownloadFilter { NamePattern = "*.jpg", MinSize = 1000 };
        var dir = new RemoteItem("sub", "/dir/sub", IsDirectory: true, 0, new DateTime(2026, 6, 1));
        Assert.True(filter.Matches(dir));
    }
}
