using System;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public class UiProgressThrottleTests
{
    [Fact]
    public void PublishesFirstCallThenThrottlesUntilIntervalElapses()
    {
        double now = 0;
        var throttle = new UiProgressThrottle(TimeSpan.FromMilliseconds(100), () => now);

        Assert.True(throttle.ShouldPublish());
        Assert.False(throttle.ShouldPublish());
        now = 0.05;
        Assert.False(throttle.ShouldPublish());
        now = 0.11;
        Assert.True(throttle.ShouldPublish());
        Assert.False(throttle.ShouldPublish());
    }
}
