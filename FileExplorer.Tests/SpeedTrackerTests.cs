using System;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class SpeedTrackerTests
{
    [Fact]
    public void Report_ComputesCurrentAndAverageSpeed()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0;
        tracker.Report(copiedBytes: 100);

        Assert.Equal(100, tracker.CurrentBytesPerSecond, precision: 1);
        Assert.Equal(100, tracker.AverageBytesPerSecond, precision: 1);
    }

    [Fact]
    public void Peak_TracksMaximum()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 10000);

        now = 1.0; tracker.Report(500);   // 500 B/s
        now = 2.0; tracker.Report(600);   // 100 B/s

        Assert.Equal(500, tracker.PeakBytesPerSecond, precision: 1);
    }

    [Fact]
    public void Eta_UsesAverageSpeed()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0; tracker.Report(100);
        // Restano 900 byte a 100 B/s medi → 9 s.
        Assert.NotNull(tracker.EtaSeconds);
        Assert.Equal(9.0, tracker.EtaSeconds!.Value, precision: 1);
    }

    [Fact]
    public void Eta_NullWhenNoProgress()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        Assert.Null(tracker.EtaSeconds);
    }

    [Fact]
    public void TryTakeSnapshot_RateLimited()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);
        now = 0.5; tracker.Report(100);

        Assert.True(tracker.TryTakeSnapshot(out _));
        Assert.False(tracker.TryTakeSnapshot(out _)); // stesso istante: rifiutato

        now = 1.0; tracker.Report(200);
        Assert.True(tracker.TryTakeSnapshot(out var snapshot));
        Assert.NotEmpty(snapshot.Samples);
    }

    [Fact]
    public void QueriesBeforeStart_DoNotThrow()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);

        Assert.Equal(0, tracker.CurrentBytesPerSecond, precision: 1);
        Assert.Equal(0, tracker.AverageBytesPerSecond, precision: 1);
        Assert.Equal(0, tracker.PeakBytesPerSecond, precision: 1);
        Assert.Null(tracker.EtaSeconds);
        Assert.Empty(tracker.Samples);

        bool took = tracker.TryTakeSnapshot(out var snapshot);
        Assert.True(took);
        Assert.Equal(0, snapshot.CurrentBytesPerSecond, precision: 1);
        Assert.Equal(0, snapshot.AverageBytesPerSecond, precision: 1);
        Assert.Equal(0, snapshot.PeakBytesPerSecond, precision: 1);
        Assert.Null(snapshot.EtaSeconds);
        Assert.Empty(snapshot.Samples);
    }

    [Fact]
    public void Samples_CappedAtSixty()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: long.MaxValue);

        for (int i = 1; i <= 100; i++)
        {
            now = i * 0.5;
            tracker.Report(i * 10);
        }

        Assert.True(tracker.Samples.Count <= 60);
    }

    [Fact]
    public void Report_OutOfOrderCumulative_NeverNegativeCurrent()
    {
        double now = 0;
        var tracker = new SpeedTracker(() => now);
        tracker.Start(totalBytes: 1000);

        now = 1.0; tracker.Report(500);
        now = 2.0; tracker.Report(400); // cumulativo out-of-order da callback paralleli

        Assert.True(tracker.CurrentBytesPerSecond >= 0);
        Assert.All(tracker.Samples, sample => Assert.True(sample >= 0));
    }
}
