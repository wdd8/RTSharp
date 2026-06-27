using Avalonia.Data.Converters;
using Avalonia.Media;

using System.Globalization;

namespace RTSharp.Shared.Controls.Converters;

public class ColorStringToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string color && !string.IsNullOrEmpty(color))
            return new SolidColorBrush(Color.Parse(color));

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new InvalidOperationException();
    }
}
