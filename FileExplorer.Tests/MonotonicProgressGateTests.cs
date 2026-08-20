using System.Linq;
using System.Threading.Tasks;
using FileExplorer.Services;
using Xunit;

namespace FileExplorer.Tests;

public class MonotonicProgressGateTests
{
    [Fact]
    public void AdvancesOnFirstValueAndOnNonDecreasingValues()
    {
        var gate = new MonotonicProgressGate();

        Assert.True(gate.TryAdvance(0));
        Assert.True(gate.TryAdvance(5));
        Assert.True(gate.TryAdvance(5));
        Assert.True(gate.TryAdvance(6));
    }

    [Fact]
    public void RejectsValuesLowerThanTheLastAdvanced()
    {
        var gate = new MonotonicProgressGate();

        Assert.True(gate.TryAdvance(6));
        Assert.False(gate.TryAdvance(5));
        Assert.False(gate.TryAdvance(0));
        Assert.True(gate.TryAdvance(7));
    }

    [Fact]
    public async Task ConcurrentAdvancesNeverLoseTheHighestValue()
    {
        var gate = new MonotonicProgressGate();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
                gate.TryAdvance(i);
        })));

        // Nessun aggiornamento perso: il CAS lascia il gate sul massimo osservato.
        Assert.False(gate.TryAdvance(498));
        Assert.True(gate.TryAdvance(499));
    }
}
