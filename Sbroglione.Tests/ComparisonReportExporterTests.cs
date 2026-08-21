using System;
using System.Linq;
using System.Text.Json;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public sealed class ComparisonReportExporterTests
{
    private static readonly string[] LeftOnlyPaths = { "solo-sx.txt" };
    private static readonly string[] RightOnlyPaths = { "solo-dx.txt" };
    private static readonly string[] DifferentPaths = { "diverso.txt" };
    private static readonly string[] IdenticalPaths = { "uguale.txt" };

    private static DirectoryComparisonResult SampleResult() =>
        new(LeftOnlyPaths, RightOnlyPaths, DifferentPaths, IdenticalPaths);

    private static readonly DateTime GeneratedUtc = new(2026, 8, 18, 15, 30, 0, DateTimeKind.Utc);

    private static readonly string[] HtmlUnsafeLeftOnlyPaths = { "cattivo<script>.txt" };

    [Fact]
    public void Render_Csv_HasHeaderAndOneRowPerFile()
    {
        string csv = ComparisonReportExporter.Render(
            SampleResult(), ComparisonReportFormat.Csv, "/sx", "/dx", GeneratedUtc);

        string[] lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("categoria;percorso", lines[0]);
        Assert.Contains("solo-a-sinistra;solo-sx.txt", lines);
        Assert.Contains("solo-a-destra;solo-dx.txt", lines);
        Assert.Contains("diversi;diverso.txt", lines);
        Assert.Contains("identici;uguale.txt", lines);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void Render_Json_RoundTrips()
    {
        string json = ComparisonReportExporter.Render(
            SampleResult(), ComparisonReportFormat.Json, "/sx", "/dx", GeneratedUtc);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("/sx", root.GetProperty("left").GetString());
        Assert.Equal("solo-sx.txt", root.GetProperty("leftOnly")[0].GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("different").GetInt32());
    }

    [Fact]
    public void Render_Html_ContainsSummaryAndEscapesPaths()
    {
        var result = new DirectoryComparisonResult(
            HtmlUnsafeLeftOnlyPaths, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>());

        string html = ComparisonReportExporter.Render(
            result, ComparisonReportFormat.Html, "/sx", "/dx", GeneratedUtc);

        Assert.Contains("Solo a sinistra", html);
        Assert.Contains("cattivo&lt;script&gt;.txt", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void SuggestFileName_UsesTimestampAndExtension()
    {
        Assert.Equal("confronto-20260818-153000.csv",
            ComparisonReportExporter.SuggestFileName(ComparisonReportFormat.Csv, GeneratedUtc));
    }
}
