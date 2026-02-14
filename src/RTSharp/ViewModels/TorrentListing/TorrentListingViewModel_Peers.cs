using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;
using Serilog;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.Images;
using System.Threading.Channels;
using RTSharp.Core;
using System.Net;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConcurrentCollections;
using Polly;
using Polly.Bulkhead;
using RTSharp.Core.Services.Cache.ASCache;
using Avalonia.Threading;
using Avalonia.Media;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
    {
        private Channel<(Models.Torrent, ListingChanges<Peer, Models.Peer, IPEndPoint>)> PeersChanges;
        private Channel<Models.Peer> PeerInfoFetches;
        static AsyncBulkheadPolicy? PeerInfoFetchQueue;

        private async Task PeersTasks(Models.Torrent Torrent, CancellationToken SelectionChange)
        {
            PeerInfoFetches = Channel.CreateUnbounded<Models.Peer>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });
            PeersChanges = Channel.CreateUnbounded<(Models.Torrent, ListingChanges<Peer, Models.Peer, IPEndPoint>)>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            await Task.WhenAll(
                await Task.Factory.StartNew(PeerInfoFetch, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default),
                await Task.Factory.StartNew(PeersModelUpdates, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()),
                await Task.Factory.StartNew(() => GetPeersChanges(Torrent, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
            );
        }

        private async Task PeerInfoFetch()
        {
            using (var scope = Core.ServiceProvider.CreateScope()) {
                var config = scope.ServiceProvider.GetRequiredService<Config>();
                PeerInfoFetchQueue ??= Policy.BulkheadAsync(config.Caching.Value.ConcurrentPeerCachingRequests);
            }

            var fetchingPeers = new ConcurrentHashSet<IPAddress>();
            var peerCheckLock = new object();

            await foreach (var peer in PeerInfoFetches.Reader.ReadAllAsync()) {
                lock (peerCheckLock) {
                    if (fetchingPeers.Contains(peer.IPPort.Address))
                        return;
                    fetchingPeers.Add(peer.IPPort.Address);
                }

                _ = PeerInfoFetchQueue.ExecuteAsync(async () => {
                    using var innerScope = Core.ServiceProvider.CreateScope();
                    var favicons = innerScope.ServiceProvider.GetRequiredService<Core.Services.Favicon>();
                    var config = innerScope.ServiceProvider.GetRequiredService<Config>();
                    var asCache = innerScope.ServiceProvider.GetRequiredService<ASCache>();
                    var imageCache = innerScope.ServiceProvider.GetRequiredService<ImageCache>();

                    try {
                        var whois = await Core.Services.Whois.GetWhoisInfo(peer.IPPort.Address, config.Behavior.Value.PeerOriginReplacements ?? new());

                        if (whois == null) {
                            Dispatcher.UIThread.Post(() => {
                                foreach (var x in PeersViewModel.Peers.Where(x => x.IPPort.Address.Equals(peer.IPPort.Address))) {
                                    x.Icon = DefaultImage;
                                    x.Origin = "Unknown";
                                }
                            }, DispatcherPriority.Default);
                            return;
                        }

                        (byte[] Hash, Bitmap Image)? realImage = null, flagImage = null;
                        IImage peerIcon;

                        if (whois.Country != null) {
                            realImage = flagImage = await imageCache.AddImage(AssetLoader.Open(new Uri($"avares://RTSharp/Assets/Icons/Flags/{whois.Country.ToLowerInvariant()}.png")));
                            peerIcon = realImage.Value.Image;
                        } else
                            peerIcon = DefaultImage;

                        Dispatcher.UIThread.Post(() => {
                            foreach (var x in PeersViewModel.Peers.Where(x => x.IPPort.Address.Equals(peer.IPPort.Address))) {
                                x.Icon = peerIcon;
                                x.Origin = whois.Organization + (whois.Domain != null ? $" [{whois.Domain}]" : "");
                            }
                        }, DispatcherPriority.Default);

                        System.IO.Stream? favicon = null;
                        if (whois.Domain != null) {
                            favicon = await favicons.GetFavicon(whois.Domain);
                        }

                        if (favicon != null) {
                            realImage = await imageCache.AddImage(favicon);
                            if (realImage != null) {
                                peerIcon = realImage.Value.Image;

                                Dispatcher.UIThread.Post(() => {
                                    foreach (var x in PeersViewModel.Peers.Where(x => x.IPPort.Address.Equals(peer.IPPort.Address))) {
                                        x.Icon = peerIcon;
                                    }
                                }, DispatcherPriority.Default);
                            } else {
                                realImage = flagImage;
                            }
                        }

                        if (realImage != null) {
                            await asCache.AddCachedAS(whois.Range, new CachedAS {
                                Domain = whois.Domain,
                                Organization = whois.Organization,
                                Country = whois.Country,
                                ImageHash = realImage.Value.Hash
                            });
                        }
                    } finally {
                        fetchingPeers.TryRemove(peer.IPPort.Address);
                    }
                });
            }
        }

        private async Task PeersModelUpdates()
        {
            Models.Torrent? lastFetchedFor = null;
            try {
                PeersViewModel.Peers.Clear();

                await foreach (var (fetchedFor, peers) in PeersChanges.Reader.ReadAllAsync()) {
                    if (lastFetchedFor == null || !fetchedFor.Hash.SequenceEqual(lastFetchedFor!.Hash) || fetchedFor.Owner != lastFetchedFor.Owner)
                        PeersViewModel.Peers.Clear();

                    var hs = PeersViewModel.Peers.ToDictionary(x => x.IPPort, x => x);

                    // Remove
                    foreach (var removed in peers.Removed) {
                        if (hs.TryGetValue(removed, out var removePeer)) {
                            PeersViewModel.Peers.Remove(removePeer);
                        }
                    }

                    foreach (var changedPeer in peers.FullUpdate) {
                        if (hs.TryGetValue(changedPeer.IPPort, out var existingPeer)) {
                            // Update
                            existingPeer.UpdateFromPluginModel(changedPeer);
                        } else {
                            // Add
                            var peer = Models.Peer.FromPluginModel(changedPeer);
                            peer.ObservedOn = DateTime.UtcNow;
                            peer.Origin = null;
                            PeersViewModel.Peers.Add(peer);
                        }
                    }

                    foreach (var peer in PeersViewModel.Peers.Where(x => x.Origin == null)) {
                        peer.Origin = "Loading...";
                        _ = Task.Run(async () => {
                            using (var scope = Core.ServiceProvider.CreateScope()) {
                                var config = scope.ServiceProvider.GetRequiredService<Config>();
                                var asCache = scope.ServiceProvider.GetRequiredService<ASCache>();
                                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                                CachedAS asInfo;
                                if ((asInfo = await asCache.GetCachedAS(peer.IPPort.Address)) != null) {
                                    var cachedImage = await imageCache.GetCachedImage(asInfo.ImageHash);
                                    Dispatcher.UIThread.Post(() => {
                                        peer.Origin = asInfo.Organization + (asInfo.Domain != null ? $" [{asInfo.Domain}]" : "");
                                        peer.Icon = cachedImage;
                                    }, DispatcherPriority.Default);
                                } else {
                                    // Just started fetching info but there is no cached info, wait 3 seconds later and start fetching
                                    Dispatcher.UIThread.Post(() => {
                                        peer.Origin = "Inactive peer";
                                    }, DispatcherPriority.MaxValue);
                                }
                            }
                        });
                    }
                    foreach (var peer in PeersViewModel.Peers.Where(x => x.Origin == "Inactive peer")) {
                        if (DateTime.UtcNow - peer.ObservedOn > TimeSpan.FromSeconds(3)) {
                            PeerInfoFetches.Writer.TryWrite(peer);
                        }
                    }

                    lastFetchedFor = fetchedFor;
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "PeersModelUpdates task has exited");
            } finally {
                PeerInfoFetches.Writer.Complete();
            }
        }

        private async Task GetPeersChanges(Models.Torrent current, CancellationToken SelectionChange)
        {
            try {
                Dictionary<IPEndPoint, Peer>? previousPeers = null;

                while (!SelectionChange.IsCancellationRequested) {
                    using var scope = Core.ServiceProvider.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<Config>();

                    var delayTask = Task.Delay(config.Behavior.Value.PeersPollingInterval, SelectionChange);
                    
                    IList<Peer> peers = null;
                    try {
                        peers = (await current!.Owner.Instance.GetPeers(new List<Torrent> { current.ToPluginModel() }, SelectionChange)).First().Value;
                    } catch (Exception ex) {
                        Log.Logger.Error(ex, "Peer poll failed");
                        return;
                    }

                    ListingChanges<Peer, Models.Peer, IPEndPoint> changes;

                    if (previousPeers == null) {
                        previousPeers = peers.ToDictionary(x => x.IPPort, x => x);
                        changes = new ListingChanges<Peer, Models.Peer, IPEndPoint>(peers, [], Array.Empty<IPEndPoint>());
                    } else {
                        var changedPeers = new List<Peer>();
                        var removedPeers = new List<IPEndPoint>();

                        foreach (var newPeer in peers) {
                            var foundInPrevious = previousPeers.TryGetValue(newPeer.IPPort, out var prevPeer);

                            if (foundInPrevious) {
                                previousPeers.Remove(newPeer.IPPort);

                                if (prevPeer.Flags != newPeer.Flags ||
                                    prevPeer.Downloaded != newPeer.Downloaded ||
                                    prevPeer.Uploaded != newPeer.Uploaded ||
                                    prevPeer.DLSpeed != newPeer.DLSpeed ||
                                    prevPeer.UPSpeed != newPeer.UPSpeed ||
                                    prevPeer.PeerDLSpeed != newPeer.PeerDLSpeed) {
                                    // Changed
                                    changedPeers.Add(newPeer);
                                }
                            } else {
                                // New
                                changedPeers.Add(newPeer);
                            }
                        }

                        foreach (var (_, prevPeer) in previousPeers) {
                            // Removed
                            removedPeers.Add(prevPeer.IPPort);
                        }

                        changes = new ListingChanges<Peer, Models.Peer, IPEndPoint>(changedPeers, [], removedPeers);
                        previousPeers = peers.ToDictionary(x => x.IPPort, x => x);
                    }

                    PeersChanges.Writer.TryWrite((current, changes));

                    try {
                        await delayTask;
                    } catch { }
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetPeersChanges task has died.");
            } finally {
                PeersChanges.Writer.Complete();
            }
        }
    }
}
