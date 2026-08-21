using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Views;

/// <summary>
/// Vista gerarchica alternativa alla treemap per <see cref="DiskUsageNode"/>: un albero
/// verticale scorrevole, una riga per cartella/file con barra inline proporzionale
/// all'occupazione e freccia di espansione. Lo stato di espansione è tenuto per
/// riferimento nodo (il Model resta senza stato di presentazione).
/// </summary>
public class HierarchyListControl : Decorator
{
    public static readonly StyledProperty<DiskUsageNode?> NodeProperty =
        AvaloniaProperty.Register<HierarchyListControl, DiskUsageNode?>(nameof(Node));

    public DiskUsageNode? Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    /// <summary>Scatta al click sul nome di un tassello (la vista lo inoltra al ViewModel, come il drill-down della treemap).</summary>
    public event Action<DiskUsageNode>? NodeActivated;

    private const int MaxChildrenPerLevel = 400;
    private const double IndentPerLevel = 18;

    private readonly HashSet<DiskUsageNode> _expanded = new();
    private readonly StackPanel _rows;

    public HierarchyListControl()
    {
        // ScrollViewer subclassato non riceve il ControlTemplate del tema Fluent (i selettori
        // matchano il tipo esatto, non le sottoclassi): niente PART_ContentPresenter, niente
        // scroll funzionante. Componiamo invece un'istanza reale di ScrollViewer come figlio.
        _rows = new StackPanel { Orientation = Orientation.Vertical };
        Child = new ScrollViewer { Content = _rows };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodeProperty)
        {
            _expanded.Clear();
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        if (Node is null)
            return;

        BuildRows(Node, depth: 0, parentSizeBytes: Node.SizeBytes);
    }

    private void BuildRows(DiskUsageNode parent, int depth, long parentSizeBytes)
    {
        var ordered = parent.Children
            .Where(child => child.SizeBytes > 0)
            .OrderByDescending(child => child.SizeBytes)
            .ToList();
        if (ordered.Count == 0)
            return;

        var (visible, hiddenCount, hiddenBytes) = TreemapControl.CapNodes(ordered, MaxChildrenPerLevel);

        foreach (var child in visible)
        {
            AddRow(child, depth, parentSizeBytes, isAggregate: false, hiddenCount: 0, hiddenBytes: 0);
            if (child.IsDirectory && _expanded.Contains(child))
                BuildRows(child, depth + 1, child.SizeBytes);
        }

        if (hiddenCount > 0)
            AddRow(null, depth, parentSizeBytes, isAggregate: true, hiddenCount, hiddenBytes);
    }

    private void AddRow(DiskUsageNode? node, int depth, long parentSizeBytes, bool isAggregate, int hiddenCount, long hiddenBytes)
    {
        long sizeBytes = isAggregate ? hiddenBytes : node!.SizeBytes;
        double pct = parentSizeBytes > 0 ? Math.Clamp((double)sizeBytes / parentSizeBytes * 100.0, 0, 100) : 0;

        bool expandable = !isAggregate && node!.IsDirectory && node.Children.Any(c => c.SizeBytes > 0);
        bool isExpanded = expandable && _expanded.Contains(node!);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,*,90"),
            Margin = new Thickness(depth * IndentPerLevel, 2, 4, 2)
        };

        var arrow = new TextBlock
        {
            Text = expandable ? (isExpanded ? "▾" : "▸") : "",
            Width = 18,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextMuted") as IBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 0);
        grid.Children.Add(arrow);

        var barCell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(new GridLength(Math.Max(pct, 0.01), GridUnitType.Star)),
                new ColumnDefinition(new GridLength(Math.Max(100 - pct, 0.01), GridUnitType.Star))
            }
        };
        var fill = new Border
        {
            Background = this.FindResource(ActualThemeVariant, isAggregate ? "Brush.Treemap.6" : "Brush.Accent") as IBrush,
            CornerRadius = new CornerRadius(2),
            Height = 14,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(fill, 0);
        barCell.Children.Add(fill);

        string label = isAggregate
            ? string.Format(LocalizationService.Tr("Str.DiskUsage.MoreItemsTooltipFormat"), hiddenCount, SizeFormatter.Format(hiddenBytes))
            : node!.Name;

        var nameText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextPrimary") as IBrush
        };
        Grid.SetColumn(nameText, 1);
        barCell.Children.Add(nameText);
        Grid.SetColumn(barCell, 1);
        grid.Children.Add(barCell);

        var sizeText = new TextBlock
        {
            Text = $"{SizeFormatter.Format(sizeBytes)} ({pct:0.#}%)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextMuted") as IBrush
        };
        Grid.SetColumn(sizeText, 2);
        grid.Children.Add(sizeText);

        if (!isAggregate)
        {
            ToolTip.SetTip(grid, string.Format(LocalizationService.Tr("Str.DiskUsage.NodeTooltipFormat"), node!.Name, SizeFormatter.Format(node.SizeBytes)));

            if (expandable)
            {
                grid.PointerPressed += (_, _) =>
                {
                    if (!_expanded.Remove(node))
                        _expanded.Add(node);
                    Rebuild();
                };
            }
            else if (!node.IsDirectory)
            {
                grid.PointerPressed += (_, _) => NodeActivated?.Invoke(node);
            }
        }
        else
        {
            ToolTip.SetTip(grid, label);
        }

        _rows.Children.Add(grid);
    }
}
