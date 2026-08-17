using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorer.Services;

/// <summary>Rettangolo di layout della treemap, in coordinate assolute.</summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
}

/// <summary>
/// Layout "squarified treemap" (Bruls, Huizing, van Wijk 2000): dispone aree
/// proporzionali ai valori dentro un rettangolo, tenendo i tasselli il più
/// possibile vicini al quadrato. Puro e senza dipendenze UI: testabile in isolamento.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapRect> Compute(
        IReadOnlyList<long> values, double x, double y, double width, double height)
    {
        var result = new TreemapRect[values.Count];
        double total = values.Where(v => v > 0).Sum(v => (double)v);
        if (values.Count == 0 || total <= 0 || width <= 0 || height <= 0)
            return result;

        // Fattore che trasforma un valore nella sua area in pixel quadri.
        double scale = width * height / total;

        // Rettangolo libero residuo.
        double freeX = x, freeY = y, freeWidth = width, freeHeight = height;

        // Riga corrente: indici dei valori e statistiche delle loro aree.
        var row = new List<int>();
        var rowAreas = new List<double>();
        double rowArea = 0, rowMin = double.MaxValue, rowMax = 0;

        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0)
                continue;

            double itemArea = values[i] * scale;
            double side = Math.Min(freeWidth, freeHeight);

            bool startNewRow = row.Count > 0
                && WorstRatio(side, rowArea + itemArea, Math.Min(rowMin, itemArea), Math.Max(rowMax, itemArea))
                   > WorstRatio(side, rowArea, rowMin, rowMax);

            if (startNewRow)
            {
                LayoutRow(result, row, rowAreas, rowArea, ref freeX, ref freeY, ref freeWidth, ref freeHeight);
                row.Clear();
                rowAreas.Clear();
                rowArea = 0;
                rowMin = double.MaxValue;
                rowMax = 0;
            }

            row.Add(i);
            rowAreas.Add(itemArea);
            rowArea += itemArea;
            rowMin = Math.Min(rowMin, itemArea);
            rowMax = Math.Max(rowMax, itemArea);
        }

        if (row.Count > 0)
            LayoutRow(result, row, rowAreas, rowArea, ref freeX, ref freeY, ref freeWidth, ref freeHeight);

        return result;
    }

    /// <summary>
    /// Aspect ratio peggiore tra i tasselli di una riga di area <paramref name="rowArea"/>
    /// disposta lungo un lato di lunghezza <paramref name="side"/>.
    /// </summary>
    private static double WorstRatio(double side, double rowArea, double minArea, double maxArea)
    {
        double side2 = side * side;
        double area2 = rowArea * rowArea;
        return Math.Max(side2 * maxArea / area2, area2 / (side2 * minArea));
    }

    /// <summary>
    /// Dispone la riga corrente come striscia lungo il lato corto del rettangolo
    /// libero e riduce il rettangolo libero di conseguenza.
    /// </summary>
    private static void LayoutRow(
        TreemapRect[] result,
        List<int> row,
        List<double> rowAreas,
        double rowArea,
        ref double freeX,
        ref double freeY,
        ref double freeWidth,
        ref double freeHeight)
    {
        if (freeWidth >= freeHeight)
        {
            // Striscia verticale sul bordo sinistro.
            double stripWidth = rowArea / freeHeight;
            double currentY = freeY;
            for (int k = 0; k < row.Count; k++)
            {
                double itemHeight = rowAreas[k] / stripWidth;
                result[row[k]] = new TreemapRect(freeX, currentY, stripWidth, itemHeight);
                currentY += itemHeight;
            }

            freeX += stripWidth;
            freeWidth -= stripWidth;
        }
        else
        {
            // Striscia orizzontale sul bordo superiore.
            double stripHeight = rowArea / freeWidth;
            double currentX = freeX;
            for (int k = 0; k < row.Count; k++)
            {
                double itemWidth = rowAreas[k] / stripHeight;
                result[row[k]] = new TreemapRect(currentX, freeY, itemWidth, stripHeight);
                currentX += itemWidth;
            }

            freeY += stripHeight;
            freeHeight -= stripHeight;
        }
    }
}
