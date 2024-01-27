using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;
using System.Reactive.Linq;
using Serilog;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TrackerDb;
using System.Threading.Channels;
using RTSharp.Shared.Utils;
using RTSharp.Core;
using Nager.PublicSuffix;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
	{
		private Channel<(Models.Torrent, IList<Tracker>)> TrackersChanges;

		private async Task TrackersTasks(Models.Torrent Torrent, CancellationToken SelectionChange)
		{
			TrackersChanges = Channel.CreateUnbounded<(Models.Torrent, IList<Tracker>)>(new UnboundedChannelOptions() {
				SingleReader = true,
				SingleWriter = true
			});

			await Task.WhenAll(
				await Task.Factory.StartNew(TrackersModelUpdates, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()),
				await Task.Factory.StartNew(() => GetTrackersChanges(Torrent, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
			);
		}

		public async Task UpdateTrackersInTorrents()
		{
			var infos = new Dictionary<string, TrackerInfo>();
			using var scope = Core.ServiceProvider.CreateScope();
			var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
			var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

			foreach (var torrent in Torrents) {
				var domain = UriUtils.GetDomainForTracker(torrent.TrackerSingle);

				if (!infos.TryGetValue(domain, out var trackerInfo))
					trackerInfo = await trackerDb.GetTrackerInfo(domain);

				if (trackerInfo == null)
					continue;

				torrent.TrackerDisplayName = trackerInfo.Name;

				if (trackerInfo.ImageHash == null)
					continue;

				var image = await imageCache.GetCachedImage(trackerInfo.ImageHash);
				if (image != null)
					torrent.TrackerIcon = image;
			}
		}

		private async Task TrackersModelUpdates()
		{
			Models.Torrent? lastFetchedFor = null;
			try {
				TrackersViewModel.Trackers.Clear();

				await foreach (var (fetchedFor, trackers) in TrackersChanges.Reader.ReadAllAsync()) {
					if (lastFetchedFor == null || !fetchedFor.Hash.SequenceEqual(lastFetchedFor!.Hash) || fetchedFor.Owner != lastFetchedFor.Owner)
						TrackersViewModel.Trackers.Clear();

					foreach (var newTracker in trackers) {
						bool foundInOld = false;
						foreach (var oldTracker in TrackersViewModel.Trackers) {
							if (newTracker.Uri == oldTracker.Uri) {
								// Update
								oldTracker.UpdateFromPluginModel(newTracker);
								foundInOld = true;
								break;
							}
						}

						if (!foundInOld) {
							// Add
							TrackersViewModel.Trackers.Add(Models.Tracker.FromPluginModel(newTracker));
						}
					}

					foreach (var oldTracker in TrackersViewModel.Trackers) {
						bool foundInNew = false;
						foreach (var newTracker in trackers) {
							if (newTracker.Uri == oldTracker.Uri) {
								foundInNew = true;
								break;
							}
						}

						if (!foundInNew) {
							// Remove
							TrackersViewModel.Trackers.Remove(oldTracker);
						}
					}

					foreach (var tracker in TrackersViewModel.Trackers) {
						if (tracker.Icon == null) {
							using var scope = Core.ServiceProvider.CreateScope();
							var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
							var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

							var trackerInfo = await trackerDb.GetTrackerInfo(tracker.Domain);
							if (trackerInfo != null) {
								tracker.DisplayName = trackerInfo.Name;
								tracker.UpdateDisplay();

								if (trackerInfo.ImageHash != null) {
									var icon = await imageCache.GetCachedImage(trackerInfo.ImageHash);
									tracker.Icon = icon;
								}
								tracker.Icon ??= DefaultImage;
							} else {
								tracker.Icon = DefaultImage;

								_ = Task.Factory.StartNew(async () => {
									using var innerScope = Core.ServiceProvider.CreateScope();
									var favicons = innerScope.ServiceProvider.GetRequiredService<Core.Services.Favicon>();
									var innerTrackerDb = innerScope.ServiceProvider.GetRequiredService<TrackerDb>();
									var innerImageCache = innerScope.ServiceProvider.GetRequiredService<ImageCache>();

									byte[]? favicon = null;
									if (!string.IsNullOrEmpty(tracker.Uri.Host)) {
										var domainParser = new DomainParser(new WebTldRuleProvider());
										var domainInfo = domainParser.Parse(tracker.Uri.Host);
										favicon = await favicons.GetFavicon(domainInfo.Domain + "." + domainInfo.TLD);
									}

									try {
										if (favicon != null) {
											var (imageHash, icon) = await innerImageCache.AddImage(favicon);
											trackerInfo = new TrackerInfo(tracker.Domain, tracker.Domain, imageHash);
											tracker.Icon = icon;
										} else {
											trackerInfo = new TrackerInfo(tracker.Domain, tracker.Domain, null);
										}
										await innerTrackerDb.AddOrUpdateTrackerInfo(tracker.Domain, trackerInfo);

										await UpdateTrackersInTorrents();
									} catch { }
								}, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
							}
						}
					}

					lastFetchedFor = fetchedFor;
				}
			} catch (Exception ex) {
				Log.Logger.Fatal(ex, "GetTrackersChanges task has died.");
				throw;
			}
		}

		private async Task GetTrackersChanges(Models.Torrent current, CancellationToken SelectionChange)
		{
			try {
				while (!SelectionChange.IsCancellationRequested) {
					using var scope = Core.ServiceProvider.CreateScope();
					var config = scope.ServiceProvider.GetRequiredService<Config>();

					var delayTask = Task.Delay(config.Behavior.Value.TrackersPollingInterval, SelectionChange);

					IList<Tracker> trackers;
					try {
						trackers = (await current.Owner.Instance.GetTrackers(new List<Torrent> { current.ToPluginModel() }, SelectionChange)).First().Value;
					} catch {
						return;
					}

					TrackersChanges.Writer.TryWrite((current, trackers));

					try {
						await delayTask;
					} catch { }
				}
			} catch (Exception ex) {
				Log.Logger.Fatal(ex, "GetTrackersChanges task has died.");
				throw;
			} finally {
				TrackersChanges.Writer.Complete();
			}
		}
	}
}
