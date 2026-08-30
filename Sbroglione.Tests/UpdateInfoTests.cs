using Sbroglione.Models;

namespace Sbroglione.Tests;

public class UpdateInfoTests
{
    [Fact]
    public void UpdateCheckResult_ExposesStatusInfoAndError()
    {
        var info = new UpdateInfo(new Version(2, 0, 0), "https://example.test/releases/tag/v2.0.0", "https://example.test/app.exe", "app.exe");
        var result = new UpdateCheckResult(UpdateCheckStatus.Available, info, null);

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(new Version(2, 0, 0), result.Info!.Version);
        Assert.Equal("app.exe", result.Info.AssetFileName);
        Assert.Null(result.ErrorMessage);
    }
}
