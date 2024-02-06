using BencodeNET.Objects;
using BencodeNET.Torrents;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

using static RTSharp.Shared.Utils.TorrentCreator;

namespace RTSharp.Shared.Utils
{
    public class TorrentCreator
    {
        public record DataInfo(long TotalSize, FileInfo? SingleFile, List<FileInfo>? Files);

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

        public async ValueTask<DataInfo> GetDataInfo(string Path)
        {
            if (!Path.EndsWith(System.IO.Path.DirectorySeparatorChar))
                Path += System.IO.Path.DirectorySeparatorChar;

            var allFiles = new ConcurrentBag<FileInfo>();

            bool singleFile = File.Exists(Path);
            long totalSize;
            if (singleFile) {
                var info = new FileInfo(Path);
                totalSize = info.Length;
                return new(totalSize, info, null);
            } else {
                var task = Task.Run(() => GetDirectorySize(allFiles, new DirectoryInfo(Path)));
                return new(await task, null, allFiles.OrderBy(file => file.FullName[Path.Length..].Contains(System.IO.Path.DirectorySeparatorChar)).ThenBy(file => Regex.Replace(file.FullName, @"\d+", match => match.Value.PadLeft(4, '0'))).ToList());
            }
        }

        public int CalculatePieceLength(long TotalSize)
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

        public uint CalculateNumberOfPieces(long TotalSize, int PieceLength)
        {
            return (uint)Math.Ceiling((decimal)TotalSize / PieceLength);
        }

        public async Task<byte[]> Create(
            string Path,
            IList<string>? Trackers,
            IList<string>? WebSeeds,
            string? Comments,
            string? Source,
            bool Private,
            bool Entropy,
            bool EmitCreationDate,
            int? PieceLength = null,
            bool Parallel = true,
            IProgress<(float HashProgress, string CurrentFile, float FileProgress, float FileBuffer, string HashExcerpt)> Progress = null,
            CancellationToken cancellationToken = default)
        {
            Progress.Report((0, "Calculating total size...", 0, 0, ""));
            var dataInfo = await GetDataInfo(Path);
            int pieceLength = PieceLength ?? CalculatePieceLength(dataInfo.TotalSize);
            
            float hashProgress = 0;
            string currentFile = "";
            float fileProgress = 0;
            float fileBuffer = 0;
            string hashExcerpt = "";

            void emitProgress()
            {
                Progress.Report((hashProgress, currentFile, fileProgress, fileBuffer, hashExcerpt));
            }

            bool singleFile = dataInfo.SingleFile != null;

            var torrent = new Torrent() {
                CreationDate = DateTime.UtcNow,
                Files = singleFile ? null : new MultiFileInfoList(System.IO.Path.GetFileName(Path)),
                File = singleFile ? new SingleFileInfo() {
                    FileName = System.IO.Path.GetFileName(Path),
                    FileSize = dataInfo.TotalSize
                } : null,
                PieceSize = pieceLength,
                IsPrivate = Private
            };

            var allFiles = dataInfo.Files ?? new List<FileInfo>();

            if (singleFile) {
                allFiles.Add(new FileInfo(Path));
            }

            var pipeLimit = (int)Math.Floor(32m * 1024 * 1024 / pieceLength);
            var dataPipe = Channel.CreateBounded<(int, byte[])>(new BoundedChannelOptions(pipeLimit) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = Parallel ? false : true,
                SingleWriter = true
            });

            Memory<byte> pieces = new byte[CalculateNumberOfPieces(dataInfo.TotalSize, pieceLength) * 20];

            var readerTask = Task.Run(async () =>
            {
                int length = 0;
                int index = 0;
                int x = 0;

                Memory<byte> buf = new byte[(int)torrent.PieceSize];
                var sw = Stopwatch.StartNew();

                foreach (FileInfo file in allFiles) {
                    long readSize = 0;

                    if (!singleFile) {
                        currentFile = file.FullName[Path.Length..].TrimStart(new[] { '/', '\\' });

                        torrent.Files!.Add(new MultiFileInfo() {
                            FullPath = file.FullName,
                            FileSize = file.Length,
                            Path = currentFile.Split(System.IO.Path.DirectorySeparatorChar)
                        });
                    } else {
                        currentFile = System.IO.Path.GetFileName(file.FullName);
                    }

                    if (file.LinkTarget != null)
                        continue;

                    using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);

                    while ((length = await fs.ReadAsync(buf[index..])) != 0) {
                        index += length;
                        if (index == buf.Length) {
                            await dataPipe.Writer.WriteAsync((x++, buf.ToArray()));
                            index = 0;
                        }

                        readSize += length;
                        if (sw.ElapsedMilliseconds > 100) {
                            if (cancellationToken.IsCancellationRequested) {
                                dataPipe.Writer.Complete();
                                return;
                            }
                            fileProgress = (float)readSize / file.Length * 100f;
                            fileBuffer = (float)dataPipe.Reader.Count / pipeLimit * 100f;
                            emitProgress();
                            sw.Restart();
                        }
                    }

                    if (readSize != file.Length)
                        throw new InvalidOperationException("Reported file size is not equal to read size");
                }

                if (index != 0) {
                    await dataPipe.Writer.WriteAsync((x++, buf[..index].ToArray()));
                }

                dataPipe.Writer.Complete();
            });

            async Task hashTask()
            {
                using var sha1 = SHA1.Create();

                await foreach (var (x, buf) in dataPipe.Reader.ReadAllAsync()) {
                    var hash = sha1.ComputeHash(buf);

                    hash.CopyTo(pieces[(x * 20)..((x + 1) * 20)]);

                    hashProgress = x / (pieces.Length / 20f) * 100f;
                    hashExcerpt = Convert.ToHexString(hash);
                }
            }

            Task[] hashTasks;
            if (Parallel) {
                hashTasks = new Task[Environment.ProcessorCount];
                for (var x = 0;x < hashTasks.Length;x++) {
                    hashTasks[x] = hashTask();
                }
            } else {
                hashTasks = new Task[1];
                hashTasks[0] = hashTask();
            }

            await readerTask;
            await Task.WhenAll(hashTasks);

            emitProgress();

            cancellationToken.ThrowIfCancellationRequested();

            torrent.Pieces = pieces.ToArray();

            if (Trackers?.Any() == true)
                torrent.Trackers.Add(Trackers);

            var dict = torrent.ToBDictionary();

            if (WebSeeds?.Any() == true)
                dict["url-list"] = new BList(WebSeeds, torrent.Encoding);

            if (!String.IsNullOrEmpty(Comments))
                torrent.Comment = Comments;
            
            if (!String.IsNullOrEmpty(Source))
                dict["source"] = new BString(Source);

            if (Entropy) {
                var buffer = new byte[32];
                Random.Shared.NextBytes(buffer);
                ((BDictionary)dict["info"])["entropy"] = new BString(buffer);
            }

            if (EmitCreationDate)
                torrent.CreationDate = DateTime.UtcNow;

            return dict.EncodeAsBytes();
        }
    }
}
