using System.Collections.Generic;

namespace FileExplorer.Models;

/// <summary>Nodo dell'albero di occupazione disco (file o cartella con somma ricorsiva).</summary>
public class DiskUsageNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsDirectory { get; set; }
    public List<DiskUsageNode> Children { get; } = new();
}
