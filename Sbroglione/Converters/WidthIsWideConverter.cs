using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Sbroglione.Converters;

/// <summary>
/// Stessa soglia di WidthToTabStripPlacementConverter (600px): sopra è la sidebar
/// desktop (etichette + toggle espandi/collassa), sotto il bottom-nav mobile a sole
/// icone su riga unica (nessuna label, nessun toggle: non c'è spazio da risparmiare
/// collassando orizzontalmente una barra già in basso).
/// </summary>
public sealed class WidthIsWideConverter : IValueConverter
{
    private const double NarrowThreshold = 600.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double width = value switch
        {
            double d and > 0.0 => d,
            _ => double.PositiveInfinity
        };

        bool isWide = width >= NarrowThreshold;
        return parameter is "Invert" ? !isWide : isWide;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
