using BencodeNET.Objects;
using BencodeNET.Torrents;

using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

using System.Buffers;
using System.Security.Cryptography;

namespace RTSharp.Shared.Utils;

public static partial class BencodeUtils
{
    /// <summary>
    /// Gets info hashes from .torrent bencode root
    /// </summary>
    /// <param name="BTorrent">.torrent</param>
    /// <returns>V1/V2 info hashes if exist</returns>
    public static (byte[]? V1, byte[]? V2) GetInfoHashes(BDictionary BTorrent)
    {
        var info = BTorrent.Get<BDictionary>(TorrentFields.Info);

        using var ms = new MemoryStream(info.GetSizeInBytes());
        info.EncodeTo(ms);
        var infoBytes = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        var v1 = info.ContainsKey("pieces")
            ? SHA1.HashData(infoBytes)
            : null;

        var v2 = info.ContainsKey("file tree")
            ? SHA256.HashData(infoBytes)
            : null;

        return (v1, v2);
    }

    /// <summary>
    /// A single file in a torrent
    /// </summary>
    public readonly record struct TorrentFile(string[] PathComponents, long FileSize)
    {
        public string Path => System.IO.Path.Combine(PathComponents);
    }

    /// <summary>
    /// Reads "file tree" (v2) or "files" (v1), without BEP 47 padding files as they aren't part of the content
    /// </summary>
    public static (TorrentFile[] Files, bool IsMultiFile) GetFiles(BDictionary Info)
    {
        var fileTree = Info.Get<BDictionary>("file tree");
        if (fileTree != null) {
            var entries = new List<FileTreeEntry>();
            WalkFileTree(fileTree, [], entries);

            var files = entries.Select(e => e.File).ToArray();
            // A lone entry whose path is the torrent name is a single-file torrent; anything
            // else lives under a content directory named after the torrent.
            var singleFile = files is [{ PathComponents: [ var only ] }] && only == GetName(Info);
            return (files, !singleFile);
        }

        var filesList = Info.Get<BList>("files");
        if (filesList == null)
            return ([ new TorrentFile([ GetName(Info) ], Info.Get<BNumber>("length")?.Value ?? throw new InvalidDataException("Single-file torrent has no \"length\"")) ], false);

        return ([ .. ReadFilesList(filesList).Where(x => !x.IsPadding).Select(x => x.File) ], true);
    }

    /// <summary>
    /// Reads the v1 "files" list as it is, where BEP 47 padding files take up space in the pieces
    /// </summary>
    private static (TorrentFile File, bool IsPadding)[] ReadFilesList(BList FilesList) =>
        [ .. FilesList.Cast<BDictionary>().Select(f => (
            new TorrentFile(
                (f.Get<BList>("path") ?? f.Get<BList>("path.utf-8"))?.AsStrings().ToArray()
                    ?? throw new InvalidDataException("Torrent \"files\" entry has no \"path\""),
                f.Get<BNumber>("length")?.Value
                    ?? throw new InvalidDataException("Torrent \"files\" entry has no \"length\"")
            ),
            f.Get<BString>("attr")?.ToString().Contains('p') == true
        )) ];

    /// <summary>
    /// Total size of the BEP 47 padding files in the v1 "files" list, which a hybrid torrent has as well
    /// </summary>
    public static long GetPaddingSize(BDictionary Info)
    {
        var filesList = Info.Get<BList>("files");
        return filesList == null ? 0 : ReadFilesList(filesList).Where(x => x.IsPadding).Sum(x => x.File.FileSize);
    }

    /// <summary>
    /// Adds a <paramref name="BaseDirectory" /> to the start of each file path
    /// </summary>
    public static TorrentFile[] GetFilesOnDisk(BDictionary Info, string BaseDirectory)
    {
        var (files, isMultiFile) = GetFiles(Info);
        var contentDir = isMultiFile ? Path.Combine(BaseDirectory, GetName(Info)) : BaseDirectory;
        return files
            .Select(f => f with { PathComponents = [ contentDir, ..f.PathComponents ] })
            .ToArray();
    }

    /// <summary>
    /// Whether the name and every file path component stay inside the directory they get combined with.
    /// Needed before using a .torrent from an untrusted source.
    /// </summary>
    public static bool HasSafePaths(BDictionary Info)
    {
        static bool isSafe(string Component) =>
            Component.Length != 0 && Component != "." && Component != ".." && Component.IndexOfAny([ '/', '\\', '\0' ]) == -1;

        // Padding files included, as not all clients know what they are and would create them
        var filesList = Info.Get<BList>("files");

        return isSafe(GetName(Info))
            && GetFiles(Info).Files.All(x => x.PathComponents.All(isSafe))
            && (filesList == null || ReadFilesList(filesList).All(x => x.File.PathComponents.All(isSafe)));
    }

    /// <summary>
    /// How one torrent's files line up with another torrent's files on disk
    /// </summary>
    /// <param name="DiskFiles">The file on disk for each file of the torrent being matched, in its <see cref="GetFiles"/> order</param>
    /// <param name="Strict">Both torrents have the same layout, so the base directory on disk can be used as is. Never set when matching against several torrents</param>
    public record FileTreeMatch(TorrentFile[] DiskFiles, bool Strict);

    /// <summary>
    /// Pairs a torrent's files with the files of other torrents on disk, first by relative path and then by
    /// size where that size is not shared with another unpaired file. Files at the same relative path must have the
    /// same size.
    /// </summary>
    /// <param name="Info">Info dictionary of the torrent being matched</param>
    /// <param name="OnDisk">Info dictionary and base directory of each torrent whose files are on disk</param>
    /// <returns>The match, or <c>null</c> if the files differ or can't be paired unambiguously</returns>
    public static FileTreeMatch? MatchFileTrees(BDictionary Info, IReadOnlyList<(BDictionary Info, string BaseDirectory)> OnDisk)
    {
        var (files, isMultiFile) = GetFiles(Info);
        var onDiskFiles = OnDisk.SelectMany(x => GetFiles(x.Info).Files).ToArray();

        if (files.Length != onDiskFiles.Length)
            return null;

        var filesOnDisk = OnDisk.SelectMany(x => GetFilesOnDisk(x.Info, x.BaseDirectory)).ToArray();

        var unpairedOnDisk = new Dictionary<string, int>(onDiskFiles.Length);
        for (var x = 0;x < onDiskFiles.Length;x++)
            unpairedOnDisk[string.Join('/', onDiskFiles[x].PathComponents)] = x;

        var result = new TorrentFile[files.Length];
        var unpaired = new List<int>();

        for (var x = 0;x < files.Length;x++) {
            var path = string.Join('/', files[x].PathComponents);

            if (unpairedOnDisk.TryGetValue(path, out var onDisk)) {
                // Another file at the same path, even if its contents are found under another path. Could be enabled in the future with folders named after the info hash
                if (onDiskFiles[onDisk].FileSize != files[x].FileSize)
                    return null;

                result[x] = filesOnDisk[onDisk];
                unpairedOnDisk.Remove(path);
            } else
                unpaired.Add(x);
        }

        var strict = OnDisk is [ var only ] && unpaired.Count == 0 && isMultiFile == GetFiles(only.Info).IsMultiFile && GetName(Info) == GetName(only.Info);

        var onDiskBySize = unpairedOnDisk.Values.ToLookup(x => onDiskFiles[x].FileSize);
        var unpairedBySize = unpaired.ToLookup(x => files[x].FileSize);

        foreach (var x in unpaired) {
            var size = files[x].FileSize;

            if (unpairedBySize[size].Count() != 1 || onDiskBySize[size].Count() != 1)
                return null;

            result[x] = filesOnDisk[onDiskBySize[size].Single()];
        }

        return new FileTreeMatch(result, strict);
    }

    /// <summary>
    /// A single torrent piece hash, with byte offsets relative to the start of the
    /// file it belongs to.
    /// </summary>
    public readonly record struct PieceHash(ulong Start, ulong End, byte[] Hash);

    /// <summary>
    /// Piece hashes fully contained within a single file.
    /// <paramref name="PieceHashes"/> excludes pieces that exceed a file boundary, so
    /// every entry maps onto exactly one torrent piece. <paramref name="PieceRange"/>
    /// is the range of global piece indices covered.
    /// </summary>
    public record FilePieceHashes(TorrentFile File, PieceHash[] PieceHashes, Range PieceRange);

    /// <summary>
    /// For each file in the torrent, computes the piece hashes fully contained within
    /// that file, with byte offsets relative to the start of the file. Files smaller
    /// than the piece size (or with no fully-contained piece) yield an empty
    /// <see cref="FilePieceHashes.PieceHashes"/> array and a <c>0..0</c>
    /// <see cref="FilePieceHashes.PieceRange"/>.
    /// </summary>
    public static FilePieceHashes[] AssignHashesToPieces(BDictionary BTorrent)
    {
        var info = BTorrent.Get<BDictionary>(TorrentFields.Info)
            ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        if (info.ContainsKey("file tree"))
            throw new InvalidDataException("AssignHashesToPieces is v1-only; use AssignHashesToPiecesV2 for v2/hybrid torrents");

        var pieceSize = GetPieceLength(info);

        var pieces = GetPieces(info)
            ?? throw new InvalidDataException("Torrent has no \"pieces\"");

        // Padding files get no result, but the offsets of the files after them depend on their size
        var filesList = info.Get<BList>("files");
        (TorrentFile File, bool IsPadding)[] files = filesList != null ? ReadFilesList(filesList) : [ (GetFiles(info).Files[0], false) ];

        var result = new List<FilePieceHashes>(files.Length);

        for (var x = 0; x < files.Length; x++) {
            var (file, isPadding) = files[x];

            if (isPadding)
                continue;

            if (file.FileSize < pieceSize) {
                result.Add(new FilePieceHashes(file, [], 0..0));
                continue;
            }

            /*
             * Assuming:
             *
             * Torrent containing:
             * file1 (1000 bytes)
             * file2 (3000 bytes)
             * file3 (2000 bytes)
             * file4 (405000 bytes)
             * file5 (155120 bytes)
             *
             * Piece length: 16384 bytes
             *
             * Current `file`: file4
             */

            /*
             * Start bytes in the whole torrent
             *
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                         ^
             */
            var start = 0L;
            for (var i = 0; i < x; i++)
                start += files[i].File.FileSize;

            var unaligned = start % pieceSize != 0 ? 1 : 0;

            /*
             * Start bytes in the whole torrent aligned to piece size, but after `start`
             *
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                         ^^
             */
            var startPieceAligned = ((start / pieceSize) + unaligned) * pieceSize;

            /*
             * End bytes in the whole torrent
             *
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                                             ^
             */
            var end = start + file.FileSize;

            /*
             * End bytes in the whole torrent aligned to piece size, but before end bytes in the whole torrent
             *
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                                            ^^
             */
            var endingBytesAligned = (end / pieceSize) * pieceSize;

            /*
             * Offset of starting piece of the file
             *
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          ^
             */
            var startingPiecesBytes = (int)(startPieceAligned / pieceSize) * SHA1.HashSizeInBytes;

            // Same for ending...
            var endingPiecesBytes = (int)(endingBytesAligned / pieceSize) * SHA1.HashSizeInBytes;

            /*
             * Pieces that belong to the file
             *
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          <------------------------------>
             */
            var piecesBytesRange = pieces[startingPiecesBytes..endingPiecesBytes];

            var pieceHashes = piecesBytesRange.Chunk(SHA1.HashSizeInBytes).Select((hash, i) => new PieceHash(
                Start: (ulong)((startPieceAligned - start) + (i * pieceSize)),
                End: (ulong)((startPieceAligned - start) + ((i + 1) * pieceSize)),
                Hash: hash
            )).ToArray();

            result.Add(new FilePieceHashes(
                file,
                pieceHashes,
                (startingPiecesBytes / SHA1.HashSizeInBytes)..(endingPiecesBytes / SHA1.HashSizeInBytes)));
        }

        return [ .. result ];
    }

    /// <summary>
    /// Perform file existence, size, and random piece hash verification for a torrent's files on disk
    /// </summary>
    /// <param name="BTorrent">.torrent bencode root</param>
    /// <param name="GetDiskFiles">Resolves where each file is on disk from the info dictionary, in <see cref="GetFiles"/> order</param>
    /// <param name="Logger"></param>
    /// <returns></returns>
    public static async Task<bool> FastVerifyFilesOnDisk(BDictionary BTorrent, Func<BDictionary, TorrentFile[]> GetDiskFiles, ILogger Logger)
    {
        var info = BTorrent.Get<BDictionary>(TorrentFields.Info)
            ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        /*
         * Prefer the v2 path whenever v2 metadata is present, even for hybrid torrents
         * that also carry v1 pieces, as the v1 path doesn't take them.
         */
        if (info.ContainsKey("file tree"))
            return await FastVerifyFilesOnDiskV2(BTorrent, GetDiskFiles, Logger);

        var pieces = GetPieces(info);
        if (pieces == null || pieces.Length == 0) {
            Logger.LogWarning("Fast verify unavailable: torrent has no v1 piece hashes");
            return false;
        }

        var pieceLength = GetPieceLength(info);

        var files = GetDiskFiles(info);

        if (!CheckFilesExistWithSize(files, Logger))
            return false;

        var fileHashes = AssignHashesToPieces(BTorrent);
        var toVerify = new List<FileToVerify>(files.Length);
        for (var x = 0;x < files.Length;x++)
            toVerify.Add(new FileToVerify(files[x], fileHashes[x].PieceHashes, static (Data, Expected) => SHA1.HashData(Data.Span).AsSpan().SequenceEqual(Expected)));

        return await VerifyRandomPieces(toVerify, (int)pieceLength, Logger);
    }

    private static bool CheckFilesExistWithSize(IReadOnlyList<TorrentFile> Files, ILogger Logger)
    {
        foreach (var file in Files) {
            var fi = new FileInfo(file.Path);
            if (!fi.Exists || fi.Length != file.FileSize) {
                Logger.LogWarning($"Mismatch for \"{file.Path}\" (exists: {fi.Exists}, expected size: {file.FileSize})");
                return false;
            }
        }

        return true;
    }

    private record FileToVerify(TorrentFile File, PieceHash[] Pieces, Func<ReadOnlyMemory<byte>, byte[], bool> FxHashMatches);

    private static async Task<bool> VerifyRandomPieces(IReadOnlyList<FileToVerify> Files, int BufferSize, ILogger Logger)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        var verified = 0;

        try {
            foreach (var file in Files) {
                var pieceHashes = file.Pieces;
                if (pieceHashes.Length == 0)
                    continue;

                var scaled = 50.0 * Math.Sqrt(file.File.FileSize / (1024d * 1024 * 1024));
                var toCheck = Math.Min(Math.Clamp((int)Math.Ceiling(scaled), 1, 200), pieceHashes.Length);

                try {
                    using var handle = File.OpenHandle(file.File.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);

                    for (var i = 0;i < toCheck;i++) {
                        var piece = pieceHashes[Random.Shared.Next(pieceHashes.Length)];

                        if (!await VerifyPiece(handle, piece.Start, (int)(piece.End - piece.Start), piece.Hash, buf, file.FxHashMatches)) {
                            Logger.LogWarning($"Fast resume: piece hash mismatch in \"{file.File.Path}\" at offset {piece.Start}");
                            return false;
                        }

                        verified++;
                    }
                } catch (Exception ex) {
                    Logger.LogWarning(ex, $"Fast resume: failed to read piece in \"{file.File.Path}\"");
                    return false;
                }
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buf);
        }

        Logger.LogInformation($"Fast resume: verified {verified} random piece(s)");
        return true;
    }

    private static async Task<bool> VerifyPiece(SafeFileHandle Handle, ulong Start, int Length, byte[] ExpectedHash, Memory<byte> Buf, Func<ReadOnlyMemory<byte>, byte[], bool> HashMatches)
    {
        var buffer = Buf[..Length];
        var total = 0;
        while (total < Length) {
            var sz = await RandomAccess.ReadAsync(Handle, buffer[total..], (long)Start + total);
            if (sz == 0)
                return false;
            total += sz;
        }

        return HashMatches(buffer, ExpectedHash);
    }
}
