using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sbroglione.Views.Controls;

/// <summary>Un segmento del percorso mostrato: etichetta e percorso cumulativo fino a lì.</summary>
public sealed record BreadcrumbSegment(string Label, string FullPath);

/// <summary>Barra del percorso a segmenti cliccabili, condivisa tra pannello locale e remoto.</summary>
public partial class BreadcrumbBar : UserControl
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<BreadcrumbBar, string?>(nameof(Path));

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>Sollevato con il percorso cumulativo del segmento cliccato.</summary>
    public event EventHandler<string>? SegmentClicked;

    public BreadcrumbBar()
    {
        InitializeComponent();
        this.GetObservable(PathProperty).Subscribe(path =>
            SegmentsItemsControl.ItemsSource = BuildSegments(path));
    }

    /// <summary>
    /// Divide <paramref name="path"/> in segmenti cliccabili con il percorso cumulativo fino a
    /// ciascuno. Riconosce percorsi Unix (radice "/") e Windows (radice "C:\").
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> BuildSegments(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Array.Empty<BreadcrumbSegment>();

        bool isUnix = path.StartsWith("/", StringComparison.Ordinal);
        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<BreadcrumbSegment>();

        if (isUnix)
            segments.Add(new BreadcrumbSegment("/", "/"));

        for (int i = 0; i < parts.Length; i++)
        {
            string cumulative = isUnix
                ? "/" + string.Join('/', parts.Take(i + 1))
                : string.Join(System.IO.Path.DirectorySeparatorChar, parts.Take(i + 1));
            segments.Add(new BreadcrumbSegment(parts[i], cumulative));
        }

        return segments;
    }

    private void OnSegmentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string fullPath })
            SegmentClicked?.Invoke(this, fullPath);
    }
}
