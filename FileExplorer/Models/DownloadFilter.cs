using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileExplorer.Models;

/// <summary>
/// Criteri di filtro per i download. <see cref="Matches"/> valuta nome, dimensione e data;
/// <see cref="OnlyMissing"/> e <see cref="Recursive"/> sono gestiti da DownloadService/ViewModel.
/// </summary>
public sealed class DownloadFilter
{
    /// <summary>Pattern wildcard separati da ';' (es. "*.jpg;report*"). Vuoto o null = tutti.</summary>
    public string? NamePattern { get; set; }

    /// <summary>Dimensione minima in byte (inclusa).</summary>
    public long? MinSize { get; set; }

    /// <summary>Dimensione massima in byte (inclusa).</summary>
    public long? MaxSize { get; set; }

    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }

    /// <summary>Scarica solo i file assenti dalla destinazione.</summary>
    public bool OnlyMissing { get; set; }

    /// <summary>Includi le sottocartelle nel download della directory.</summary>
    public bool Recursive { get; set; }

    /// <summary>True se il file passa nome, dimensione e data. Le cartelle passano sempre.</summary>
    public bool Matches(RemoteItem item)
    {
        if (item.IsDirectory)
            return true;

        if (!MatchesName(item.Name))
            return false;

        if (MinSize is not null && item.Size < MinSize)
            return false;

        if (MaxSize is not null && item.Size > MaxSize)
            return false;

        if (ModifiedAfter is not null && item.Modified < ModifiedAfter)
            return false;

        if (ModifiedBefore is not null && item.Modified > ModifiedBefore)
            return false;

        return true;
    }

    private bool MatchesName(string name)
    {
        if (string.IsNullOrWhiteSpace(NamePattern))
            return true;

        return NamePattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(pattern => Regex.IsMatch(
                name,
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase));
    }
}
