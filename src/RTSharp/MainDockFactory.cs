using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Data;

using CommunityToolkit.Mvvm.Input;

using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Controls.ViewModels;
using RTSharp.ViewModels;
using RTSharp.ViewModels.TorrentListing;
using Serilog;

namespace RTSharp;

public class MainDockFactory : Factory
{
    private ProportionalDock TorrentListingDock;

    public MainDockFactory()
    {
    }

    private int GetDockableVisualIndex(IDockable In)
    {
        int slashes;
        if ((slashes = In.Id.LastIndexOf("//")) == -1)
            return 1;

        return Int32.Parse(In.Id[(slashes+2)..]);
    }

    private DocumentDock _mainDocuments;
    private IRootDock _rootDock;


	public override IRootDock CreateLayout()
    {
        // TODO: Save layout

		Document createTorrentListing(int visualId)
        {
			var listing = new TorrentListingViewModel() {
                Id = "TorrentListing",
                Title = $"Torrent Listing {(visualId > 1 ? visualId : "")}"
            };

			return listing;
		}

		var logEntries = new LogEntriesViewModel() {
			Id = "LogEntries",
			Title = "Log"
		};

		var actionQueue = new ActionQueueViewModel() {
			Id = "ActionQueue",
			Title = "Actions"
		};

        var dataProviders = new DataProvidersViewModel() {
            Id = "DataProviders",
            Title = "Data providers"
        };

		_mainDocuments = new DocumentDock {
			Id = "MainPane",
			Title = "MainPane",
			Proportion = 1,
			VisibleDockables = CreateList<IDockable>
			(
				createTorrentListing(1),
				actionQueue,
				logEntries
			)
		};

		//_mainDocuments.CanCreateDocument = true;
		_mainDocuments.CreateDocument = new RelayCommand(() => {
			var newestVisualIndex = 1;
			int newestDockableIndex = 0;
			for (var x = _mainDocuments.VisibleDockables.Count - 1;x >= 0;x--) {
				if (_mainDocuments.VisibleDockables[x] is DockableDocumentWrapperViewModel dockable && dockable.Id.StartsWith("TorrentListing")) {
					var curIndex = GetDockableVisualIndex(dockable);
					if (curIndex > newestVisualIndex) {
						newestDockableIndex = x;
						newestVisualIndex = curIndex;
					}
				}
			}

			var document = createTorrentListing(newestVisualIndex + 1);
			this.InsertDockable(_mainDocuments, document, newestDockableIndex + 1);
			if (document is IContextPopulatedNotifyable notifiable)
				notifiable.OnContextPopulated();

			this.SetActiveDockable(document);
			this.SetFocusedDockable(_mainDocuments, document);
		});

		var mainView = new MainViewModel
        {
            Id = "Main",
            Title = "Main",
            ActiveDockable = _mainDocuments,
            VisibleDockables = CreateList<IDockable>(_mainDocuments, dataProviders),
            RightPinnedDockables = CreateList<IDockable>(dataProviders)
        };

        var root = CreateRootDock();

        root.Id = "Root";
        root.Title = "Root";
        root.ActiveDockable = mainView;
        root.DefaultDockable = mainView;
        root.VisibleDockables = CreateList<IDockable>(mainView);
		_rootDock = root;

		return root;
    }

    class ContextLocatorEqualityComparer : IEqualityComparer<string>
    {
	    public bool Equals(string? x, string? y)
	    {
            if (x == null || y == null)
                return false;

		    var xIndex = x.IndexOf("//");
            if (xIndex != -1)
                x = x[..xIndex];

			var yIndex = y.IndexOf("//");
			if (yIndex != -1)
				y = y[..yIndex];

            return x == y;
		}

	    public int GetHashCode(string obj)
	    {
			var index = obj.IndexOf("//");
			if (index != -1)
				return obj[..index].GetHashCode();

            return obj.GetHashCode();
		}
    }

    public override void InitLayout(IDockable layout)
    {
        this.ContextLocator = new Dictionary<string, Func<object?>>(new ContextLocatorEqualityComparer())
        {
            ["TorrentListing"] = () => TorrentPolling.Torrents,
            ["LogEntries"] = () => Core.LogWindowSink.LogEntries,
            ["ActionQueue"] = () => Core.ActionQueue.ActionQueues,
            ["DataProviders"] = () => Plugin.Plugins.DataProviders,
        };

        this.HostWindowLocator = new Dictionary<string, Func<IHostWindow>>
        {
            [nameof(IDockWindow)] = () =>
            {
                var hostWindow = new HostWindow()
                {
                    [!HostWindow.TitleProperty] = new Binding("ActiveDockable.Title")
                };
                return hostWindow;
            }
        };

        this.DockableLocator = new Dictionary<string, Func<IDockable>> {
            ["Root"] = () => _rootDock,
            ["MainPane"] = () => _mainDocuments
        };

		this.ActiveDockableChanged += (sender, e) => {
            Log.Logger.Verbose("Dockable changed: " + e.Dockable?.Id);
		};

        this.HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> {
			[nameof(IDockWindow)] = () => new HostWindow()
		};

		base.DockableInit += (sender, e) => {
			if (e.Dockable is IContextPopulatedNotifyable notifyable)
				notifyable.OnContextPopulated();
		};

		base.InitLayout(layout);

        void subOnUnselected(IDockable dockable)
        {
	        if (dockable is not DockBase dock)
		        return;

	        /*foreach (var d in dock.VisibleDockables!.Where(x => x is DockBase).Cast<DockBase>()) {
		        if (d is IOnUnselectedNotifyable) {
			        d
                        .WhenAnyValue(x => x.ActiveDockable)
						.Buffer(2, 1)
						.Select(b => (Previous: b[0], Current: b[1]))
                        .Subscribe(x =>
                        {
	                        (x.Previous as IOnUnselectedNotifyable)?.OnUnselected();
                        });
		        }

		        subOnUnselected(d);
	        }*/
        }

        subOnUnselected(layout);
    }
}