using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class StringJoiner : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            return String.Join(", ", ((IEnumerable<string>)value));
        }

        public object? ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            return ((string)value).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
