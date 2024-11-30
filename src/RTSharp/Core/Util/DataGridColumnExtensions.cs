using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace RTSharp.Core.Util
{
    public static class DataGridColumnExtensions
    {
        static readonly PropertyInfo HeaderCellPropertyInfo = typeof(DataGridColumn).GetProperty("HeaderCell", BindingFlags.NonPublic | BindingFlags.Instance)!;
        static readonly PropertyInfo CurrentSortingStatePropertyInfo = typeof(DataGridColumnHeader).GetProperty("CurrentSortingState", BindingFlags.NonPublic | BindingFlags.Instance)!;

        static readonly MethodInfo ProcessSortMethodInfo = typeof(DataGridColumnHeader).GetMethod("ProcessSort", BindingFlags.NonPublic | BindingFlags.Instance)!;

        public static ListSortDirection? GetSortDirection(this DataGridColumn In)
        {
            var header = (DataGridColumnHeader)HeaderCellPropertyInfo.GetValue(In)!;
            return (ListSortDirection?)CurrentSortingStatePropertyInfo.GetValue(header);
        }

        /*public static void SetSortDirection(this DataGridColumn In, ListSortDirection? Value)
        {
            var header = (DataGridColumnHeader)HeaderCellPropertyInfo.GetValue(In)!;

            ProcessSortMethodInfo.Invoke(header, [ KeyModifiers.None, Value ]);

            //CurrentSortingStatePropertyInfo.SetValue(header, Value);
        }*/

        public static string GetBoundPropertyPath(this DataGridColumn In)
        {
            if (In is DataGridTextColumn txt) {
                return ((Binding)txt.Binding).Path;
            }
            if (In is DataGridTemplateColumn tmpl) {
                return null;
            }

            Debug.Assert(false);
            return null;
        }
    }
}
