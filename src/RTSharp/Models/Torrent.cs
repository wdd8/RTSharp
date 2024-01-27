using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TrackerDb;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Models
{
    public partial class Torrent : ObservableObject
    {
		/// <summary>
		/// Torrent info hash, 20 bytes
		/// </summary>
		public byte[] Hash { get; set; }

		public DataProvider Owner { get; set; }

		/// <summary>
		/// Torrent name
		/// </summary>
		[ObservableProperty]
		public string name;

		[ObservableProperty]
		public TORRENT_STATE internalState;

		/// <summary>
		/// State
		/// </summary>
		[ObservableProperty]
		public string state;

		/// <summary>
		/// Size in bytes
		/// </summary>
		[ObservableProperty]
		public ulong size;

		/// <summary>
		/// Size we want to download (in case of 0 priority files)
		/// </summary>
		[ObservableProperty]
		public ulong wantedSize;

		/// <summary>
		/// Chunk size
		/// </summary>
		public ulong? ChunkSize { get; set; }

		/// <summary>
		/// Wasted bytes
		/// </summary>
		[ObservableProperty]
		public ulong? wasted;

		/// <summary>
		/// Done percentage
		/// </summary>
		[ObservableProperty]
		public float done;

		/// <summary>
		/// Downloaded bytes
		/// </summary>
		[ObservableProperty]
		public ulong downloaded;

		/// <summary>
		/// Uploaded bytes
		/// </summary>
		[ObservableProperty]
		public ulong uploaded;

		/// <summary>
		/// Share ratio
		/// </summary>
		[ObservableProperty]
		public float ratio;

		/// <summary>
		/// Download speed, B/s
		/// </summary>
		[ObservableProperty]
		public ulong dLSpeed;

		/// <summary>
		/// Upload speed, B/S
		/// </summary>
		[ObservableProperty]
		public ulong uPSpeed;

		/// <summary>
		/// ETA, <c>0</c> if already at 100%
		/// </summary>
		/// <seealso cref="Done"/>
		[ObservableProperty]
		public TimeSpan eTA;

		/// <summary>
		/// Torrent label
		/// </summary>
		[ObservableProperty]
		public HashSet<string> labels;

		/// <summary>
		/// Torrent peers. Connected, Total
		/// </summary>
		[ObservableProperty]
		public ConnectedTotalPair peers;
		/// <summary>
		/// Torrent seeders. Connected, Total
		/// </summary>
		[ObservableProperty]
		public ConnectedTotalPair seeders;

		private TORRENT_PRIORITY InternalPriority;

		/// <summary>
		/// Torrent priority
		/// </summary>
		[ObservableProperty]
		public string priority;

		/// <summary>
		/// Unix timestamp of when .torrent file was created
		/// </summary>
		public DateTime? CreatedOnDate { get; set; }

		/// <summary>
		/// Remaining size to download
		/// </summary>
		/// <seealso cref="Size"/>
		/// <seealso cref="Downloaded"/>
		[ObservableProperty]
		public ulong remainingSize;

		/// <summary>
		/// Date when torrent finished downloading
		/// </summary>
		[ObservableProperty]
		public DateTime? finishedOnDate;

		/// <summary>
		/// Time elapsed when downloading torrent.
		/// This can be provider specific, for example elapsed time can be counted from the moment torrent connects to a seeder, or from a moment it was added.
		/// </summary>
		[ObservableProperty]
		public TimeSpan timeElapsed;

		/// <summary>
		/// Unix timestamp of when torrent was added
		/// </summary>
		public DateTime AddedOnDate { get; set; }

		/// <summary>
		/// Primary tracker URI
		/// </summary>
		[ObservableProperty]
		public Uri? trackerSingle;

		[ObservableProperty]
		public IImage? trackerIcon;

		[ObservableProperty]
		public string? trackerDisplayName;

		/// <summary>
		/// Status message
		/// </summary>
		[ObservableProperty]
		public string statusMsg;

		/// <summary>
		/// Torrent comment
		/// </summary>
		public string Comment { get; set; }

		/// <summary>
		/// Remote path of torrent data
		/// </summary>
		[ObservableProperty]
		public string remotePath;

		/// <summary>
		/// Is torrent a magnet link dummy waiting to be resolved?
		/// </summary>
		[ObservableProperty]
		public bool magnetDummy;

		public Torrent(byte[] Hash, DataProvider Owner)
		{
			this.Hash = Hash;
			this.Owner = Owner;
		}

		public async ValueTask UpdateFromPluginModel(Shared.Abstractions.Torrent In)
		{
			this.Name = In.Name;

			string stateStr = "N/A";
			if (In.State.HasFlag(TORRENT_STATE.SEEDING))
				stateStr = "Seeding";
			if (In.State.HasFlag(TORRENT_STATE.STOPPED))
				stateStr = "Stopped";
			if (In.State.HasFlag(TORRENT_STATE.COMPLETE))
				stateStr = "Complete";
			if (In.State.HasFlag(TORRENT_STATE.DOWNLOADING))
				stateStr = "Downloading";
			if (In.State.HasFlag(TORRENT_STATE.PAUSED))
				stateStr = "Paused";
			if (In.State.HasFlag(TORRENT_STATE.HASHING))
				stateStr = "Hashing";
			if (In.State.HasFlag(TORRENT_STATE.ERRORED))
				stateStr = "☠ " + stateStr;
			if (In.State.HasFlag(TORRENT_STATE.ACTIVE))
				stateStr = "⚡ " + stateStr;

			this.State = stateStr;
			this.InternalState = In.State;

			this.Size = In.Size;
			this.WantedSize = In.WantedSize;
			this.ChunkSize = In.ChunkSize;
			this.Wasted = In.Wasted;
			this.Done = In.Done;
			this.Downloaded = In.Downloaded;
			this.Uploaded = In.Uploaded;
			this.Ratio = (float)In.Uploaded / In.Downloaded;
			if (In.Uploaded == 0)
				this.Ratio = 0;
			this.DLSpeed = In.DLSpeed;
			this.UPSpeed = In.UPSpeed;
			this.ETA = In.ETA;
			this.Labels = In.Labels;
			this.Peers = new ConnectedTotalPair(In.Peers.Connected, In.Peers.Total);
			this.Seeders = new ConnectedTotalPair(In.Seeders.Connected, In.Seeders.Total);

			if (In.Priority == TORRENT_PRIORITY.HIGH)
				this.Priority = "High";
			else if (In.Priority == TORRENT_PRIORITY.LOW)
				this.Priority = "Low";
			else if (In.Priority == TORRENT_PRIORITY.NORMAL)
				this.Priority = "Normal";
			else
				this.Priority = In.Priority == TORRENT_PRIORITY.OFF ? "Off" : "N/A";

			this.InternalPriority = In.Priority;

			if (this.TrackerSingle != In.TrackerSingle) {
				using var scope = Core.ServiceProvider.CreateScope();
				var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
				var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

				var trackerInfo = await trackerDb.GetTrackerInfo(UriUtils.GetDomainForTracker(In.TrackerSingle));
				if (trackerInfo != null) {
					if (trackerInfo.ImageHash != null) {
						var image = await imageCache.GetCachedImage(trackerInfo.ImageHash);
						if (image != null)
							this.TrackerIcon = image;
					}
					this.TrackerDisplayName = trackerInfo.Name ?? UriUtils.GetDomainForTracker(In.TrackerSingle);
				} else
					this.TrackerDisplayName = UriUtils.GetDomainForTracker(In.TrackerSingle);
			}

			this.CreatedOnDate = In.CreatedOnDate;
			this.RemainingSize = In.RemainingSize;
			this.FinishedOnDate = In.FinishedOnDate;
			this.TimeElapsed = In.TimeElapsed;
			this.AddedOnDate = In.AddedOnDate;
			this.TrackerSingle = In.TrackerSingle;
			this.StatusMsg = In.StatusMessage;
			this.Comment = In.Comment;
			this.RemotePath = In.RemotePath;
			this.MagnetDummy = In.MagnetDummy;
		}

		public Shared.Abstractions.Torrent ToPluginModel()
		{
			return new Shared.Abstractions.Torrent(Hash) {
				Name = Name,
				Owner = Owner.Instance,
				State = InternalState,
				Size = Size,
				WantedSize = Size, // ??????????
				ChunkSize = ChunkSize,
				Wasted = Wasted,
				Done = (float)Downloaded / Size * 100, // Maybe get from server?
				Downloaded = Downloaded,
				Uploaded = Uploaded,
				DLSpeed = DLSpeed,
				UPSpeed = UPSpeed,
				ETA = ETA,
				Labels = Labels,
				Peers = (Peers.Connected, Peers.Total),
				Seeders = (Seeders.Connected, Seeders.Total),
				Priority = InternalPriority,
				CreatedOnDate = CreatedOnDate,
				RemainingSize = Size - Downloaded,
				FinishedOnDate = FinishedOnDate,
				TimeElapsed = TimeElapsed,
				AddedOnDate = AddedOnDate,
				TrackerSingle = TrackerSingle,
				StatusMessage = StatusMsg,
				Comment = Comment,
				RemotePath = RemotePath,
				MagnetDummy = MagnetDummy
			};
		}
	}
}
