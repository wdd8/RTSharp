using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Shared.Utils;
using static RTSharp.Shared.Abstractions.File;

namespace RTSharp.Models
{
    public partial class File : ObservableObject
	{
		/// <summary>
		/// File path
		/// </summary>
		public string Path { get; set; }

		/// <summary>
		/// File name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Size in bytes
		/// </summary>
		public ulong Size { get; set; }

		/// <summary>
		/// Downloaded chunks
		/// </summary>
		[ObservableProperty]
		public ulong downloadedChunks;

		/// <summary>
		/// Percent done
		/// </summary>
		[ObservableProperty]
		public float done;

		/// <summary>
		/// Downloaded size in bytes
		/// </summary>
		[ObservableProperty]
		public ulong downloaded;

		private PRIORITY PriorityInternal { get; set; }

		/// <summary>
		/// Priority
		/// </summary>
		[ObservableProperty]
		public string priority;

		private DOWNLOAD_STRATEGY DownloadStrategyInternal { get; set; }

		/// <summary>
		/// Download strategy
		/// </summary>
		[ObservableProperty]
		public string downloadStrategy;

		[ObservableProperty]
		public bool isExpanded;

		public bool IsDirectory { get; set; }

		public ObservableCollection<File> Children { get; set; } = new();

		public void Update(Models.File file)
		{
			this.DownloadedChunks = file.DownloadedChunks;
			this.Done = file.Done;
			this.Downloaded = file.Downloaded;
			this.Priority = file.Priority;
			this.PriorityInternal = file.PriorityInternal;
			this.DownloadStrategy = file.DownloadStrategy;
			this.DownloadStrategyInternal = file.DownloadStrategyInternal;
		}

		private static ObservableCollection<File> FromModel<T>(string TorrentName, bool MultiFile, IList<T> In, Func<T, File> MapFx, Func<T, string> PathSelector)
		{
			var files = In.ToDictionary(x => PathSelector(x), x => x);
			var tree = TreeBuilder.Build(In.Select(x => PathSelector(x)));

			void add(ObservableCollection<File> children, Node node)
			{
				foreach (var child in node.Children) {
					File mapped;
					if (files.TryGetValue(child.Path, out var realFile)) {
						mapped = MapFx(realFile);
						mapped.Name = child.Name;
					} else {
						mapped = new File {
							Name = child.Name,
							Path = child.Path,
							IsDirectory = true
						};
					}

					children.Add(mapped);
					add(mapped.Children, child);
				}
			}

			var rootPath = MultiFile ? TorrentName : "./";

			var ret = new ObservableCollection<File> {
				new File {
					Path = rootPath,
					Name = rootPath,
					IsExpanded = true,
					IsDirectory = true,
				}
			};
			add(ret[0].Children, tree);

			void sumForFolder(File file)
			{
				if (!file.IsDirectory)
					throw new InvalidOperationException("Passed file for folder-only method");

				void sum(File dst, File src)
				{
					dst.Size += src.Size;
					dst.DownloadedChunks += src.DownloadedChunks;
					dst.Done += src.Done;
					dst.Downloaded += src.Downloaded;
					if (dst.Priority == default) {
						dst.Priority = src.Priority;
						dst.PriorityInternal = src.PriorityInternal;
					} else if (dst.Priority != src.Priority) {
						dst.Priority = "N/A";
						dst.PriorityInternal = PRIORITY.NA;
					}
					if (dst.DownloadStrategy == default) {
						dst.DownloadStrategy = src.DownloadStrategy;
						dst.DownloadStrategyInternal = src.DownloadStrategyInternal;
					} else if (dst.DownloadStrategy != src.DownloadStrategy) {
						dst.DownloadStrategy = "N/A";
						dst.DownloadStrategyInternal = DOWNLOAD_STRATEGY.NA;
					}
				}

				foreach (var child in file.Children) {
					if (child.IsDirectory) {
						sumForFolder(child);
						child.Done = (float)child.Downloaded / child.Size * 100;
					}
					sum(file, child);
				}
			}

			sumForFolder(ret[0]);
			ret[0].Done = (float)ret[0].Downloaded / ret[0].Size * 100;

			return ret;
		}

		public static ObservableCollection<File> FromPluginModel(string TorrentName, bool MultiFile, IList<Shared.Abstractions.File> In)
		{
			File map(Shared.Abstractions.File In)
			{
				var ret = new File();
				ret.Path = In.Path;
				ret.Size = In.Size;
				ret.DownloadedChunks = In.DownloadedChunks;
				ret.Done = In.Done;
				ret.Downloaded = In.Downloaded;

				ret.Priority = In.Priority switch {
					PRIORITY.DONT_DOWNLOAD => "Don't download",
					PRIORITY.NORMAL => "Normal",
					PRIORITY.HIGH => "High",
					PRIORITY.NA => "N/A",
					_ => throw new ArgumentOutOfRangeException()
				};

				ret.PriorityInternal = In.Priority;

				ret.DownloadStrategy = In.DownloadStrategy switch {
					DOWNLOAD_STRATEGY.NORMAL => "Normal",
					DOWNLOAD_STRATEGY.PRIORITIZE_FIRST => "Prioritize first",
					DOWNLOAD_STRATEGY.PRIORITIZE_LAST => "Prioritize last",
					DOWNLOAD_STRATEGY.NA => "N/A",
					_ => throw new ArgumentOutOfRangeException()
				};

				ret.DownloadStrategyInternal = In.DownloadStrategy;

				return ret;
			}

			return FromModel(TorrentName, MultiFile, In, map, x => x.Path);
		}

		public static ObservableCollection<File> FromCache(Models.Torrent torrent, bool IsMultiFile, IList<CachedTorrentPath> In, bool FullyDownloaded)
		{
			File map(CachedTorrentPath In)
			{
				var ret = new File();
				ret.Path = In.Path;
				ret.Size = In.Size;
				ret.DownloadedChunks = FullyDownloaded && torrent.ChunkSize.HasValue ? (ulong)Math.Ceiling((double)In.Size / torrent.ChunkSize.Value) : 0;
				ret.Done = FullyDownloaded ? 100 : 0;
				ret.Downloaded = FullyDownloaded ? In.Size : 0;
				ret.Priority = "N/A";
				ret.PriorityInternal = PRIORITY.NA;

				ret.DownloadStrategy = "N/A";
				ret.DownloadStrategyInternal = DOWNLOAD_STRATEGY.NA;

				return ret;
			}

			return FromModel(torrent.Name, IsMultiFile, In, map, x => x.Path);
		}
	}
}
