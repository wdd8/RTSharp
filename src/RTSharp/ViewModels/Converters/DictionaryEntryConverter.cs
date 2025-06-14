using Avalonia.Data;
using Avalonia.Data.Converters;

using RTSharp.Shared.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace RTSharp.ViewModels.Converters
{
    public class DictionaryEntryConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, System.Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 2)
                return BindingOperations.DoNothing;

            if (values[0] is IDictionary dictionary) {
                var value = dictionary[values[1]!];

                if (values.Count == 3) {
                    var key = values[2];
                    
                    if (value is INotifyable notifyable) {
                        return notifyable.ValueStr;
                    }
                }
                
                return value;
            }

            return BindingOperations.DoNothing;
        }
    }
}
