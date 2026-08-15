using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FileExplorer.Models;

namespace FileExplorer.Converters;

/// <summary>Traduce <see cref="LocalFileStatus"/> nel testo italiano mostrato nella colonna "Su disco".</summary>
public class LocalFileStatusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            LocalFileStatus.Missing => "Mancante",
            LocalFileStatus.Present => "Presente",
            LocalFileStatus.Different => "Diverso",
            _ => null
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
