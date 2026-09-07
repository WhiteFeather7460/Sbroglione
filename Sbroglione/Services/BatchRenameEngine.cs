using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sbroglione.Services;

public sealed record RenameOptions(
    bool UseRegex,
    string FindPattern,
    string ReplacePattern,
    string Template,
    int CounterStart,
    int CounterStep);

public sealed record RenamePlanItem(
    string OriginalPath,
    string OriginalName,
    string NewName,
    string NewPath,
    bool HasConflict,
    string? Error);

/// <summary>
/// Motore puro di calcolo del piano di rinomina: nessun accesso disco, solo
/// stringa in ingresso -&gt; nome calcolato, cosi' e' interamente testabile e
/// riusabile sia per l'anteprima live sia per l'esecuzione effettiva.
/// </summary>
public static class BatchRenameEngine
{
    private static readonly Regex TokenPattern = new(@"\{(\w+)(?::([^}]*))?\}", RegexOptions.Compiled);

    public static IReadOnlyList<RenamePlanItem> BuildPlan(
        IReadOnlyList<(string Path, DateTime LastModified)> files,
        RenameOptions options)
    {
        var results = new List<RenamePlanItem>(files.Count);
        var seenNewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int counter = options.CounterStart;

        foreach ((string path, DateTime lastModified) in files)
        {
            string originalName = Path.GetFileName(path);
            string? error = null;
            string newName;

            try
            {
                newName = ComputeNewName(originalName, options, counter, lastModified);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                newName = originalName;
            }

            string? directory = Path.GetDirectoryName(path);
            string newPath = directory is null ? newName : Path.Combine(directory, newName);
            bool conflict = error is null && !seenNewPaths.Add(newPath);

            results.Add(new RenamePlanItem(path, originalName, newName, newPath, conflict, error));
            counter += options.CounterStep;
        }

        return results;
    }

    internal static string ComputeNewName(string originalName, RenameOptions options, int counterValue, DateTime fileDate)
    {
        string baseName = Path.GetFileNameWithoutExtension(originalName);
        string ext = Path.GetExtension(originalName);
        string extNoDot = ext.Length > 0 ? ext[1..] : ext;

        if (!string.IsNullOrEmpty(options.FindPattern))
        {
            baseName = options.UseRegex
                ? Regex.Replace(baseName, options.FindPattern, options.ReplacePattern)
                : baseName.Replace(options.FindPattern, options.ReplacePattern, StringComparison.Ordinal);
        }

        if (string.IsNullOrEmpty(options.Template))
            return baseName + ext;

        string result = TokenPattern.Replace(options.Template, match =>
        {
            string token = match.Groups[1].Value.ToLowerInvariant();
            string? format = match.Groups[2].Success ? match.Groups[2].Value : null;
            return token switch
            {
                "name" => baseName,
                "ext" => extNoDot,
                "counter" => counterValue.ToString(format ?? "0"),
                "date" => fileDate.ToString(format ?? "yyyyMMdd"),
                _ => match.Value,
            };
        });

        return result;
    }
}
