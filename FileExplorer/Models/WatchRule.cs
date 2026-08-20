using System;

namespace FileExplorer.Models;

/// <summary>Modalità di sincronizzazione di una regola watch-folder.</summary>
public enum WatchMode
{
    /// <summary>Sincronizza al cambiamento della sorgente (FileSystemWatcher + debounce).</summary>
    OnChange,

    /// <summary>Sincronizza a intervallo fisso di minuti.</summary>
    Interval
}

/// <summary>Regola di sincronizzazione automatica sorgente → destinazione.</summary>
public class WatchRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public WatchMode Mode { get; set; } = WatchMode.OnChange;
    public int IntervalMinutes { get; set; } = 30;
}
