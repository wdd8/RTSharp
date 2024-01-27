using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class ETATimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Shared.Utils.Converters.ToAgoString((TimeSpan)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}
