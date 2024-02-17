using Avalonia.Data.Converters;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RTSharp.ViewModels.Converters
{
    public class ValueConverterGroup : List<IValueConverter>, IValueConverter
    {
        private IList? _parameters;
        private bool _shouldReverse;

        public bool SingleParameter { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            ExtractParameters(parameter);

            if (_shouldReverse) {
                Reverse();
                _shouldReverse = false;
            }

            return this.Aggregate(value, (current, converter) => converter.Convert(current, targetType, GetParameter(converter), culture));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            ExtractParameters(parameter);

            Reverse();
            _shouldReverse = true;

            return this.Aggregate(value, (current, converter) => converter.ConvertBack(current, targetType, GetParameter(converter), culture));
        }

        private void ExtractParameters(object? parameter)
        {
            if (parameter is IList list)
                _parameters = list;

            if (parameter != null)
                _parameters = new[] { parameter };
        }

        private object? GetParameter(IValueConverter converter)
        {
            if (_parameters == null)
                return null;

            var index = SingleParameter ? 0 : IndexOf(converter);
            object? parameter;

            if (index > _parameters.Count - 1)
                parameter = null;
            else
                parameter = _parameters[index];

            return parameter;
        }
    }
}
