using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace FileExplorer.Converters;

/// <summary>
/// True se nessuno dei valori bindati (bool) è true. Usato per abilitare i comandi solo quando
/// tutte le guardie di rientranza della viewmodel (es. IsBusy, IsDownloading) sono spente.
/// Uso: IsEnabled con MultiBinding su più proprietà bool, Converter="{StaticResource NotAny}".
/// </summary>
public class NotAnyConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        !values.Any(v => v is true);
}
