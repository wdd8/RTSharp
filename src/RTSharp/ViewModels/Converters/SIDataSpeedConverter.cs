using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class SIDataSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch {
                ulong u64 => Shared.Utils.Converters.GetSIDataSize(u64) + "/s",
                long i64 => Shared.Utils.Converters.GetSIDataSize((ulong)i64) + "/s",
                _ => null,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}
