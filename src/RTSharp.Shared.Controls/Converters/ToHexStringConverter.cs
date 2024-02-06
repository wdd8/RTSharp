using Avalonia.Data.Converters;
using Avalonia.Platform;
using Avalonia;

using System.Globalization;

namespace RTSharp.Shared.Controls.Converters
{
    public class ToHexStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            if (value is not byte[] b)
                return value.ToString();

            return System.Convert.ToHexString(b);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
