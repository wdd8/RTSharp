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
using NP.Ava.UniDockService;

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
        public async Task SetName((IList SelectedItems, string Text) In)
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();

            var trackers = In.SelectedItems.Cast<Models.Tracker>().ToArray();

            if (trackers.Length != 1)
                return;

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

            byte[] icon;
            try {
                icon = await System.IO.File.ReadAllBytesAsync(iconPath);
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Failed to open file \"{iconPath}\"");
                return;
            }

            var trackerInfo = await trackerDb.GetTrackerInfo(trackers[0].Domain);
            trackerInfo ??= new TrackerInfo() {
                Name = trackers[0].Domain
            };

            var (imageHash, image) = await imageCache.AddImage(icon);

            trackerInfo.ImageHash = imageHash;
            foreach (var tracker in Trackers) {
                if (tracker.Domain != trackers[0].Domain)
                    continue;

                tracker.Icon = image;
            }

            await trackerDb.AddOrUpdateTrackerInfo(trackers[0].Domain, trackerInfo);
            await Parent.UpdateTrackersInTorrents();
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-address-book");
    }
}
