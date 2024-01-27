using Avalonia;
using Avalonia.Data.Converters;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.Converters
{
	public class ItemsParamConverter : IMultiValueConverter
	{
		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		{
			if (values[0] is UnsetValueType)
				return null;

			return ((IList)values[0], (string)values[1]);
		}
	}
}
