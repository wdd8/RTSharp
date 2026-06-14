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
        private Channel<(Models.Torrent, IList<Tracker>)>? TrackersChanges;

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

        private async Task TrackersModelUpdates()
        {
            Models.Torrent? lastFetchedFor = null;

            if (TrackersChanges == null)
                throw new NullReferenceException(nameof(TrackersChanges));

            try {
                TrackersViewModel.Trackers.Clear();

                await foreach (var (fetchedFor, trackers) in TrackersChanges.Reader.ReadAllAsync()) {
                    bool refresh = false;

                    if (lastFetchedFor == null || !fetchedFor.Hash.SequenceEqual(lastFetchedFor!.Hash) || fetchedFor.DataOwner != lastFetchedFor.DataOwner) {
                        TrackersViewModel.Trackers.Clear();
                        refresh = true;
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
                            refresh = true;
                        }
                    }

                    foreach (var oldTracker in TrackersViewModel.Trackers.ToArray()) {
                        if (!@new.Contains(oldTracker.Uri)) {
                            // Remove
                            TrackersViewModel.Trackers.Remove(oldTracker);
                            refresh = true;
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

                                _ = Task.Run(async () => {
                                    using var innerScope = Core.ServiceProvider.CreateScope();
                                    var favicons = innerScope.ServiceProvider.GetRequiredService<Core.Services.Favicon>();
                                    var innerTrackerDb = innerScope.ServiceProvider.GetRequiredService<TrackerDb>();
                                    var innerImageCache = innerScope.ServiceProvider.GetRequiredService<ImageCache>();
                                    var domainParser = innerScope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>();

                                    System.IO.Stream? favicon = null;
                                    if (!string.IsNullOrEmpty(UriUtils.GetDomainForTracker(tracker.Uri))) {
                                        var domainInfo = domainParser.Parse(UriUtils.GetDomainForTracker(tracker.Uri));
                                        if (domainInfo?.RegistrableDomain != null) {
                                            favicon = await favicons.GetFavicon(domainInfo.RegistrableDomain);
                                        }
                                    }

                                    try {
                                        byte[]? hash = null;
                                        if (favicon != null) {
                                            var img = await innerImageCache.AddImage(favicon);
                                            if (img != null) {
                                                hash = img.Value.Hash;
                                                var icon = img.Value.Image;
                                                Dispatcher.UIThread.Post(() => tracker.Icon = icon);
                                            }
                                        }
                                        trackerInfo = new TrackerInfo(tracker.Domain, tracker.Domain, hash);
                                        await innerTrackerDb.AddOrUpdateTrackerInfo(tracker.Domain, trackerInfo);

                                        await UpdateTrackersInTorrents();
                                    } catch { }
                                });
                            }
                        }
                    }
                    if (refresh)
                        TrackersViewModel.TrackersView.Refresh();

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
                        trackers = (await current.DataOwner.Instance.GetTrackers(new List<Torrent> { current.ToPluginModel() }, SelectionChange)).First().Value;
                    } catch (Exception ex) {
                        Log.Logger.Error(ex, "Trackers pool error");
                        return;
                    }

                    TrackersChanges!.Writer.TryWrite((current, trackers));

                    try {
                        await delayTask;
                    } catch { }
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetTrackersChanges task has died.");
                throw;
            } finally {
                TrackersChanges!.Writer.Complete();
            }
        }
    }
}
