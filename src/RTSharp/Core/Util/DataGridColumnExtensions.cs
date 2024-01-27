using Avalonia.Controls;

using System.ComponentModel;
using System.Reflection;

namespace RTSharp.Core.Util
{
	public static class DataGridColumnExtensions
	{
		static readonly PropertyInfo HeaderCellPropertyInfo = typeof(DataGridColumn).GetProperty("HeaderCell", BindingFlags.NonPublic | BindingFlags.Instance)!;
		static readonly PropertyInfo CurrentSortingStatePropertyInfo = typeof(DataGridColumnHeader).GetProperty("CurrentSortingState", BindingFlags.NonPublic | BindingFlags.Instance)!;

		public static ListSortDirection? GetSortDirection(this DataGridColumn In)
		{
			var header = (DataGridColumnHeader)HeaderCellPropertyInfo.GetValue(In)!;
			return (ListSortDirection?)CurrentSortingStatePropertyInfo.GetValue(header);
		}
	}
}
