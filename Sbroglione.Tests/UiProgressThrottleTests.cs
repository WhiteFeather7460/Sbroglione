using System;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

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
