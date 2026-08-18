using System;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public sealed class TokenBucketTests
{
    [Fact]
    public void ReserveOrWaitSeconds_RateZero_AlwaysGrantsImmediately()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 0 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(long.MaxValue));
    }

    [Fact]
    public void ReserveOrWaitSeconds_WithinBudget_GrantsImmediately()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        // Il bucket parte pieno (burst di 1 secondo = 1000 byte).
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
    }

    [Fact]
    public void ReserveOrWaitSeconds_BudgetExhausted_ReturnsWaitTime()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        // Bucket vuoto: altri 500 byte richiedono 0.5 s di attesa.
        double wait = bucket.ReserveOrWaitSeconds(500);
        Assert.Equal(0.5, wait, precision: 3);
    }

    [Fact]
    public void ReserveOrWaitSeconds_RefillsWithTime()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        now = 1.0; // dopo 1 s il bucket è di nuovo pieno.
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
    }

    [Fact]
    public void ReserveOrWaitSeconds_RefillCappedAtOneSecondBurst()
    {
        double now = 0;
        var bucket = new TokenBucket(() => now) { BytesPerSecond = 1000 };

        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        now = 10.0; // il refill non accumula oltre 1 s di burst.
        Assert.Equal(0, bucket.ReserveOrWaitSeconds(1000));
        double wait = bucket.ReserveOrWaitSeconds(1000);
        Assert.Equal(1.0, wait, precision: 3);
    }
}
