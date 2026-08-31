using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Sbroglione.Converters;

/// <summary>
/// Sotto 600px (con margine di 40px sotto MinWidth desktop di 640) la barra dei tab passa da laterale
/// a inferiore: su schermo stretto (telefono in verticale) una colonna di 7 icone a
/// sinistra ruba spazio orizzontale prezioso al contenuto; in basso resta un pattern
/// di navigazione familiare (bottom nav bar) senza sottrarre larghezza.
/// </summary>
public sealed class WidthToTabStripPlacementConverter : IValueConverter
{
    private const double NarrowThreshold = 600.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double width = value switch
        {
            double d and > 0.0 => d,
            _ => double.PositiveInfinity
        };

        return width < NarrowThreshold ? Dock.Bottom : Dock.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
