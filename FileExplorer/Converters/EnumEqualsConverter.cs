using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FileExplorer.Converters;

/// <summary>
/// True se il valore bindato (enum) ha lo stesso nome del parametro.
/// Uso: Classes.success="{Binding StateKind, Converter={StaticResource EnumEquals}, ConverterParameter=Success}".
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null
        && parameter is string name
        && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
