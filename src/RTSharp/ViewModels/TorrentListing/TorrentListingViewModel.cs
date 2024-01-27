using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Torrent = RTSharp.Models.Torrent;
using RTSharp.Core.TorrentPolling;
using RTSharp.Views;
using RTSharp.Core;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Serilog;
using Avalonia.Data;
using RTSharp.ViewModels.Options;
using RTSharp.Views.Options;
using Avalonia.Controls.Primitives;
using RTSharp.Shared.Abstractions;
using System.Collections.Concurrent;
using Dock.Model.Mvvm.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using System.Collections.Specialized;
using RTSharp.Core.Util;
using RTSharp.Views.DataGridEx;
using RTSharp.Views.TorrentListing;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel : Document, IContextPopulatedNotifyable
	{
        private ObservableCollectionEx<Torrent> Torrents => (ObservableCollectionEx<Torrent>)Context!;

		private ObservableCollectionEx<Torrent> FilteredTorrents { get; set; } = new();

		public DataGridEx DataGrid { get; set; }

		ObservableCollectionEx<object>? CurrentlySelectedItems => DataGrid?.SelectedItems;

		private CancellationTokenSource? SelectionChange { get; set; }

		[ObservableProperty]
		private string stringFilter;

		[ObservableProperty]
		private TemplatedControl[] labelsWithAdd;

		private GeneralTorrentInfoViewModel GeneralInfoViewModel { get; } = new();

		private TorrentFilesViewModel FilesViewModel { get; } = new();

		private TorrentPeersViewModel PeersViewModel { get; } = new();

		private TorrentTrackersViewModel TrackersViewModel { get; }

		private static readonly DrawingImage DefaultImage;

		private bool StartTorrentAllowed => CurrentlySelectedItems.Any() && ((Torrent)CurrentlySelectedItems[0]).InternalState != TORRENT_STATE.SEEDING;

		public Action ResortList { get; set; }

		static TorrentListingViewModel()
		{
			DefaultImage = new DrawingImage() {
				Drawing = new GeometryDrawing() {
					Geometry = FontAwesomeIcons.Get("fa-solid fa-globe"),
					Brush = Brushes.White
				}
			};
		}

		private List<IDisposable> Disposables = new();

		public TorrentListingViewModel()
        {
			this.TrackersViewModel = new(this);
		}

		~TorrentListingViewModel()
		{
			foreach (var item in Disposables)
				item.Dispose();

			Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
			CurrentlySelectedItems.CollectionChanged -= CurrentlySelectedTorrents_CollectionChanged;
		}

		public void OnViewModelAttached(TorrentListingView View)
		{
			this.DataGrid = View.grid;

			CurrentlySelectedItems.CollectionChanged -= CurrentlySelectedTorrents_CollectionChanged;
			CurrentlySelectedItems.CollectionChanged += CurrentlySelectedTorrents_CollectionChanged;
		}

		public void OnTorrentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => EvaluateFilters();

		partial void OnStringFilterChanged(string value) => EvaluateFilters();

		[RelayCommand]
		public void ShowAddLabel()
		{
			ShowAddLabelDialog();
		}

		[RelayCommand]
		public void ShowOptions()
		{
			var optionsWindow = new OptionsWindow() {
				DataContext = new OptionsViewModel()
			};
			optionsWindow.Show();
		}

		public override bool OnClose()
		{
			return true;
		}

		private TemplatedControl[] LabelsControlCached;

		private TemplatedControl[] GetLabelsWithAdd(string[] AllLabels)
		{
			var ret = new List<TemplatedControl>();

			if (CurrentlySelectedItems == null)
				return Array.Empty<TemplatedControl>();

			var current = CurrentlySelectedItems.Cast<Torrent>().ToArray();

			foreach (var label in AllLabels) {
				bool checkedAll = current.All(x => x.Labels.Contains(label));

				ret.Add(new MenuItem {
					Icon = new CheckBox() {
						BorderThickness = new Avalonia.Thickness(0),
						IsHitTestVisible = true,
						Command = ToggleLabelCommand,
						CommandParameter = (current, label, checkedAll),
						IsChecked = checkedAll
					},
					Header = label
				});
			}

			if (ret.Count > 0)
				ret.Add(new Separator());

			ret.Add(new MenuItem {
				Command = ShowAddLabelCommand,
				Header = "Add..."
			});

			return LabelsControlCached = ret.ToArray();
		}

		private void UpdateTorrentInChildViewModels(ObservableCollectionEx<object>? NewTorrents)
		{
			if (NewTorrents?.Count != 1) {
				GeneralInfoViewModel.Torrent = null;
				FilesViewModel.Torrent = null;
				FilesViewModel.Files.Clear();
				PeersViewModel.Peers.Clear();
				TrackersViewModel.Trackers.Clear();
				return;
			}

			var newTorrent = (Torrent)NewTorrents[0];

			GeneralInfoViewModel.Torrent = newTorrent;
			FilesViewModel.Torrent = newTorrent;
		}

		private Task? TorrentTabsTasks;
		private SemaphoreSlim TorrentTabsSwitch = new(1, 1);
		private async Task PollTorrentInfo(ObservableCollectionEx<object>? NewTorrents)
		{
			await TorrentTabsSwitch.WaitAsync(); {
				var newTorrent = NewTorrents?.Count != 1 ? null : (Torrent)NewTorrents[0];

				if (SelectionChange == null)
					SelectionChange = new CancellationTokenSource();
				else {
					SelectionChange.Cancel();

					var sw = Stopwatch.StartNew();
					try {
						if (TorrentTabsTasks != null) {
							await TorrentTabsTasks;
						}
					} catch (TaskCanceledException) {
					} catch (Exception ex) {
						Log.Logger.Error(ex, "One of torrent tasks failed");
					} finally {
                        TorrentTabsTasks = null;
                    }
					if (sw.Elapsed > TimeSpan.FromMilliseconds(100))
						Log.Logger.Warning($"Torrent tasks are taking too long to complete ({Shared.Utils.Converters.ToAgoString(sw.Elapsed)})");
				}

				if (newTorrent != null) {
					SelectionChange = new CancellationTokenSource();

					Debug.Assert(TorrentTabsTasks == null);

					Log.Logger.Information("Starting tasks for: " + newTorrent.Name);
					TorrentTabsTasks = Task.WhenAll(
						FilesTasks(newTorrent, SelectionChange.Token),
						PeersTasks(newTorrent, SelectionChange.Token),
						TrackersTasks(newTorrent, SelectionChange.Token)
					);
				}
			} TorrentTabsSwitch.Release();
		}

		private void EvaluateFilters()
		{
			if (Torrents == null)
				return;

			this.OnPropertyChanging(nameof(FilteredTorrents));

			if (String.IsNullOrEmpty(StringFilter)) {
				FilteredTorrents.Replace(Torrents);
			} else {
				var newTorrents = new ConcurrentBag<Torrent>();
				Parallel.ForEach(Torrents, x => {
					if (x.Name.Contains(StringFilter, StringComparison.OrdinalIgnoreCase)) {
						newTorrents.Add(x);
					} else if (x.TrackerDisplayName != null && x.TrackerDisplayName.Contains(StringFilter, StringComparison.OrdinalIgnoreCase)) {
						newTorrents.Add(x);
					}
				});
				FilteredTorrents.Replace(newTorrents);
			}

			this.OnPropertyChanged(nameof(FilteredTorrents));

			ResortList?.Invoke();
		}

		public void OnContextPopulated()
		{
			Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
			Torrents.CollectionChanged += OnTorrentsCollectionChanged;

            Disposables.Add(TorrentPolling.AllLabelReferencesObservable.Subscribe(x => LabelsWithAdd = GetLabelsWithAdd(x)));

			EvaluateFilters();
		}

		private void CurrentlySelectedTorrents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			UpdateTorrentInChildViewModels(this.CurrentlySelectedItems);
			_ = PollTorrentInfo(this.CurrentlySelectedItems);
			this.OnPropertyChanged(nameof(StartTorrentAllowed));
			this.OnPropertyChanged(nameof(CurrentlySelectedItems));
			StartTorrentsCommand.NotifyCanExecuteChanged();
			StopTorrentsCommand.NotifyCanExecuteChanged();
			PauseTorrentsCommand.NotifyCanExecuteChanged();
			ForceRecheckTorrentsCommand.NotifyCanExecuteChanged();
			ReannounceToAllTrackersCommand.NotifyCanExecuteChanged();
			MoveDownloadDirectoryCommand.NotifyCanExecuteChanged();
			RemoveTorrentsCommand.NotifyCanExecuteChanged();
			RemoveTorrentsAndDataCommand.NotifyCanExecuteChanged();
			GetDotTorrentsCommand.NotifyCanExecuteChanged();
			AddLabelCommand.NotifyCanExecuteChanged();

            LabelsWithAdd = GetLabelsWithAdd(TorrentPolling.AllLabelReferences.Keys.ToArray());
        }
	}

	public static class ExampleTorrentListingViewModel
	{
        public static TorrentListingViewModel ViewModel { get; } = new TorrentListingViewModel();
    }
}
