using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sbroglione.Services;

/// <summary>
/// Token bucket per il limite di banda: accumula "byte spendibili" al ritmo di
/// <see cref="BytesPerSecond"/>, con burst massimo di 1 secondo. Thread-safe.
/// Il clock è iniettabile per i test.
/// </summary>
public sealed class TokenBucket
{
    private readonly Func<double> _nowSeconds;
    private readonly object _gate = new();
    private double _available;
    private double _lastRefill;
    private double _bytesPerSecond;

    public TokenBucket(Func<double> nowSeconds)
    {
        _nowSeconds = nowSeconds;
        _lastRefill = nowSeconds();
    }

    /// <summary>Byte al secondo; 0 (o negativo) = nessun limite.</summary>
    public double BytesPerSecond
    {
        get { lock (_gate) return _bytesPerSecond; }
        set
        {
            lock (_gate)
            {
                _bytesPerSecond = value;
                // Cambio limite: il bucket riparte pieno per evitare attese spurie.
                _available = value;
                _lastRefill = _nowSeconds();
            }
        }
    }

    /// <summary>
    /// Prova a spendere <paramref name="bytes"/>: restituisce 0 se concessi subito,
    /// altrimenti i secondi da attendere prima di riprovare. La spesa avviene comunque
    /// (il saldo può andare negativo): un blocco già letto va scritto in ogni caso.
    /// </summary>
    public double ReserveOrWaitSeconds(long bytes)
    {
        lock (_gate)
        {
            if (_bytesPerSecond <= 0)
                return 0;

            double now = _nowSeconds();
            _available = Math.Min(_bytesPerSecond, _available + (now - _lastRefill) * _bytesPerSecond);
            _lastRefill = now;

            _available -= bytes;
            return _available >= 0 ? 0 : -_available / _bytesPerSecond;
        }
    }
}

/// <summary>
/// Limite di banda globale della copia: unico bucket condiviso da tutte le copie
/// in corso, pilotato dalle impostazioni (<see cref="Models.AppSettings.ThrottleEnabled"/>
/// e <see cref="Models.AppSettings.ThrottleMBps"/>) rilette a ogni chiamata,
/// così il toggle rapido ha effetto immediato sulle copie già avviate.
/// </summary>
public static class IoThrottleService
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly TokenBucket Bucket = new(() => Clock.Elapsed.TotalSeconds);

    public static async Task WaitAsync(long bytes, CancellationToken ct)
    {
        var settings = AppSettingsStore.Current;
        double rate = settings.ThrottleEnabled ? settings.ThrottleMBps * 1024.0 * 1024.0 : 0;
        if (Math.Abs(Bucket.BytesPerSecond - rate) > 0.5)
            Bucket.BytesPerSecond = rate;

        if (rate <= 0)
            return;

        double waitSeconds = Bucket.ReserveOrWaitSeconds(bytes);
        if (waitSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
    }
}
