using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorer.Services;

/// <summary>Fotografia della velocità di copia per l'aggiornamento della UI.</summary>
public readonly record struct SpeedSnapshot(
    double CurrentBytesPerSecond,
    double AverageBytesPerSecond,
    double PeakBytesPerSecond,
    double? EtaSeconds,
    IReadOnlyList<double> Samples);

/// <summary>
/// Traccia la velocità di una copia a partire dai byte cumulativi riportati dai
/// callback di avanzamento. Clock iniettabile per i test; thread-safe (i callback
/// di copia arrivano da thread di background).
/// </summary>
public sealed class SpeedTracker
{
    private const int MaxSamples = 60;
    private const double SnapshotIntervalSeconds = 0.25;

    private readonly Func<double> _nowSeconds;
    private readonly object _gate = new();
    private readonly List<(double Time, long Bytes)> _points = new();
    private readonly List<double> _samples = new();

    private long _totalBytes;
    private double _startTime;
    private long _lastBytes;
    private double _peak;
    private double _lastSnapshotTime = double.NegativeInfinity;

    public SpeedTracker(Func<double> nowSeconds)
    {
        _nowSeconds = nowSeconds;
    }

    public void Start(long totalBytes)
    {
        lock (_gate)
        {
            _totalBytes = totalBytes;
            _startTime = _nowSeconds();
            _lastBytes = 0;
            _peak = 0;
            _points.Clear();
            _samples.Clear();
            _lastSnapshotTime = double.NegativeInfinity;
            _points.Add((_startTime, 0));
        }
    }

    public void Report(long copiedBytes)
    {
        lock (_gate)
        {
            double now = _nowSeconds();
            _lastBytes = copiedBytes;
            _points.Add((now, copiedBytes));

            // Finestra mobile: tiene solo l'ultimo secondo (e almeno 2 punti).
            while (_points.Count > 2 && now - _points[0].Time > 1.0)
                _points.RemoveAt(0);

            double current = CurrentLocked(now);
            if (current > _peak)
                _peak = current;

            _samples.Add(current / (1024.0 * 1024.0));
            if (_samples.Count > MaxSamples)
                _samples.RemoveAt(0);
        }
    }

    private double CurrentLocked(double now)
    {
        // Guardia: se Start() non è ancora stato chiamato, _points è vuota.
        if (_points.Count == 0)
            return 0;

        var oldest = _points[0];
        double window = now - oldest.Time;
        return window > 0 ? (_lastBytes - oldest.Bytes) / window : 0;
    }

    public double CurrentBytesPerSecond
    {
        get { lock (_gate) return CurrentLocked(_nowSeconds()); }
    }

    public double AverageBytesPerSecond
    {
        get
        {
            lock (_gate)
            {
                double elapsed = _nowSeconds() - _startTime;
                return elapsed > 0 ? _lastBytes / elapsed : 0;
            }
        }
    }

    public double PeakBytesPerSecond
    {
        get { lock (_gate) return _peak; }
    }

    public double? EtaSeconds
    {
        get
        {
            lock (_gate)
            {
                double average = AverageLocked();
                if (average <= 0 || _totalBytes <= 0 || _lastBytes >= _totalBytes)
                    return null;
                return (_totalBytes - _lastBytes) / average;
            }
        }
    }

    private double AverageLocked()
    {
        double elapsed = _nowSeconds() - _startTime;
        return elapsed > 0 ? _lastBytes / elapsed : 0;
    }

    public IReadOnlyList<double> Samples
    {
        get { lock (_gate) return _samples.ToList(); }
    }

    /// <summary>
    /// True al massimo ~4 volte al secondo: limita la frequenza di aggiornamento
    /// della UI senza timer dedicati (chiamato dai callback di avanzamento).
    /// </summary>
    public bool TryTakeSnapshot(out SpeedSnapshot snapshot)
    {
        lock (_gate)
        {
            double now = _nowSeconds();
            if (now - _lastSnapshotTime < SnapshotIntervalSeconds)
            {
                snapshot = default;
                return false;
            }

            _lastSnapshotTime = now;
            double average = AverageLocked();
            double? eta = average > 0 && _totalBytes > 0 && _lastBytes < _totalBytes
                ? (_totalBytes - _lastBytes) / average
                : null;

            snapshot = new SpeedSnapshot(CurrentLocked(now), average, _peak, eta, _samples.ToList());
            return true;
        }
    }
}
