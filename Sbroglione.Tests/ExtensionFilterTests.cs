using Sbroglione.Models;

namespace Sbroglione.Tests;

public sealed class ExtensionFilterTests
{
    [Fact]
    public void Parse_ModeNone_ReturnsNull()
    {
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.None, "jpg,png"));
    }

    [Fact]
    public void Parse_WhitelistEmptyText_ReturnsNull()
    {
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, ""));
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, null));
        Assert.Null(ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "  , , "));
    }

    [Fact]
    public void Matches_Whitelist_OnlyListedExtensionsMatch()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, "jpg, png,MP4");
        Assert.NotNull(filter);
        Assert.True(filter!.Matches(@"C:\photos\a.jpg"));
        Assert.True(filter.Matches(@"C:\photos\B.PNG"));
        Assert.True(filter.Matches(@"C:\videos\c.mp4"));
        Assert.False(filter.Matches(@"C:\docs\d.txt"));
        Assert.False(filter.Matches(@"C:\misc\noext"));
    }

    [Fact]
    public void Matches_Blacklist_ListedExtensionsExcluded()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Blacklist, "tmp,.log");
        Assert.NotNull(filter);
        Assert.False(filter!.Matches(@"C:\x\a.tmp"));
        Assert.False(filter.Matches(@"C:\x\b.LOG"));
        Assert.True(filter.Matches(@"C:\x\c.jpg"));
        Assert.True(filter.Matches(@"C:\x\noext"));
    }

    [Fact]
    public void Parse_LeadingDotAndWhitespaceInExtensions_IsNormalized()
    {
        var filter = ExtensionFilter.Parse(ExtensionFilterMode.Whitelist, " .jpg , PNG ");
        Assert.NotNull(filter);
        Assert.True(filter!.Matches(@"C:\a.jpg"));
        Assert.True(filter.Matches(@"C:\b.png"));
    }
}
