using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class ContractVersionCompatibilityTests
{
    [Theory]
    [InlineData("1.0.0", 1, true)]
    [InlineData("1.5.2", 1, true)]
    [InlineData("2.0.0", 1, false)]
    [InlineData("0.9.0", 1, false)]
    [InlineData("1", 1, true)]
    public void IsCompatible_ComparesMajorVersion(string manifestVersion, int supportedMajor, bool expected)
    {
        Assert.Equal(expected, ContractVersionCompatibility.IsCompatible(manifestVersion, supportedMajor));
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    public void IsCompatible_UnparsableVersion_ReturnsFalse(string manifestVersion)
    {
        Assert.False(ContractVersionCompatibility.IsCompatible(manifestVersion, supportedMajor: 1));
    }
}
