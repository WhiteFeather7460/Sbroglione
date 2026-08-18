using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Formato di esportazione del report di confronto.</summary>
public enum ComparisonReportFormat
{
    Html,
    Csv,
    Json
}

/// <summary>
/// Rendering ed esportazione del report di confronto directory in HTML
/// (autonomo, CSS inline), CSV (separatore ';') e JSON.
/// </summary>
public static class ComparisonReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Render(
        DirectoryComparisonResult result,
        ComparisonReportFormat format,
        string leftRoot,
        string rightRoot,
        DateTime generatedUtc)
    {
        return format switch
        {
            ComparisonReportFormat.Csv => RenderCsv(result),
            ComparisonReportFormat.Json => RenderJson(result, leftRoot, rightRoot, generatedUtc),
            _ => RenderHtml(result, leftRoot, rightRoot, generatedUtc)
        };
    }

    public static async Task ExportAsync(
        string filePath,
        DirectoryComparisonResult result,
        ComparisonReportFormat format,
        string leftRoot,
        string rightRoot,
        DateTime generatedUtc,
        CancellationToken ct)
    {
        string content = Render(result, format, leftRoot, rightRoot, generatedUtc);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, ct);
    }

    public static string SuggestFileName(ComparisonReportFormat format, DateTime generatedUtc)
    {
        string extension = format switch
        {
            ComparisonReportFormat.Csv => "csv",
            ComparisonReportFormat.Json => "json",
            _ => "html"
        };
        return $"confronto-{generatedUtc:yyyyMMdd-HHmmss}.{extension}";
    }

    private static string RenderCsv(DirectoryComparisonResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("categoria;percorso");
        AppendCsvRows(builder, "solo-a-sinistra", result.LeftOnly);
        AppendCsvRows(builder, "solo-a-destra", result.RightOnly);
        AppendCsvRows(builder, "diversi", result.Different);
        AppendCsvRows(builder, "identici", result.Identical);
        return builder.ToString();
    }

    private static void AppendCsvRows(StringBuilder builder, string category, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            // Il ';' è il separatore: i path che lo contengono vanno quotati.
            string cell = path.Contains(';') || path.Contains('"')
                ? "\"" + path.Replace("\"", "\"\"") + "\""
                : path;
            builder.Append(category).Append(';').AppendLine(cell);
        }
    }

    private static string RenderJson(
        DirectoryComparisonResult result, string leftRoot, string rightRoot, DateTime generatedUtc)
    {
        var payload = new
        {
            Left = leftRoot,
            Right = rightRoot,
            GeneratedUtc = generatedUtc,
            Summary = new
            {
                LeftOnly = result.LeftOnly.Count,
                RightOnly = result.RightOnly.Count,
                Different = result.Different.Count,
                Identical = result.Identical.Count
            },
            LeftOnly = result.LeftOnly,
            RightOnly = result.RightOnly,
            Different = result.Different,
            Identical = result.Identical
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string RenderHtml(
        DirectoryComparisonResult result, string leftRoot, string rightRoot, DateTime generatedUtc)
    {
        string Escape(string value) => WebUtility.HtmlEncode(value);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>Report confronto directory</title>");
        builder.AppendLine("<style>body{font-family:sans-serif;margin:2rem;color:#1f2937}h1{font-size:1.3rem}h2{font-size:1.05rem;margin-top:1.5rem}table{border-collapse:collapse;margin-top:.5rem}td,th{border:1px solid #d1d5db;padding:.3rem .6rem;text-align:left;font-size:.9rem}.empty{color:#6b7280;font-style:italic}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine($"<h1>Report confronto directory</h1>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<p><strong>Sinistra:</strong> {Escape(leftRoot)}<br><strong>Destra:</strong> {Escape(rightRoot)}<br><strong>Generato (UTC):</strong> {generatedUtc:yyyy-MM-dd HH:mm:ss}</p>");
        builder.AppendLine("<table><tr><th>Categoria</th><th>File</th></tr>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>Solo a sinistra</td><td>{result.LeftOnly.Count}</td></tr>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>Solo a destra</td><td>{result.RightOnly.Count}</td></tr>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>Diversi</td><td>{result.Different.Count}</td></tr>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>Identici</td><td>{result.Identical.Count}</td></tr>");
        builder.AppendLine("</table>");

        AppendHtmlSection(builder, "Solo a sinistra", result.LeftOnly, Escape);
        AppendHtmlSection(builder, "Solo a destra", result.RightOnly, Escape);
        AppendHtmlSection(builder, "Diversi", result.Different, Escape);
        AppendHtmlSection(builder, "Identici", result.Identical, Escape);

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void AppendHtmlSection(
        StringBuilder builder, string title, IReadOnlyList<string> paths, Func<string, string> escape)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"<h2>{escape(title)} ({paths.Count})</h2>");
        if (paths.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">Nessun file.</p>");
            return;
        }

        builder.AppendLine("<ul>");
        foreach (var path in paths)
            builder.AppendLine(CultureInfo.InvariantCulture, $"<li>{escape(path)}</li>");
        builder.AppendLine("</ul>");
    }
}
