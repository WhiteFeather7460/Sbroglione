using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sbroglione.Models;

/// <summary>
/// Filtro per estensione applicato all'enumerazione dei file durante una copia di
/// cartella: whitelist (copia solo le estensioni elencate) o blacklist (copia tutto
/// tranne le estensioni elencate). Le estensioni sono confrontate senza punto e senza
/// distinzione maiuscole/minuscole.
/// </summary>
public sealed class ExtensionFilter
{
    private readonly ExtensionFilterMode _mode;
    private readonly HashSet<string> _extensions;

    private ExtensionFilter(ExtensionFilterMode mode, HashSet<string> extensions)
    {
        _mode = mode;
        _extensions = extensions;
    }

    /// <summary>
    /// Analizza <paramref name="extensionsText"/> (estensioni separate da virgola, es.
    /// "jpg,png,mp4") secondo <paramref name="mode"/>. Ritorna null se il filtro non va
    /// applicato: <paramref name="mode"/> è <see cref="ExtensionFilterMode.None"/> oppure
    /// non c'è nessuna estensione valida dopo il parsing (nessuna restrizione = copia tutto).
    /// </summary>
    public static ExtensionFilter? Parse(ExtensionFilterMode mode, string? extensionsText)
    {
        if (mode == ExtensionFilterMode.None)
            return null;

        var extensions = (extensionsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(e => e.Length > 0)
            .ToHashSet();

        return extensions.Count == 0 ? null : new ExtensionFilter(mode, extensions);
    }

    /// <summary>True se il file va copiato secondo questo filtro.</summary>
    public bool Matches(string filePath)
    {
        string extension = Normalize(Path.GetExtension(filePath));
        bool inList = _extensions.Contains(extension);
        return _mode == ExtensionFilterMode.Whitelist ? inList : !inList;
    }

    private static string Normalize(string extension) =>
        extension.TrimStart('.').Trim().ToLowerInvariant();
}
