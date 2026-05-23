using BencodeNET.Objects;

using Google.Protobuf.WellKnownTypes;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace RTSharp.Shared.Utils
{
    public partial class TorrentCreator
    {
        private const int MerkleBlockSize = 16 * 1024;
        private const int FileReadBufferSize = 128 * 1024;

        public record TorrentFileEntry(FileInfo Info, string[] RelativePath, string DisplayPath)
        {
            internal string SortPath { get; } = String.Join('\0', RelativePath);
        }

        public record DataInfo(long TotalSize, bool SingleFile, List<TorrentFileEntry> Files);

        private record V2FileHash(BString Root, BString? PieceLayer);

        [Flags]
        public enum VERSION
        {
            V1 = 1,
            V2 = 2
        }

        private static long GetDirectorySize(ConcurrentBag<FileInfo> Files, DirectoryInfo DirectoryInfo, bool Recursive = true)
        {
            var startDirectorySize = default(long);
            if (DirectoryInfo == null || !DirectoryInfo.Exists)
                return startDirectorySize; //Return 0 while Directory does not exist.

            //Add size of files in the Current Directory to main size.
            foreach (var fileInfo in DirectoryInfo.GetFiles()) {
                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
                    continue;

                Interlocked.Add(ref startDirectorySize, fileInfo.Length);
                Files.Add(fileInfo);
            }

            if (Recursive) //Loop on Sub Directories in the Current Directory and Calculate it's files size.
                Parallel.ForEach(DirectoryInfo.GetDirectories(), (subDirectory) =>
                    Interlocked.Add(ref startDirectorySize, GetDirectorySize(Files, subDirectory, Recursive))
                );

            return startDirectorySize;  //Return full Size of this Directory.
        }

        public static async ValueTask<DataInfo> GetDataInfo(string Path)
        {
            var allFiles = new ConcurrentBag<FileInfo>();

            bool singleFile = File.Exists(Path);
            if (singleFile) {
                var info = new FileInfo(Path);
                var fileName = System.IO.Path.GetFileName(info.FullName);
                return new DataInfo(info.Length, true, [new TorrentFileEntry(info, [fileName], fileName)]);
            } else {
                var task = Task.Run(() => GetDirectorySize(allFiles, new DirectoryInfo(Path)));
                var totalSize = await task;
                var files = allFiles
                    .Select(file =>
                    {
                        var relativePath = System.IO.Path.GetRelativePath(Path, file.FullName);
                        var segments = relativePath.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                        return new TorrentFileEntry(file, segments, relativePath);
                    })
                    .ToList();

                // Hybrid torrents need V1 file order to match V2 file tree traversal
                files.Sort((left, right) => String.CompareOrdinal(left.SortPath, right.SortPath));

                return new DataInfo(totalSize, false, files);
            }
        }

        public static int CalculatePieceLength(long TotalSize)
        {
            int ret = 0;
            for (var x = 0;x < 10;x++) {
                ret = 16 * 1024 * (int)Math.Pow(2, x);

                if (Math.Pow(2 * ret / 20, 2) >= TotalSize) {
                    break;
                }
            }

            return ret;
        }

        public static uint CalculateNumberOfPieces(long TotalSize, int PieceLength)
        {
            return (uint)Math.Ceiling((decimal)TotalSize / PieceLength);
        }

        public static async Task<byte[]> Create(
            string Path,
            IList<string>? Trackers,
            IList<string>? WebSeeds,
            string? Comments,
            string? Source,
            bool Private,
            bool Entropy,
            bool EmitCreationDate,
            VERSION Version = VERSION.V1 | VERSION.V2,
            int? PieceLength = null,
            bool Parallel = true,
            Action<(float HashProgress, string CurrentFile, float FileProgress, float FileBuffer, string HashExcerpt)>? Progress = null,
            CancellationToken cancellationToken = default)
        {
            bool createV1 = Version.HasFlag(VERSION.V1);
            bool createV2 = Version.HasFlag(VERSION.V2);
            if (!createV1 && !createV2)
                throw new ArgumentException("At least one torrent version must be selected.", nameof(Version));

            Progress?.Invoke((0, "Calculating total size...", 0, 0, ""));
            var dataInfo = await GetDataInfo(Path);
            int pieceLength = PieceLength ?? CalculatePieceLength(dataInfo.TotalSize);
            ValidatePieceLength(pieceLength, createV2);

            bool singleFile = dataInfo.SingleFile;
            var allFiles = dataInfo.Files;
            if (allFiles.Count == 0)
                throw new InvalidOperationException("Cannot create a torrent with no files.");

            var progressState = new TorrentCreationProgress(dataInfo.TotalSize, Progress);

            int lastLegitFileIdx = allFiles.FindLastIndex(file => file.Info.Length > 0);
            int v1HashCapacity = createV1 ? GetV1PieceHashCapacity(GetV1PayloadLength(dataInfo.TotalSize, allFiles, pieceLength, lastLegitFileIdx, createV2), pieceLength) : 0;
            var hashResult = Parallel
                ? await HashFilesInParallel(allFiles, createV1, createV2, progressState, pieceLength, lastLegitFileIdx, v1HashCapacity, cancellationToken)
                : await HashFilesSequentially(allFiles, createV1, createV2, progressState, pieceLength, lastLegitFileIdx, v1HashCapacity, cancellationToken);

            progressState.Complete();

            cancellationToken.ThrowIfCancellationRequested();

            var dict = BuildTorrentDictionary(
                Path,
                singleFile,
                allFiles,
                hashResult.V1Hashes,
                hashResult.V2Hashes,
                pieceLength,
                Trackers,
                WebSeeds,
                Comments,
                Source,
                Private,
                Entropy,
                EmitCreationDate);

            return dict.EncodeAsBytes();
        }

        private static async Task<HashResult> HashFilesSequentially(
            List<TorrentFileEntry> AllFiles,
            bool CreateV1,
            bool CreateV2,
            TorrentCreationProgress ProgressState,
            int PieceLength,
            int LastLegitFileIdx,
            int V1HashCapacity,
            CancellationToken CancellationToken)
        {
            var v2Hashes = CreateV2 ? new Dictionary<TorrentFileEntry, V2FileHash>() : null;
            var v1Hasher = CreateV1 ? new V1PieceHasher(ProgressState, PieceLength, V1HashCapacity) : null;
            var v2Hasher = CreateV2 ? new V2FileHasher(ProgressState, PieceLength) : null;

            var reader = new FileDistributor(ProgressState);
            await reader.ReadSynchronously(AllFiles, v1Hasher, v2Hasher, v2Hashes, CreateV1 && CreateV2, PieceLength, LastLegitFileIdx, CancellationToken);

            v1Hasher?.Flush();
            return new HashResult(v1Hasher?.Hashes, v2Hashes);
        }

        private static async Task<HashResult> HashFilesInParallel(
            List<TorrentFileEntry> AllFiles,
            bool CreateV1,
            bool CreateV2,
            TorrentCreationProgress ProgressState,
            int PieceLength,
            int LastLegitFileIdx,
            int V1HashCapacity,
            CancellationToken CancellationToken)
        {
            using var pipelineCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            var pipelineToken = pipelineCancellation.Token;
            var v1Channel = CreateV1 ? CreateHashChannel() : null;
            var v2Channel = CreateV2 ? CreateHashChannel() : null;
            var v1Task = v1Channel == null
                ? Task.FromResult<MemoryStream?>(null)
                : Task.Run(() => HashV1FromChannel(v1Channel.Reader, ProgressState, PieceLength, V1HashCapacity, CreateV2, LastLegitFileIdx, pipelineToken)); // task run to force other thread
            var v2Task = v2Channel == null
                ? Task.FromResult<Dictionary<TorrentFileEntry, V2FileHash>?>(null)
                : Task.Run(() => HashV2FromChannel(v2Channel.Reader, ProgressState, PieceLength, pipelineToken)); // task run to force other thread

            _ = v1Task.ContinueWith(
                _ => pipelineCancellation.Cancel(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            _ = v2Task.ContinueWith(
                _ => pipelineCancellation.Cancel(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            var reader = new FileDistributor(ProgressState);

            try {
                await reader.Read(AllFiles, v1Channel?.Writer, v2Channel?.Writer, pipelineToken);
                v1Channel?.Writer.Complete();
                v2Channel?.Writer.Complete();
            } catch (Exception ex) {
                pipelineCancellation.Cancel();
                v1Channel?.Writer.TryComplete(ex);
                v2Channel?.Writer.TryComplete(ex);

                try {
                    await Task.WhenAll(v1Task, v2Task);
                } catch { }

                if (v1Task.IsFaulted)
                    await v1Task;

                if (v2Task.IsFaulted)
                    await v2Task;

                throw;
            }

            return new HashResult(await v1Task, await v2Task);
        }

        private static Channel<HashWorkItem> CreateHashChannel()
        {
            return Channel.CreateBounded<HashWorkItem>(new BoundedChannelOptions(32) {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        private static async Task<MemoryStream?> HashV1FromChannel(
            ChannelReader<HashWorkItem> Reader,
            TorrentCreationProgress ProgressState,
            int PieceLength,
            int V1HashCapacity,
            bool Hybrid,
            int LastLegitFileIdx,
            CancellationToken CancellationToken)
        {
            var hasher = new V1PieceHasher(ProgressState, PieceLength, V1HashCapacity);

            await foreach (var item in Reader.ReadAllAsync(CancellationToken)) {
                if (item.Type == HashWorkItemType.Data) {
                    var block = item.Block!;
                    try {
                        hasher.Add(block.AsSpan());
                    } finally {
                        block.Release();
                    }
                } else if (item.Type == HashWorkItemType.FileCompleted && Hybrid && item.FileIndex < LastLegitFileIdx && item.Entry.Info.Length > 0) {
                    // padding for hybrid bep52 https://www.bittorrent.org/beps/bep_0052.html#upgrade-path
                    hasher.AddPadding(GetPaddingLength(item.Entry.Info.Length, PieceLength), PieceLength);
                }
            }

            hasher.Flush();
            return hasher.Hashes;
        }

        private static async Task<Dictionary<TorrentFileEntry, V2FileHash>?> HashV2FromChannel(
            ChannelReader<HashWorkItem> Reader,
            TorrentCreationProgress ProgressState,
            int PieceLength,
            CancellationToken CancellationToken)
        {
            var hasher = new V2FileHasher(ProgressState, PieceLength);
            var hashes = new Dictionary<TorrentFileEntry, V2FileHash>();

            await foreach (var item in Reader.ReadAllAsync(CancellationToken)) {
                if (item.Type == HashWorkItemType.FileStarted) {
                    hasher.Reset(item.Entry.Info.Length);
                } else if (item.Type == HashWorkItemType.Data) {
                    var block = item.Block!;
                    try {
                        hasher.Add(block.AsSpan());
                    } finally {
                        block.Release();
                    }
                } else if (item.Type == HashWorkItemType.FileCompleted) {
                    var fileHash = hasher.BuildHash();
                    if (fileHash != null)
                        hashes.Add(item.Entry, fileHash);
                }
            }

            return hashes;
        }

        private static BDictionary BuildTorrentDictionary(
            string Path,
            bool SingleFile,
            List<TorrentFileEntry> AllFiles,
            MemoryStream? V1PieceHashes,
            Dictionary<TorrentFileEntry, V2FileHash>? V2Hashes,
            int PieceLength,
            IList<string>? Trackers,
            IList<string>? WebSeeds,
            string? Comments,
            string? Source,
            bool IsPrivate,
            bool Entropy,
            bool EmitCreationDate)
        {
            var dict = new BDictionary();

            if (Trackers?.Any() == true) {
                dict["announce"] = new BString(Trackers[0]);
                var announceList = new BList();
                announceList.Value.Add(new BList(Trackers));
                dict["announce-list"] = announceList;
            }

            if (WebSeeds?.Any() == true)
                dict["url-list"] = new BList(WebSeeds);

            if (!String.IsNullOrEmpty(Comments))
                dict["comment"] = new BString(Comments);

            if (EmitCreationDate)
                dict["creation date"] = new BNumber(DateTime.UtcNow);

            var info = new BDictionary {
                ["name"] = new BString(SingleFile ? System.IO.Path.GetFileName(Path) : new DirectoryInfo(Path).Name)
            };
            info.Add("piece length", PieceLength);

            if (IsPrivate)
                info.Add("private", 1);

            if (!String.IsNullOrEmpty(Source))
                info["source"] = new BString(Source);

            if (Entropy) {
                var buffer = new byte[32];
                Random.Shared.NextBytes(buffer);
                info["entropy"] = new BString(buffer);
            }

            if (V1PieceHashes != null)
                AddV1Info(info, SingleFile, AllFiles, V1PieceHashes, PieceLength, V2Hashes != null);

            if (V2Hashes != null)
                AddV2Info(dict, info, AllFiles, V2Hashes);

            dict["info"] = info;
            return dict;
        }

        private static long GetPaddingLength(long Length, int PieceLength)
        {
            long remainder = Length % PieceLength;
            return remainder == 0 ? 0 : PieceLength - remainder;
        }

        private static void ValidatePieceLength(int PieceLength, bool CreateV2)
        {
            if (PieceLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(PieceLength), "Piece length must be positive.");

            if ((PieceLength & (PieceLength - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(PieceLength), "Piece length must be a power of two.");

            if (CreateV2 && PieceLength < MerkleBlockSize)
                throw new ArgumentOutOfRangeException(nameof(PieceLength), "V2 torrents require a piece length of at least 16 KiB.");
        }

        private record HashResult(MemoryStream? V1Hashes, Dictionary<TorrentFileEntry, V2FileHash>? V2Hashes);

        private enum HashWorkItemType
        {
            FileStarted,
            Data,
            FileCompleted
        }

        private record HashWorkItem(HashWorkItemType Type, int FileIndex, TorrentFileEntry Entry, PooledReadBlock? Block);

        private class PooledReadBlock
        {
            private readonly byte[] Buffer;
            private readonly int Capacity;
            private int ReferenceCount = 1;

            private PooledReadBlock(int MinimumLength)
            {
                Buffer = ArrayPool<byte>.Shared.Rent(MinimumLength);
                Capacity = MinimumLength;
            }

            public int Length { get; private set; }

            public Memory<byte> Memory => Buffer.AsMemory(0, Capacity);

            public static PooledReadBlock Rent(int MinimumLength)
            {
                return new PooledReadBlock(MinimumLength);
            }

            public void SetLength(int In)
            {
                this.Length = In;
            }

            public void Retain()
            {
                Interlocked.Increment(ref ReferenceCount);
            }

            public void Release()
            {
                if (Interlocked.Decrement(ref ReferenceCount) == 0)
                    ArrayPool<byte>.Shared.Return(Buffer);
            }

            public Span<byte> AsSpan()
            {
                return Buffer.AsSpan(0, Length);
            }
        }

        private class TorrentCreationProgress(long TotalSize, Action<(float HashProgress, string CurrentFile, float FileProgress, float FileBuffer, string HashExcerpt)>? Progress)
        {
            private readonly Lock Lock = new();
            private readonly Stopwatch Stopwatch = Stopwatch.StartNew();
            private long TotalRead;
            private string CurrentFile = "";
            private float FileBuffer;
            private string HashExcerpt = "";
            private float FileProgress;

            public void SetCurrentFile(string CurrentFile)
            {
                lock (Lock) {
                    this.CurrentFile = CurrentFile;
                }
            }

            public void ReportEmptyFile(string CurrentFile)
            {
                lock (Lock) {
                    this.CurrentFile = CurrentFile;
                    FileProgress = 100;
                    Emit();
                }
            }

            public void ReportHashProgress(Span<byte> HashExcerpt)
            {
                lock (Lock) {
                    if (Stopwatch.ElapsedMilliseconds <= 100)
                        return;

                    this.HashExcerpt = Convert.ToHexString(HashExcerpt);
                }
            }

            public void ReportRead(long FileLength, long FileRead, int BytesRead)
            {
                lock (Lock) {
                    TotalRead += BytesRead;

                    if (Stopwatch.ElapsedMilliseconds <= 100)
                        return;

                    FileProgress = FileLength == 0 ? 100 : (float)FileRead / FileLength * 100f;
                    FileBuffer = BytesRead / (float)FileReadBufferSize * 100f;
                    Emit();
                    Stopwatch.Restart();
                }
            }

            public void Complete()
            {
                lock (Lock) {
                    Progress?.Invoke((100, CurrentFile, FileProgress, 0, HashExcerpt));
                }
            }

            private void Emit()
            {
                float hashProgress = TotalSize == 0 ? 100 : (float)TotalRead / TotalSize * 100f;
                Progress?.Invoke((hashProgress, CurrentFile, FileProgress, FileBuffer, HashExcerpt));
            }
        }

        private class FileDistributor(TorrentCreationProgress Progress)
        {
            public async Task Read(List<TorrentFileEntry> AllFiles, ChannelWriter<HashWorkItem>? V1Writer, ChannelWriter<HashWorkItem>? V2Writer, CancellationToken CancellationToken)
            {
                for (var x = 0;x < AllFiles.Count;x++) {
                    CancellationToken.ThrowIfCancellationRequested();

                    var entry = AllFiles[x];
                    Progress.SetCurrentFile(entry.DisplayPath);

                    await Publish(new HashWorkItem(HashWorkItemType.FileStarted, x, entry, null), V1Writer, V2Writer, CancellationToken);

                    if (entry.Info.Length == 0) {
                        Progress.ReportEmptyFile(entry.DisplayPath);
                        await Publish(new HashWorkItem(HashWorkItemType.FileCompleted, x, entry, null), V1Writer, V2Writer, CancellationToken);
                        continue;
                    }

                    await ReadFile(entry, x, V1Writer, V2Writer, CancellationToken);
                    await Publish(new HashWorkItem(HashWorkItemType.FileCompleted, x, entry, null), V1Writer, V2Writer, CancellationToken);
                }
            }

            public async Task ReadSynchronously(
                List<TorrentFileEntry> AllFiles,
                V1PieceHasher? V1Hasher,
                V2FileHasher? V2Hasher,
                Dictionary<TorrentFileEntry, V2FileHash>? V2Hashes,
                bool Hybrid,
                int PieceLength,
                int LastLegitFileIdx,
                CancellationToken CancellationToken)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(FileReadBufferSize);

                try {
                    for (var x = 0;x < AllFiles.Count;x++) {
                        CancellationToken.ThrowIfCancellationRequested();

                        var entry = AllFiles[x];
                        Progress.SetCurrentFile(entry.DisplayPath);
                        V2Hasher?.Reset(entry.Info.Length);

                        if (entry.Info.Length == 0) {
                            Progress.ReportEmptyFile(entry.DisplayPath);
                        } else {
                            long readSize = 0;

                            await using var fs = new FileStream(entry.Info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                            int length;
                            while ((length = await fs.ReadAsync(buffer.AsMemory(0, FileReadBufferSize), CancellationToken)) != 0) {
                                var bytes = buffer.AsSpan(0, length);
                                V1Hasher?.Add(bytes);
                                V2Hasher?.Add(bytes);

                                readSize += length;
                                Progress.ReportRead(entry.Info.Length, readSize, length);
                            }

                            if (readSize != entry.Info.Length)
                                throw new InvalidOperationException("Reported file size is not equal to read size");
                        }

                        if (V2Hashes != null) {
                            var fileHash = V2Hasher!.BuildHash();
                            if (fileHash != null)
                                V2Hashes.Add(entry, fileHash);
                        }

                        if (Hybrid && x < LastLegitFileIdx && entry.Info.Length > 0)
                            V1Hasher!.AddPadding(GetPaddingLength(entry.Info.Length, PieceLength), PieceLength);
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            private async Task ReadFile(TorrentFileEntry Entry, int FileIndex, ChannelWriter<HashWorkItem>? V1Writer, ChannelWriter<HashWorkItem>? V2Writer, CancellationToken CancellationToken)
            {
                long readSize = 0;

                await using var fs = new FileStream(Entry.Info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                while (true) {
                    var block = PooledReadBlock.Rent(FileReadBufferSize);
                    int length;
                    try {
                        length = await fs.ReadAsync(block.Memory, CancellationToken);
                        if (length == 0)
                            break;

                        block.SetLength(length);
                        var item = new HashWorkItem(HashWorkItemType.Data, FileIndex, Entry, block);
                        await Publish(item, V1Writer, V2Writer, CancellationToken);
                    } finally {
                        block.Release();
                    }

                    readSize += length;
                    Progress.ReportRead(Entry.Info.Length, readSize, length);
                }

                if (readSize != Entry.Info.Length)
                    throw new InvalidOperationException("Reported file size is not equal to read size");
            }

            private static async ValueTask Publish(HashWorkItem Item, ChannelWriter<HashWorkItem>? V1Writer, ChannelWriter<HashWorkItem>? V2Writer, CancellationToken CancellationToken)
            {
                if (Item.Block == null) {
                    if (V1Writer != null)
                        await V1Writer.WriteAsync(Item, CancellationToken);
                    if (V2Writer != null)
                        await V2Writer.WriteAsync(Item, CancellationToken);

                    return;
                }

                await PublishData(Item, V1Writer, CancellationToken);
                await PublishData(Item, V2Writer, CancellationToken);
            }

            private static async ValueTask PublishData(HashWorkItem Item, ChannelWriter<HashWorkItem>? Writer, CancellationToken CancellationToken)
            {
                if (Writer == null)
                    return;

                Item.Block!.Retain();
                try {
                    await Writer.WriteAsync(Item, CancellationToken);
                } catch {
                    Item.Block.Release();
                    throw;
                }
            }
        }
    }
}
