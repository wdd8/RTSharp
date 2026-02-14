using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia.Data.Converters;

using RTSharp.Models;

namespace RTSharp.Views.Util
{
    public class SelectedItemsToPluginModelConverter : IValueConverter
    {
        public static readonly SelectedItemsToPluginModelConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is System.Collections.IEnumerable items) {
                var ret = new List<RTSharp.Shared.Abstractions.Torrent>();
                foreach (var x in items) {
                    if (x is Torrent t) {
                        ret.Add(t.ToPluginModel());
                    }
                }
                return ret.AsReadOnly();
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Avalonia.Data.BindingOperations.DoNothing;
    }
}