using Sbroglione.Services;

namespace Sbroglione.Tests;

public sealed class RollingChecksumTests
{
    [Fact]
    public void AddByte_SameBytes_ProducesSameValueAsIndependentComputation()
    {
        var a = new RollingChecksum();
        var b = new RollingChecksum();
        byte[] window = { 1, 2, 3, 4, 5 };

        foreach (var value in window) a.AddByte(value);
        foreach (var value in window) b.AddByte(value);

        Assert.Equal(a.Value, b.Value);
    }

    [Fact]
    public void AddByte_DifferentBytes_ProducesDifferentValue()
    {
        var a = new RollingChecksum();
        var b = new RollingChecksum();

        foreach (byte value in new byte[] { 1, 2, 3 }) a.AddByte(value);
        foreach (byte value in new byte[] { 1, 2, 4 }) b.AddByte(value);

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void Roll_MatchesRecomputationFromScratch()
    {
        // Finestra iniziale [1,2,3,4,5] fatta scorrere di uno -> [2,3,4,5,6].
        var rolling = new RollingChecksum();
        foreach (byte value in new byte[] { 1, 2, 3, 4, 5 }) rolling.AddByte(value);
        rolling.Roll(outgoing: 1, incoming: 6);

        var expected = new RollingChecksum();
        foreach (byte value in new byte[] { 2, 3, 4, 5, 6 }) expected.AddByte(value);

        Assert.Equal(expected.Value, rolling.Value);
    }

    [Fact]
    public void Roll_MultipleSlides_MatchesRecomputationEachStep()
    {
        byte[] data = { 10, 20, 30, 40, 50, 60, 70, 80 };
        const int windowSize = 3;

        var rolling = new RollingChecksum();
        for (int i = 0; i < windowSize; i++) rolling.AddByte(data[i]);

        for (int start = 1; start <= data.Length - windowSize; start++)
        {
            rolling.Roll(outgoing: data[start - 1], incoming: data[start + windowSize - 1]);

            var expected = new RollingChecksum();
            for (int i = start; i < start + windowSize; i++) expected.AddByte(data[i]);

            Assert.Equal(expected.Value, rolling.Value);
        }
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var rolling = new RollingChecksum();
        foreach (byte value in new byte[] { 1, 2, 3 }) rolling.AddByte(value);
        rolling.Reset();

        var empty = new RollingChecksum();
        Assert.Equal(empty.Value, rolling.Value);
    }
}
