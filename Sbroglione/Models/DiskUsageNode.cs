using System.Collections.Generic;
using System.Threading;

namespace Sbroglione.Models;

/// <summary>Nodo dell'albero di occupazione disco (file o cartella con somma ricorsiva).</summary>
public class DiskUsageNode
{
    private long _sizeBytes;

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get => _sizeBytes; set => _sizeBytes = value; }
    public bool IsDirectory { get; set; }

    /// <summary>True finché la scansione a strati non ha ancora enumerato il contenuto diretto di questa cartella.</summary>
    public bool IsPending { get; set; }

    /// <summary>Nodo cartella padre, usato per propagare gli incrementi di dimensione durante la scansione a strati.</summary>
    public DiskUsageNode? Parent { get; set; }

    public List<DiskUsageNode> Children { get; } = new();

    /// <summary>
    /// Aggiunge <paramref name="delta"/> a questo nodo e a tutti gli antenati, in modo
    /// thread-safe: durante la scansione a strati più cartelle sorelle (con antenati in
    /// comune) vengono scansionate in parallelo su thread diversi.
    /// </summary>
    internal void PropagateSizeIncrease(long delta)
    {
        for (var node = this; node is not null; node = node.Parent)
            Interlocked.Add(ref node._sizeBytes, delta);
    }
}
