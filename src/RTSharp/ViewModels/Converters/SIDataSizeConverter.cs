using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class SIDataSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "";

            return Shared.Utils.Converters.GetSIDataSize((ulong)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}
