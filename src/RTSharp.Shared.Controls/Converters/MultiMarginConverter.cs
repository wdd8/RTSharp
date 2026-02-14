using Avalonia;
using Avalonia.Data.Converters;

using System.Globalization;

namespace RTSharp.Shared.Controls.Converters
{
    class MultiMarginConverter : IMultiValueConverter
    {
        double ToDouble(object? value)
        {
            if (value is IConvertible)
            {
                var ret = System.Convert.ToDouble(value);
                if (Double.IsNaN(ret))
                    return 0;
                return ret;
            }
            return 0;
        }

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            return new Thickness(ToDouble(values[0]),
                                 ToDouble(values[1]),
                                 ToDouble(values[2]),
                                 ToDouble(values[3]));
        }
    }
}
