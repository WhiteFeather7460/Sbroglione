using System;
using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Preset nominato di coppie di copia, rieseguibile con un click dalla scheda Copia.</summary>
public class CopyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<CopyProfilePair> Pairs { get; set; } = new();
}

/// <summary>Singola coppia sorgente/destinazione memorizzata in un profilo.</summary>
public class CopyProfilePair
{
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public List<string> ExtraDestinations { get; set; } = new();
    public bool SkipUnchanged { get; set; }
}
