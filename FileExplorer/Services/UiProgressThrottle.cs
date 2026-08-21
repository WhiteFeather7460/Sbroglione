using System;
using System.Diagnostics;
using System.Threading;

namespace FileExplorer.Services;

/// <summary>
/// Gate thread-safe per aggiornamenti UI ad alta frequenza: ShouldPublish ritorna true
/// al massimo una volta per intervallo (default 100 ms). Il primo campione passa sempre.
/// Lo stato finale va pubblicato comunque dal chiamante, fuori dal gate.
/// </summary>
public sealed class UiProgressThrottle
{
    private readonly double _intervalSeconds;
    private readonly Func<double> _clockSeconds;
    private long _lastBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);

    public UiProgressThrottle(TimeSpan? interval = null, Func<double>? clockSeconds = null)
    {
        _intervalSeconds = (interval ?? TimeSpan.FromMilliseconds(100)).TotalSeconds;
        _clockSeconds = clockSeconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    public bool ShouldPublish()
    {
        double now = _clockSeconds();
        long lastBits = Interlocked.Read(ref _lastBits);
        if (now - BitConverter.Int64BitsToDouble(lastBits) < _intervalSeconds)
            return false;
        return Interlocked.CompareExchange(ref _lastBits, BitConverter.DoubleToInt64Bits(now), lastBits) == lastBits;
    }
}
