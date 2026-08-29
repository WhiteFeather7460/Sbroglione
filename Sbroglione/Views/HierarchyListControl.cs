using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Sbroglione.Models;
using Sbroglione.Services;

namespace Sbroglione.Views;

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
    private const double BarWidth = 60;
    private const double BarHeight = 14;

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

    /// <summary>Rilegge <see cref="Node"/> e ridisegna le righe: usato dopo un aggiornamento a
    /// strati in cui l'oggetto <see cref="Node"/> non è cambiato (quindi <c>OnPropertyChanged</c>
    /// non scatterebbe da solo).</summary>
    public void Refresh() => Rebuild();

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
            .Where(child => child.SizeBytes > 0 || (child.IsDirectory && child.IsPending))
            .OrderByDescending(child => child.SizeBytes)
            .ToList();
        if (ordered.Count == 0)
            return;

        var (visible, hiddenCount, hiddenBytes) = CapNodes(ordered, MaxChildrenPerLevel);

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
            ColumnDefinitions = new ColumnDefinitions("18,*,Auto,80"),
            Margin = new Thickness(depth * IndentPerLevel, 2, 4, 2)
        };

        bool isPendingFolder = !isAggregate && node!.IsDirectory && node.IsPending;

        var arrow = new TextBlock
        {
            Text = isPendingFolder ? "◐" : (expandable ? (isExpanded ? "▾" : "▸") : ""),
            Width = 18,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextMuted") as IBrush,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center
        };
        Grid.SetColumn(arrow, 0);
        grid.Children.Add(arrow);

        if (isPendingFolder)
        {
            arrow.RenderTransform = new RotateTransform(0);
            RunSpinnerAnimation(arrow);
        }

        string label = isAggregate
            ? string.Format(LocalizationService.Tr("Str.DiskUsage.MoreItemsTooltipFormat"), hiddenCount, SizeFormatter.Format(hiddenBytes))
            : node!.Name;

        var nameText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextPrimary") as IBrush
        };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        var barTrack = new Grid
        {
            Width = BarWidth,
            Height = BarHeight,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var track = new Border
        {
            Background = this.FindResource(ActualThemeVariant, "Brush.CardBorder") as IBrush,
            CornerRadius = new CornerRadius(2)
        };
        barTrack.Children.Add(track);

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
            CornerRadius = new CornerRadius(2)
        };
        Grid.SetColumn(fill, 0);
        barCell.Children.Add(fill);
        barTrack.Children.Add(barCell);

        var pctText = new TextBlock
        {
            Text = $"{pct:0}%",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextPrimary") as IBrush
        };
        barTrack.Children.Add(pctText);

        Grid.SetColumn(barTrack, 2);
        grid.Children.Add(barTrack);

        var sizeText = new TextBlock
        {
            Text = SizeFormatter.Format(sizeBytes),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource(ActualThemeVariant, "Brush.TextMuted") as IBrush
        };
        Grid.SetColumn(sizeText, 3);
        grid.Children.Add(sizeText);

        if (!isAggregate)
        {
            ToolTip.SetTip(grid, string.Format(LocalizationService.Tr("Str.DiskUsage.NodeTooltipFormat"), node!.Name, SizeFormatter.Format(node.SizeBytes)));
            grid.ContextMenu = BuildContextMenu(node);

            if (expandable)
            {
                grid.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
                        return;
                    if (!_expanded.Remove(node))
                        _expanded.Add(node);
                    Rebuild();
                };
            }
            else if (!node.IsDirectory)
            {
                grid.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
                        return;
                    NodeActivated?.Invoke(node);
                };
            }
        }
        else
        {
            ToolTip.SetTip(grid, label);
        }

        _rows.Children.Add(grid);
    }

    private ContextMenu BuildContextMenu(DiskUsageNode node)
    {
        var openFolder = new MenuItem { Header = LocalizationService.Tr("Str.DiskUsage.OpenFolder") };
        openFolder.Click += (_, _) =>
            FileManagerLauncher.OpenFolder(node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath) ?? node.FullPath);

        var copyPath = new MenuItem { Header = LocalizationService.Tr("Str.DiskUsage.CopyPath") };
        copyPath.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(node.FullPath);
        };

        var reveal = new MenuItem { Header = LocalizationService.Tr("Str.DiskUsage.RevealInFileManager") };
        reveal.Click += (_, _) => FileManagerLauncher.RevealInFileManager(node.FullPath);

        return new ContextMenu
        {
            ItemsSource = new[] { openFolder, copyPath, reveal }
        };
    }

    /// <summary>Rotazione continua a 1 giro/secondo per la riga di una cartella non ancora scansionata.</summary>
    private static void RunSpinnerAnimation(TextBlock spinner)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Easing = new LinearEasing(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } }
            }
        };
        _ = animation.RunAsync(spinner);
    }

    /// <summary>
    /// Limita le righe renderizzate a <paramref name="maxTiles"/>, tenendo le più grandi
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
