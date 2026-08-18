using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileExplorer.Views;

/// <summary>
/// Sparkline minimale: polilinea dei campioni (MB/s) normalizzata sull'altezza
/// disponibile, con riempimento sotto la curva. Nessun asse, nessuna label.
/// </summary>
public class SparklineControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<SparklineControl, IReadOnlyList<double>?>(nameof(Samples));

    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    static SparklineControl()
    {
        AffectsRender<SparklineControl>(SamplesProperty);
    }

    public SparklineControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var samples = Samples;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (samples is null || samples.Count < 2 || width <= 0 || height <= 0)
            return;

        double max = 0;
        foreach (var sample in samples)
            if (double.IsFinite(sample) && sample > max)
                max = sample;
        if (max <= 0)
            return;

        var lineBrush = this.FindResource(ActualThemeVariant, "Brush.Sparkline.Line") as IBrush;
        var fillBrush = this.FindResource(ActualThemeVariant, "Brush.Sparkline.Fill") as IBrush;
        if (lineBrush is null)
            return;

        double stepX = width / (samples.Count - 1);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(new Point(0, height), isFilled: true);
            for (int i = 0; i < samples.Count; i++)
            {
                double value = double.IsFinite(samples[i]) ? Math.Max(0, samples[i]) : 0;
                geometryContext.LineTo(new Point(i * stepX, height - value / max * height));
            }
            geometryContext.LineTo(new Point(width, height));
            geometryContext.EndFigure(isClosed: true);
        }

        if (fillBrush is not null)
            context.DrawGeometry(fillBrush, null, geometry);

        var pen = new Pen(lineBrush, 1.5);
        for (int i = 1; i < samples.Count; i++)
        {
            double value0 = double.IsFinite(samples[i - 1]) ? Math.Max(0, samples[i - 1]) : 0;
            double value1 = double.IsFinite(samples[i]) ? Math.Max(0, samples[i]) : 0;
            context.DrawLine(pen,
                new Point((i - 1) * stepX, height - value0 / max * height),
                new Point(i * stepX, height - value1 / max * height));
        }
    }
}
