using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;
using System.Reactive.Linq;
using Serilog;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.Images;
using System.Threading.Channels;
using RTSharp.Shared.Utils;
using RTSharp.Core;
using RTSharp.Core.Services.Database.TrackerDb;
using Avalonia.Threading;
using Avalonia.Media;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
    {
        private async Task TrackersTasks(Models.Torrent Torrent, CancellationToken SelectionChange)
        {
            var trackersChanges = Channel.CreateUnbounded<(Models.Torrent, IList<Tracker>)>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });
            var trackerInfoFetches = Channel.CreateUnbounded<Models.Tracker>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            await Task.WhenAll(
                await Task.Factory.StartNew(() => TrackerInfoFetch(trackerInfoFetches), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default),
                await Task.Factory.StartNew(() => TrackersModelUpdates(trackersChanges, trackerInfoFetches, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()),
                await Task.Factory.StartNew(() => GetTrackersChanges(Torrent, trackersChanges, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
            );
        }

        public static async Task UpdateTrackersInTorrents()
        {
            var resolved = new Dictionary<string, (bool Found, string? Name, IImage? Icon)>();
            var updates = new List<(Models.Torrent Torrent, string? Name, IImage? Icon)>();

            await Task.Run(async () => {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                foreach (var torrent in Core.TorrentPolling.TorrentPolling.Torrents.GetSnapshot()) {
                    var domain = UriUtils.GetDomainForTracker(torrent.TrackerSingle);

                    if (!resolved.TryGetValue(domain, out var info)) {
                        var trackerInfo = await trackerDb.GetTrackerInfo(domain);
                        IImage? icon = null;
                        if (trackerInfo?.ImageHash != null)
                            icon = await imageCache.GetCachedImage(trackerInfo.ImageHash);
                        info = (trackerInfo != null, trackerInfo?.Name, icon);
                        resolved[domain] = info;
                    }

                    if (!info.Found)
                        continue;

                    updates.Add((torrent, info.Name, info.Icon));
                }
            });

            if (updates.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() => {
                foreach (var (torrent, name, icon) in updates) {
                    torrent.TrackerDisplayName = name;
                    if (icon != null)
                        torrent.TrackerIcon = icon;
                }
            });
        }

        private async Task TrackersModelUpdates(Channel<(Models.Torrent, IList<Tracker>)> TrackersChanges, Channel<Models.Tracker> TrackerInfoFetches, CancellationToken SelectionChange)
        {
            Models.Torrent? lastFetchedFor = null;

            try {
                TrackersViewModel.Trackers.Clear();

                await foreach (var (fetchedFor, trackers) in TrackersChanges.Reader.ReadAllAsync()) {
                    if (SelectionChange.IsCancellationRequested)
                        break;

                    if (lastFetchedFor == null || !fetchedFor.Hash.SequenceEqual(lastFetchedFor!.Hash) || fetchedFor.DataOwner != lastFetchedFor.DataOwner) {
                        TrackersViewModel.Trackers.Clear();
                    }

                    var existing = TrackersViewModel.Trackers.ToDictionary(x => x.Uri);
                    var @new = new HashSet<string>(trackers.Count);

                    foreach (var newTracker in trackers) {
                        @new.Add(newTracker.Uri);

                        if (existing.TryGetValue(newTracker.Uri, out var oldTracker)) {
                            // Update
                            oldTracker.UpdateFromPluginModel(newTracker);
                        } else {
                            // Add
                            TrackersViewModel.Trackers.Add(Models.Tracker.FromPluginModel(newTracker));
                        }
                    }

                    foreach (var oldTracker in TrackersViewModel.Trackers.ToArray()) {
                        if (!@new.Contains(oldTracker.Uri)) {
                            // Remove
                            TrackersViewModel.Trackers.Remove(oldTracker);
                        }
                    }

                    foreach (var tracker in TrackersViewModel.Trackers) {
                        if (tracker.Icon == null) {
                            tracker.Icon = DefaultImage;
                            TrackerInfoFetches.Writer.TryWrite(tracker);
                        }
                    }

                    TrackersViewModel.TrackersView.Refresh();

                    lastFetchedFor = fetchedFor;
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "TrackersModelUpdates task has exited");
            } finally {
                TrackerInfoFetches.Writer.Complete();
            }
        }

        private async Task TrackerInfoFetch(Channel<Models.Tracker> TrackerInfoFetches)
        {
            await foreach (var tracker in TrackerInfoFetches.Reader.ReadAllAsync()) {
                try {
                    using var scope = Core.ServiceProvider.CreateScope();
                    var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                    var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                    var trackerInfo = await trackerDb.GetTrackerInfo(tracker.Domain);

                    if (trackerInfo != null) {
                        var icon = trackerInfo.ImageHash == null ? null : await imageCache.GetCachedImage(trackerInfo.ImageHash);

                        Dispatcher.UIThread.Post(() => {
                            tracker.DisplayName = trackerInfo.Name;
                            tracker.UpdateDisplay();
                            tracker.Icon = (IImage?)icon ?? DefaultImage;
                        });
                        continue;
                    }

                    var favicons = scope.ServiceProvider.GetRequiredService<Core.Services.Favicon>();
                    var domainParser = scope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>();

                    System.IO.Stream? favicon = null;
                    var domain = UriUtils.GetDomainForTracker(tracker.Uri);
                    if (!string.IsNullOrEmpty(domain)) {
                        var domainInfo = domainParser.Parse(domain);
                        if (domainInfo?.RegistrableDomain != null)
                            favicon = await favicons.GetFavicon(domainInfo.RegistrableDomain);
                    }

                    byte[]? hash = null;
                    if (favicon != null) {
                        var img = await imageCache.AddImage(favicon);
                        if (img != null) {
                            hash = img.Value.Hash;
                            var icon = img.Value.Image;
                            Dispatcher.UIThread.Post(() => tracker.Icon = icon);
                        }
                    }

                    await trackerDb.AddOrUpdateTrackerInfo(tracker.Domain, new TrackerInfo(tracker.Domain, tracker.Domain, hash));
                    await UpdateTrackersInTorrents();
                } catch (Exception ex) {
                    Log.Logger.Warning(ex, "Failed to resolve tracker info for {Uri}", tracker.Uri);
                }
            }
        }

        private async Task GetTrackersChanges(Models.Torrent current, Channel<(Models.Torrent, IList<Tracker>)> TrackersChanges, CancellationToken SelectionChange)
        {
            try {
                while (!SelectionChange.IsCancellationRequested) {
                    using var scope = Core.ServiceProvider.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<Config>();

                    var delayTask = Task.Delay(config.Behavior.Value.TrackersPollingInterval, SelectionChange);

                    IList<Tracker>? trackers = null;
                    try {
                        trackers = (await current.DataOwner.Instance.GetTrackers(new List<Torrent> { current.ToPluginModel() }, SelectionChange)).FirstOrDefault().Value;
                    } catch (Exception ex) {
                        Log.Logger.Error(ex, "Trackers pool error");
                        return;
                    }

                    if (trackers == null)
                        goto end;

                    TrackersChanges.Writer.TryWrite((current, trackers));

end:
                    await Task.WhenAny(delayTask);
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
