using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Views;

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

    private const int MaxTiles = 400;

    private readonly DispatcherTimer _resizeDebounce;

    public TreemapControl()
    {
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _resizeDebounce.Tick += (_, _) => { _resizeDebounce.Stop(); Rebuild(); };
        SizeChanged += (_, _) => { _resizeDebounce.Stop(); _resizeDebounce.Start(); };
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

        var ordered = Node.Children
            .Where(child => child.SizeBytes > 0)
            .OrderByDescending(child => child.SizeBytes)
            .ToList();
        if (ordered.Count == 0)
            return;

        var (nodes, hiddenCount, hiddenBytes) = CapNodes(ordered, MaxTiles);

        var sizes = nodes.Select(child => child.SizeBytes).ToList();
        if (hiddenCount > 0)
            sizes.Add(hiddenBytes);

        var rects = TreemapLayout.Compute(sizes, 0, 0, Bounds.Width, Bounds.Height);

        int tileCount = nodes.Count + (hiddenCount > 0 ? 1 : 0);
        for (int i = 0; i < tileCount; i++)
        {
            var rect = rects[i];
            if (rect.Width < 1 || rect.Height < 1)
                continue;

            bool isAggregate = hiddenCount > 0 && i == nodes.Count;

            var border = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                Background = isAggregate
                    ? this.FindResource(ActualThemeVariant, "Brush.Treemap.6") as IBrush
                    : FindTreemapBrush(i),
                BorderBrush = this.FindResource(ActualThemeVariant, "Brush.CardBorder") as IBrush,
                BorderThickness = new Thickness(1)
            };

            if (isAggregate)
            {
                ToolTip.SetTip(border, string.Format(LocalizationService.Tr("Str.DiskUsage.MoreItemsTooltipFormat"), hiddenCount, SizeFormatter.Format(hiddenBytes)));
            }
            else
            {
                var node = nodes[i];
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

                ToolTip.SetTip(border, string.Format(LocalizationService.Tr("Str.DiskUsage.NodeTooltipFormat"), node.Name, SizeFormatter.Format(node.SizeBytes)));
                border.PointerPressed += (_, _) => NodeActivated?.Invoke(node);
            }

            SetLeft(border, rect.X);
            SetTop(border, rect.Y);
            Children.Add(border);
        }
    }

    private IBrush? FindTreemapBrush(int index) =>
        this.FindResource(ActualThemeVariant, $"Brush.Treemap.{index % 6 + 1}") as IBrush;

    /// <summary>
    /// Limita i tasselli renderizzati a <paramref name="maxTiles"/>, tenendo i più grandi
    /// (ordinati per <see cref="DiskUsageNode.SizeBytes"/> decrescente) e aggregando il resto.
    /// </summary>
    internal static (List<DiskUsageNode> Visible, int HiddenCount, long HiddenBytes) CapNodes(
        IReadOnlyList<DiskUsageNode> children, int maxTiles)
    {
        var ordered = children.OrderByDescending(child => child.SizeBytes).ToList();
        if (ordered.Count <= maxTiles)
            return (ordered, 0, 0L);

        var visible = ordered.Take(maxTiles).ToList();
        var hidden = ordered.Skip(maxTiles).ToList();
        return (visible, hidden.Count, hidden.Sum(child => child.SizeBytes));
    }
}
