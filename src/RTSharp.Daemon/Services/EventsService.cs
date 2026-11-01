using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Shared.Abstractions;

using System.Reactive.Disposables;
using RTSharp.Daemon.Services.TorrentPoll;

using Torrent = RTSharp.Daemon.Protocols.DataProvider.Torrent;

namespace RTSharp.Daemon.Services;

[Singleton]
public class EventsService(ILogger<EventsService> Logger, RegisteredDataProviders DataProviders)
{
    private volatile List<Action<Torrent, TorrentState>> TorrentChangedStateEvSubscribers = new();
    private readonly Lock TorrentChangedStateEvSubscribersLock = new();
    private volatile List<Action<TorrentPollingService, Torrent, TorrentState>> ProviderStateSubscribers = new();

    /// <summary>
    /// Includes the source poller so equal hash evs on different providers are different.
    /// </summary>
    public IDisposable SubscribeToTorrentChangedStateEvent(Action<TorrentPollingService, Torrent, TorrentState> Handler, TimeSpan Interval)
    {
        lock (TorrentChangedStateEvSubscribersLock)
            ProviderStateSubscribers = [.. ProviderStateSubscribers, Handler];
        var subscription = Disposable.Create(() => {
            lock (TorrentChangedStateEvSubscribersLock)
                ProviderStateSubscribers = ProviderStateSubscribers.Where(x => x != Handler).ToList();
        });
        try {
            return new CompositeDisposable(subscription, DataProviders.SubscribeToTorrentPolling(Interval));
        } catch {
            subscription.Dispose();
            throw;
        }
    }

    public IDisposable SubscribeToTorrentChangedStateEvent(Action<Torrent, TorrentState> Handler)
    {
        lock (TorrentChangedStateEvSubscribersLock) {
            var copy = TorrentChangedStateEvSubscribers.ToList();
            copy.Add(Handler);
            TorrentChangedStateEvSubscribers = copy;
        }
        return Disposable.Create(() => {
            lock (TorrentChangedStateEvSubscribersLock) {
                var copy = TorrentChangedStateEvSubscribers.ToList();
                copy.Remove(Handler);
                TorrentChangedStateEvSubscribers = copy;
            }
        });
    }

    /// <summary>
    /// Subscribe to state changes and keep all configured providers polling at the requested interval.
    /// </summary>
    public IDisposable SubscribeToTorrentChangedStateEvent(Action<Torrent, TorrentState> Handler, TimeSpan Interval)
    {
        var handler = SubscribeToTorrentChangedStateEvent(Handler);
        try {
            return new CompositeDisposable(handler, DataProviders.SubscribeToTorrentPolling(Interval));
        } catch {
            handler.Dispose();
            throw;
        }
    }

    internal void EmitTorrentChangedStateEvent(TorrentPollingService Source, Torrent Torrent, TorrentState State)
    {
        foreach (var handler in ProviderStateSubscribers) {
            try {
                handler(Source, Torrent.Clone(), State);
            } catch (Exception ex) {
                Logger.LogError(ex, "Error in provider torrent state handler");
            }
        }
        foreach (var handler in TorrentChangedStateEvSubscribers) {
            try {
                handler(Torrent, State);
            } catch (Exception ex) {
                Logger.LogError(ex, "Error in TorrentChangedStateEvSubscribers handler");
            }
        }
    }
}
