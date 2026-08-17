using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Views;

/// <summary>
/// Treemap dei figli di un <see cref="DiskUsageNode"/>: un Border per tassello
/// (tooltip nativo, click per drill-down), layout squarified ricalcolato al
/// cambio di nodo o di dimensioni.
/// </summary>
public class TreemapControl : Canvas
{
    public static readonly StyledProperty<DiskUsageNode?> NodeProperty =
        AvaloniaProperty.Register<TreemapControl, DiskUsageNode?>(nameof(Node));

    public DiskUsageNode? Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    /// <summary>Scatta al click su un tassello (la vista lo inoltra al ViewModel).</summary>
    public event Action<DiskUsageNode>? NodeActivated;

    public TreemapControl()
    {
        SizeChanged += (_, _) => Rebuild();
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodeProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();

        if (Node is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var nodes = Node.Children
            .Where(child => child.SizeBytes > 0)
            .OrderByDescending(child => child.SizeBytes)
            .ToList();
        if (nodes.Count == 0)
            return;

        var rects = TreemapLayout.Compute(
            nodes.Select(child => child.SizeBytes).ToList(),
            0, 0, Bounds.Width, Bounds.Height);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var rect = rects[i];
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            var border = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                Background = FindTreemapBrush(i),
                BorderBrush = this.FindResource(ActualThemeVariant, "Brush.CardBorder") as IBrush,
                BorderThickness = new Thickness(1)
            };

            if (rect.Width >= 60 && rect.Height >= 24)
            {
                border.Child = new TextBlock
                {
                    Text = node.Name,
                    FontSize = 11,
                    Foreground = this.FindResource(ActualThemeVariant, "Brush.TextPrimary") as IBrush,
                    Margin = new Thickness(4, 2),
                    VerticalAlignment = VerticalAlignment.Top,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
            }

            ToolTip.SetTip(border, $"{node.Name} — {SizeFormatter.Format(node.SizeBytes)}");
            border.PointerPressed += (_, _) => NodeActivated?.Invoke(node);

            SetLeft(border, rect.X);
            SetTop(border, rect.Y);
            Children.Add(border);
        }
    }

    private IBrush? FindTreemapBrush(int index) =>
        this.FindResource(ActualThemeVariant, $"Brush.Treemap.{index % 6 + 1}") as IBrush;
}
