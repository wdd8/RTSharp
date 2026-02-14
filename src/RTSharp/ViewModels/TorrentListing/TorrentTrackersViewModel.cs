using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using RTSharp.Core;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TrackerDb;
using Serilog;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentTrackersViewModel : ObservableObject
    {
        public ObservableCollection<Models.Tracker> Trackers { get; } = new();

        public Func<Window, Task<string?>> BrowseForIconDialog { get; set; }

        public TorrentListingViewModel Parent { get; init; }

        public TorrentTrackersViewModel()
        {
        }

        public TorrentTrackersViewModel(TorrentListingViewModel Parent)
        {
            this.Parent = Parent;
        }

        [RelayCommand]
        public async Task AddNewTracker(IList In)
        {

        }

        [RelayCommand]
        public async Task Enable(IList In)
        {

        }

        [RelayCommand]
        public async Task Disable(IList In)
        {

        }

        [RelayCommand]
        public async Task Remove(IList In)
        {

        }

        [RelayCommand]
        public async Task Reannounce(IList In)
        {
            var trackers = In.Cast<Models.Tracker>().ToArray();
        }

        [RelayCommand]
        public async Task SetName((object SelectedItems, string Text) In)
        {
            var trackers = ((IList)In.SelectedItems).Cast<Models.Tracker>().ToArray();

            if (trackers.Length != 1)
                return;

            using var scope = Core.ServiceProvider.CreateScope();
            var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();

            trackers[0].DisplayName = In.Text;
            trackers[0].UpdateDisplay();

            try {
                var trackerInfo = await trackerDb.GetTrackerInfo(trackers[0].Domain);
                trackerInfo ??= new TrackerInfo();
                trackerInfo.Name = In.Text;
                await trackerDb.AddOrUpdateTrackerInfo(trackers[0].Domain, trackerInfo);
                await Parent.UpdateTrackersInTorrents();
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Setting tracker name failed");
            } finally {
                DialogHost.GetDialogSession("SetNameHost")!.Close(false);
            }
        }

        [RelayCommand]
        public async Task SetIcon(IList In)
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
            var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

            var trackers = In.Cast<Models.Tracker>().ToArray();

            if (trackers.Length != 1)
                return;

            var iconPath = await BrowseForIconDialog(App.MainWindow);
            if (iconPath == null)
                return;

            System.IO.Stream icon;
            try {
                icon = System.IO.File.OpenRead(iconPath);
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Failed to open file \"{iconPath}\"");
                return;
            }

            var trackerInfo = await trackerDb.GetTrackerInfo(trackers[0].Domain);
            trackerInfo ??= new TrackerInfo() {
                Name = trackers[0].Domain
            };

            var img = await imageCache.AddImage(icon);
            if (img == null) {
                Log.Logger.Error("Invalid image");
                return;
            }

            trackerInfo.ImageHash = img.Value.Hash;
            foreach (var tracker in Trackers) {
                if (tracker.Domain != trackers[0].Domain)
                    continue;

                tracker.Icon = img.Value.Image;
            }

            await trackerDb.AddOrUpdateTrackerInfo(trackers[0].Domain, trackerInfo);
            await Parent.UpdateTrackersInTorrents();
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-address-book");

        [RelayCommand]
        public async Task ReplaceTracker((object SelectedItems, string Text) In)
        {
            var trackers = ((IList)In.SelectedItems).Cast<Models.Tracker>().ToArray();

            if (trackers.Length != 1)
                return;

            var oldTracker = trackers[0].Uri;

            var selectedTorrents = Parent.CurrentlySelectedItems;
            if (selectedTorrents.Count != 1)
                return;

            var selectedTorrent = selectedTorrents.Items[0];

            try {
                await selectedTorrent.Owner.Instance.Tracker.ReplaceTracker(selectedTorrent.ToPluginModel(), oldTracker, In.Text);
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Replacing tracker failed");
            } finally {
                DialogHost.GetDialogSession("ReplaceTrackerHost")!.Close(false);
            }
        }
    }
}
