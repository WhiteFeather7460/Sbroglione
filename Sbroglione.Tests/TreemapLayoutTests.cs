using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class TreemapLayoutTests
{
    [Fact]
    public void Compute_AreasProportionalToValues()
    {
        var rects = TreemapLayout.Compute(new long[] { 3, 1 }, 0, 0, 100, 100);

        Assert.Equal(7500, rects[0].Area, precision: 6);
        Assert.Equal(2500, rects[1].Area, precision: 6);
    }

    [Fact]
    public void Compute_AllRectsInsideBounds_AndTotalAreaMatches()
    {
        var values = new long[] { 500, 300, 200, 100, 50, 25 };
        var rects = TreemapLayout.Compute(values, 10, 20, 400, 300);

        double totalArea = 0;
        foreach (var rect in rects)
        {
            Assert.True(rect.X >= 10 - 1e-6 && rect.Y >= 20 - 1e-6);
            Assert.True(rect.X + rect.Width <= 410 + 1e-6);
            Assert.True(rect.Y + rect.Height <= 320 + 1e-6);
            totalArea += rect.Area;
        }

        Assert.Equal(400 * 300, totalArea, precision: 4);
    }

    [Fact]
    public void Compute_RectsDoNotOverlap()
    {
        var rects = TreemapLayout.Compute(new long[] { 40, 30, 20, 10 }, 0, 0, 200, 100);

        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                double overlapWidth = Math.Min(rects[i].X + rects[i].Width, rects[j].X + rects[j].Width)
                                      - Math.Max(rects[i].X, rects[j].X);
                double overlapHeight = Math.Min(rects[i].Y + rects[i].Height, rects[j].Y + rects[j].Height)
                                       - Math.Max(rects[i].Y, rects[j].Y);
                double overlapArea = Math.Max(0, overlapWidth) * Math.Max(0, overlapHeight);
                Assert.Equal(0, overlapArea, precision: 4);
            }
    }

    [Fact]
    public void Compute_NonFiniteBounds_ReturnsDefaultRects()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var rects = TreemapLayout.Compute(new long[] { 5, 3 }, 0, 0, bad, 100);
            Assert.All(rects, rect => Assert.Equal(default, rect));

            rects = TreemapLayout.Compute(new long[] { 5, 3 }, 0, 0, 100, bad);
            Assert.All(rects, rect => Assert.Equal(default, rect));
        }
    }

    [Fact]
    public void Compute_EmptyAndZeroValues_HandledGracefully()
    {
        Assert.Empty(TreemapLayout.Compute(Array.Empty<long>(), 0, 0, 100, 100));

        var rects = TreemapLayout.Compute(new long[] { 0, 10, 0 }, 0, 0, 100, 100);
        Assert.Equal(default, rects[0]);
        Assert.Equal(default, rects[2]);
        Assert.Equal(10000, rects[1].Area, precision: 4);
    }
}
