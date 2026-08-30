using System.Globalization;
using Avalonia.Controls;
using Sbroglione.Converters;
using Xunit;

namespace Sbroglione.Tests;

public class WidthToTabStripPlacementConverterTests
{
    private readonly WidthToTabStripPlacementConverter _converter = new();

    [Theory]
    [InlineData(900.0, Dock.Left)]
    [InlineData(640.0, Dock.Left)]
    [InlineData(0.0, Dock.Left)]
    [InlineData(639.9, Dock.Bottom)]
    [InlineData(360.0, Dock.Bottom)]
    public void Convert_ReturnsExpectedPlacement(double width, Dock expected)
    {
        object? result = _converter.Convert(width, typeof(Dock), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }
}
