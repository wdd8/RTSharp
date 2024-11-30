using Avalonia;
using Avalonia.Controls;

using RTSharp.Core.Util;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

namespace RTSharp.Views.DataGridEx
{
    public partial class DataGridEx : UserControl
    {
        public record ColumnSerializationData(string HeaderId, int DisplayIndex, bool IsVisible, ListSortDirection? Sorting, string Width);
        public record SerializationData(List<ColumnSerializationData> Columns);
        private string LastSerialization;

        public DataGridEx()
        {
            InitializeComponent();
            ItemsSourceProperty.Changed.AddClassHandler<DataGridEx>((x, e) => x.OnItemsSourcePropertyChanged(e));
        }

        public void OnItemsSourcePropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            grid.ItemsSource = ItemsSource;
        }

        public void EvLoadingRow(object sender, DataGridRowEventArgs e)
        {
            LoadingRow(sender, e);
        }

        public void EvSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.SelectedItems.Apply(e);
        }

        public void EvSorting(object sender, DataGridColumnEventArgs e)
        {
            Sorted(sender, e);
        }

        public bool StateChanged()
        {
            return LastSerialization != GetState();
        }

        public string SaveState()
        {
            return LastSerialization = GetState();
        }

        private string GetState()
        {
            var conv = new DataGridLengthConverter();

            return JsonSerializer.Serialize(new SerializationData(
                grid.Columns.Select(x => new ColumnSerializationData(x.Header.ToString()!, x.DisplayIndex, x.IsVisible, x.GetSortDirection(), conv.ConvertToInvariantString(x.Width)!)).ToList()
            ));
        }

        public void RestoreState(string RawData)
        {
            if (String.IsNullOrEmpty(RawData))
                return;

            var conv = new DataGridLengthConverter();
            var data = JsonSerializer.Deserialize<SerializationData>(RawData)!;

            foreach (var col in data.Columns) {
                var realCol = grid.Columns.First(x => x.Header.ToString()! == col.HeaderId);

                realCol.IsVisible = col.IsVisible;
                if (col.Sorting != null) {
                    //realCol.SetSortDirection(col.Sorting.Value);
                    realCol.Sort(col.Sorting.Value);
                }
                
                realCol.DisplayIndex = col.DisplayIndex;
                realCol.Width = (DataGridLength)conv.ConvertFromInvariantString(col.Width)!;
            }
        }

        public event EventHandler<DataGridRowEventArgs> LoadingRow;

        public event EventHandler<DataGridColumnEventArgs> Sorted;

        public ObservableCollectionEx<object> SelectedItems { get; } = new();

        public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
            AvaloniaProperty.Register<DataGrid, IEnumerable>(nameof(ItemsSource));

        public IEnumerable ItemsSource {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public ObservableCollection<DataGridColumn> Columns => grid.Columns;
    }
}
