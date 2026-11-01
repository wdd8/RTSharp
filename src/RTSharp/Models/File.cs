using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Core.Services;
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
        public required string Path { get; set; }

        /// <summary>
        /// File name
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Size in bytes
        /// </summary>
        public ulong Size { get; set; }

        /// <summary>
        /// Downloaded pieces
        /// </summary>
        [ObservableProperty]
        public partial ulong DownloadedPieces { get; set; }

        /// <summary>
        /// Percent done
        /// </summary>
        [ObservableProperty]
        public partial float Done { get; set; }

        /// <summary>
        /// Downloaded size in bytes
        /// </summary>
        [ObservableProperty]
        public partial ulong Downloaded { get; set; }
        private PRIORITY PriorityInternal { get; set; }

        /// <summary>
        /// Priority
        /// </summary>
        [ObservableProperty]
        public partial string Priority { get; set; }
        private DOWNLOAD_STRATEGY DownloadStrategyInternal { get; set; }

        /// <summary>
        /// Download strategy
        /// </summary>
        [ObservableProperty]
        public partial string DownloadStrategy { get; set; }

        [ObservableProperty]
        public partial bool IsExpanded { get; set; }
        public bool IsDirectory { get; set; }

        /// <summary>
        /// Index into the data provider's file list, <c>-1</c> for directories
        /// </summary>
        public int Index { get; set; } = -1;

        public Task<Bitmap?> IconTask => FileIconService.GetIconAsync(Name, IsDirectory);

        public ObservableCollection<File> Children { get; set; } = new();

        public static string PriorityToString(PRIORITY Priority) => Priority switch {
            PRIORITY.DONT_DOWNLOAD => "Don't download",
            PRIORITY.NORMAL => "Normal",
            PRIORITY.HIGH => "High",
            PRIORITY.NA => "N/A",
            _ => throw new ArgumentOutOfRangeException()
        };

        public static string StrategyToString(DOWNLOAD_STRATEGY Strategy) => Strategy switch {
            DOWNLOAD_STRATEGY.NORMAL => "Normal",
            DOWNLOAD_STRATEGY.PRIORITIZE_FIRST => "Prioritize first",
            DOWNLOAD_STRATEGY.PRIORITIZE_LAST => "Prioritize last",
            DOWNLOAD_STRATEGY.NA => "N/A",
            _ => throw new ArgumentOutOfRangeException()
        };

        public void ApplyDelta(Shared.Abstractions.FileDelta Delta)
        {
            DownloadedPieces = Delta.DownloadedPieces;
            Downloaded = Delta.Downloaded;
            var done = Size == 0 ? 0 : (float)Delta.Downloaded / Size * 100;
            Done = Math.Clamp(done, 0, 100);
            Priority = PriorityToString(Delta.Priority);
            PriorityInternal = Delta.Priority;
            DownloadStrategy = StrategyToString(Delta.DownloadStrategy);
            DownloadStrategyInternal = Delta.DownloadStrategy;
        }

        private static (File Root, File[] Leaves) FromModel<T>(string TorrentName, bool MultiFile, IList<T> In, Func<T, File> MapFx, Func<T, string> PathSelector)
        {
            var leaves = new File[In.Count];
            var files = new Dictionary<string, (T Item, int Index)>(In.Count);
            for (var x = 0; x < In.Count; x++)
                files[PathSelector(In[x])] = (In[x], x);

            var tree = TreeBuilder.Build(In.Select(PathSelector));

            void add(ObservableCollection<File> children, Node node)
            {
                foreach (var child in node.Children) {
                    File mapped;
                    if (files.TryGetValue(child.Path, out var realFile)) {
                        mapped = MapFx(realFile.Item);
                        mapped.Name = child.Name;
                        mapped.Index = realFile.Index;
                        leaves[realFile.Index] = mapped;
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

            var ret = new File {
                Path = rootPath,
                Name = rootPath,
                IsExpanded = true,
                IsDirectory = true,
            };
            add(ret.Children, tree);

            RecomputeFolders(ret);

            return (ret, leaves);
        }

        public static void RecomputeFolders(File Root)
        {
            zero(Root);
            sumForFolder(Root);
            Root.Done = Root.Size == 0 ? 0 : (float)Root.Downloaded / Root.Size * 100;

            static void zero(File dir)
            {
                dir.Size = 0;
                dir.DownloadedPieces = 0;
                dir.Downloaded = 0;
                dir.Done = 0;
                dir.Priority = default!;
                dir.PriorityInternal = default;
                dir.DownloadStrategy = default!;
                dir.DownloadStrategyInternal = default;

                foreach (var child in dir.Children) {
                    if (child.IsDirectory)
                        zero(child);
                }
            }

            static void sum(File dst, File src)
            {
                dst.Size += src.Size;
                dst.DownloadedPieces += src.DownloadedPieces;
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

            static void sumForFolder(File file)
            {
                if (!file.IsDirectory)
                    throw new InvalidOperationException("Passed file for folder-only method");

                foreach (var child in file.Children) {
                    if (child.IsDirectory) {
                        sumForFolder(child);
                        child.Done = child.Size == 0 ? 0 : (float)child.Downloaded / child.Size * 100;
                    }
                    sum(file, child);
                }
            }
        }

        public static (File Root, File[] Leaves) FromPluginModel(string TorrentName, Shared.Abstractions.TorrentFiles TorrentFiles)
        {
            var (MultiFile, In) = TorrentFiles;

            static File map(Shared.Abstractions.File In)
            {
                var ret = new File {
                    Path = In.Path,
                    Size = In.Size,
                    DownloadedPieces = In.DownloadedPieces,
                    Done = In.Done,
                    Downloaded = In.Downloaded,

                    Priority = PriorityToString(In.Priority),
                    PriorityInternal = In.Priority,

                    DownloadStrategy = StrategyToString(In.DownloadStrategy),
                    DownloadStrategyInternal = In.DownloadStrategy
                };

                return ret;
            }

            return FromModel(TorrentName, MultiFile, In, map, x => x.Path);
        }

        public static (File Root, File[] Leaves) FromCache(Models.Torrent torrent, bool IsMultiFile, IList<CachedTorrentPath> In, bool FullyDownloaded)
        {
            File map(CachedTorrentPath In)
            {
                var ret = new File {
                    Path = In.Path,
                    Size = In.Size,
                    DownloadedPieces = FullyDownloaded && torrent.PieceSize.HasValue ? (ulong)Math.Ceiling((double)In.Size / torrent.PieceSize.Value) : 0,
                    Done = FullyDownloaded ? 100 : 0,
                    Downloaded = FullyDownloaded ? In.Size : 0,
                    Priority = "N/A",
                    PriorityInternal = PRIORITY.NA,

                    DownloadStrategy = "N/A",
                    DownloadStrategyInternal = DOWNLOAD_STRATEGY.NA
                };

                return ret;
            }

            return FromModel(torrent.Name, IsMultiFile, In, map, x => x.Path);
        }

        public static (File Root, File[] Leaves) FromBencode(string TorrentName, bool IsMultiFile, IList<BencodeUtils.TorrentFile> In)
        {
            static File map(BencodeUtils.TorrentFile In)
            {
                var ret = new File {
                    Path = String.Join('/', In.PathComponents),
                    Size = (ulong)In.FileSize,
                    Priority = "N/A",
                    PriorityInternal = PRIORITY.NA,

                    DownloadStrategy = "N/A",
                    DownloadStrategyInternal = DOWNLOAD_STRATEGY.NA
                };

                return ret;
            }

            return FromModel(TorrentName, IsMultiFile, In, map, x => String.Join('/', x.PathComponents));
        }

        public string GetRemotePath(string RemoteBasePath, string TorrentName, bool IsMultiFile)
        {
            return System.IO.Path.Combine(IsMultiFile ? System.IO.Path.Combine(RemoteBasePath, TorrentName) : RemoteBasePath, Path).Replace("\\", "/");
        }
    }
}
