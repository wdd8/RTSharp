using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class SIDataSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value) {
                case ulong u64:
					return Shared.Utils.Converters.GetSIDataSize(u64) + "/s";
				case long i64:
					return Shared.Utils.Converters.GetSIDataSize((ulong)i64) + "/s";
			}
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}
