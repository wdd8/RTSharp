using Avalonia;
using Avalonia.Data.Converters;

using System.Globalization;

namespace RTSharp.Shared.Controls.Converters
{
    /// <summary>
    /// Converts a height into a top-only <see cref="Thickness"/> equal to half that height.
    /// Used to vertically centre the <see cref="Views.GroupBox"/> title over its border line.
    /// </summary>
    public class HalfHeightTopMarginConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var height = value is IConvertible ? System.Convert.ToDouble(value, culture) : 0;
            if (Double.IsNaN(height))
                height = 0;

            return new Thickness(0, height / 2, 0, 0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
