using System;
using System.Collections.Generic;

namespace Sbroglione.Models;

/// <summary>
/// Voce del journal delle copie: una copia avviata e non ancora conclusa.
/// Le voci rimaste su disco all'avvio indicano copie interrotte (crash/chiusura).
/// </summary>
public class CopyJobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public List<string> ExtraDestinations { get; set; } = new();
    public DateTime StartedUtc { get; set; }
}
