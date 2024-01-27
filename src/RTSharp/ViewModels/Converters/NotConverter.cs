using Avalonia.Data.Converters;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Data;

namespace RTSharp.ViewModels.Converters
{
	public class NotConverter : IValueConverter
	{
		private object InnerConvert(object value)
		{
			if (value is not bool b)
				return BindingOperations.DoNothing;
			return !b;
		}

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => InnerConvert(value);

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => InnerConvert(value);
	}
}
