using Avalonia;

using RTSharp.ViewModels;

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Nito.AsyncEx;
using RTSharp.Models;
using RTSharp.Shared.Controls;

namespace RTSharp.Views
{
	public partial class DirectorySelectorWindow : VmWindow<DirectorySelectorWindowViewModel>
	{
		public DirectorySelectorWindow()
		{
			InitializeComponent();

			BindViewModelActions(vm => {
				vm!.ClearSelection = ClearSelection;
				vm!.CloseDialog = CloseDialog;
				vm!.DoCreateDirectory = CreateDirectory;
			});
		}

		private void ClearSelection()
		{
			MainGrid.SelectedIndex = -1;
			if (MainGrid.SelectedItem != null) {
				throw new Exception();
			}
		}

		private void CloseDialog(string? Input)
		{
			Close(Input!);
		}

		private async Task<bool> CreateDirectory(FileSystemItem Input)
		{
			MainGrid.SelectedItem = Input;
			var col = MainGrid.CurrentColumn;
			col.IsReadOnly = false;

			var ev = new AsyncManualResetEvent();
			bool commited = false;

			void editEnded(object? sender, DataGridCellEditEndedEventArgs e)
			{
				commited = e.EditAction == DataGridEditAction.Commit;
				ev.Set();
			}

			MainGrid.CellEditEnded += editEnded;

			if (!MainGrid.BeginEdit()) {
				return false;
			}
			//((TextBox)col.GetCellContent(Input)).SelectAll();

			await ev.WaitAsync();
			MainGrid.CellEditEnded -= editEnded;
			col.IsReadOnly = true;

			return commited;
		}

		private async void EvDoubleTapped(object sender, TappedEventArgs e)
		{
			var dataGrid = (DataGrid)sender;
			var item = (FileSystemItem)dataGrid.SelectedItem;

			if (item == null)
				return;

			try {
				await ViewModel!.SetCurrentFolder(item.Path);
			} catch { }
		}

		private void EvSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var dataGrid = (DataGrid)sender;
			var item = (FileSystemItem)dataGrid.SelectedItem;
			if (item == null)
				return;

			ViewModel!.SetSelectedItem(item);
		}
	}
}
