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
    /// Torrent display name
    /// </summary>
    public static string GetName(BDictionary Info) =>
        Info.Get<BString>("name")?.ToString()
        ?? Info.Get<BString>("name.utf-8")?.ToString()
        ?? throw new InvalidDataException("Torrent has no \"name\"");

    /// <summary>
    /// The piece length (bytes per piece).
    /// </summary>
    public static long GetPieceLength(BDictionary Info) =>
        Info.Get<BNumber>("piece length")?.Value
        ?? throw new InvalidDataException("Torrent has no \"piece length\"");

    /// <summary>
    /// The raw concatenated v1 SHA-1 piece hashes ("pieces"), or <c>null</c> when the
    /// info dictionary carries no v1 metadata (a v2-only torrent).
    /// </summary>
    public static byte[]? GetPieces(BDictionary Info) =>
        Info.Get<BString>("pieces")?.Value.ToArray();

    /// <summary>
    /// The optional free-text "comment" from the .torrent root, or <c>null</c> if absent.
    /// </summary>
    public static string? GetComment(BDictionary BTorrent) =>
        BTorrent.Get<BString>("comment")?.ToString()
        ?? BTorrent.Get<BString>("comment.utf-8")?.ToString();

    /// <summary>
    /// A single file in a torrent
    /// </summary>
    public readonly record struct TorrentFile(string[] PathComponents, long FileSize)
    {
        public string Path => System.IO.Path.Combine(PathComponents);
    }

    /// <summary>
    /// Reads "file tree" (v2) or "files" (v1)
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

        var multiFiles = filesList.Cast<BDictionary>().Select(f => new TorrentFile(
            (f.Get<BList>("path") ?? f.Get<BList>("path.utf-8"))?.AsStrings().ToArray()
                ?? throw new InvalidDataException("Torrent \"files\" entry has no \"path\""),
            f.Get<BNumber>("length")?.Value
                ?? throw new InvalidDataException("Torrent \"files\" entry has no \"length\"")
        )).ToArray();
        return (multiFiles, true);
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

        var files = GetFiles(info).Files;

        var result = new FilePieceHashes[files.Length];

        for (var x = 0; x < files.Length; x++) {
            var file = files[x];

            if (file.FileSize < pieceSize) {
                result[x] = new FilePieceHashes(file, [], 0..0);
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
                start += files[i].FileSize;

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

            result[x] = new FilePieceHashes(
                file,
                pieceHashes,
                (startingPiecesBytes / SHA1.HashSizeInBytes)..(endingPiecesBytes / SHA1.HashSizeInBytes));
        }

        return result;
    }

    /// <summary>
    /// Perform file existence, size, and random piece hash verification for a torrent's files on disk
    /// </summary>
    /// <param name="BTorrent">.torrent bencode root</param>
    /// <param name="BaseDirectory">Target base directory</param>
    /// <param name="Logger"></param>
    /// <returns></returns>
    public static async Task<bool> FastVerifyFilesOnDisk(BDictionary BTorrent, string BaseDirectory, ILogger Logger)
    {
        var info = BTorrent.Get<BDictionary>(TorrentFields.Info)
            ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        /*
         * Prefer the v2 path whenever v2 metadata is present, even for hybrid torrents
         * that also carry v1 pieces. The v1 path cannot fast verify a hybrid torrent at
         * all: BEP 52 requires v1 padding file entries, which have no counterpart on
         * disk, so its existence check always fails.
         */
        if (info.ContainsKey("file tree"))
            return await FastVerifyFilesOnDiskV2(BTorrent, BaseDirectory, Logger);

        var pieces = GetPieces(info);
        if (pieces == null || pieces.Length == 0) {
            Logger.LogWarning("Fast verify unavailable: torrent has no v1 piece hashes");
            return false;
        }

        var pieceLength = GetPieceLength(info);

        var files = GetFilesOnDisk(info, BaseDirectory);

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
