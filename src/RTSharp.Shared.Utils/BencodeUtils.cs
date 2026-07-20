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
    /// <param name="TorrentData">.torrent</param>
    /// <returns>V1/V2 info hashes if exist</returns>
    public static (byte[]? V1, byte[]? V2) GetInfoHashes(BDictionary TorrentData)
    {
        var info = TorrentData.Get<BDictionary>(TorrentFields.Info);

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
    public record FilePieceHashes(string FullPath, long FileSize, PieceHash[] PieceHashes, Range PieceRange);

    /// <summary>
    /// For each file in the torrent, computes the piece hashes fully contained within
    /// that file, with byte offsets relative to the start of the file. Files smaller
    /// than the piece size (or with no fully-contained piece) yield an empty
    /// <see cref="FilePieceHashes.PieceHashes"/> array and a <c>0..0</c>
    /// <see cref="FilePieceHashes.PieceRange"/>.
    /// </summary>
    public static FilePieceHashes[] AssignHashesToPieces(Torrent BTorrent)
    {
        var files = BTorrent.Files
            ?.Select(x => (FullPath: x.FullPath.Replace(Path.DirectorySeparatorChar, '/'), FileSize: x.FileSize))
            ?.ToArray() ?? [ (BTorrent.File.FileName, BTorrent.File.FileSize) ];

        var result = new FilePieceHashes[files.Length];

        for (var x = 0; x < files.Length; x++) {
            var file = files[x];

            if (file.FileSize < BTorrent.PieceSize) {
                result[x] = new FilePieceHashes(file.FullPath, file.FileSize, [], 0..0);
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

            var unaligned = start % BTorrent.PieceSize != 0 ? 1 : 0;

            /*
             * Start bytes in the whole torrent aligned to piece size, but after `start`
             *
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                         ^^
             */
            var startPieceAligned = ((start / BTorrent.PieceSize) + unaligned) * BTorrent.PieceSize;

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
            var endingBytesAligned = (end / BTorrent.PieceSize) * BTorrent.PieceSize;

            /*
             * Offset of starting piece of the file
             *
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          ^
             */
            var startingPiecesBytes = (int)(startPieceAligned / BTorrent.PieceSize) * SHA1.HashSizeInBytes;

            // Same for ending...
            var endingPiecesBytes = (int)(endingBytesAligned / BTorrent.PieceSize) * SHA1.HashSizeInBytes;

            /*
             * Pieces that belong to the file
             *
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          <------------------------------>
             */
            var piecesBytesRange = BTorrent.Pieces[startingPiecesBytes..endingPiecesBytes];

            var pieceHashes = piecesBytesRange.Chunk(SHA1.HashSizeInBytes).Select((hash, i) => new PieceHash(
                Start: (ulong)((startPieceAligned - start) + (i * BTorrent.PieceSize)),
                End: (ulong)((startPieceAligned - start) + ((i + 1) * BTorrent.PieceSize)),
                Hash: hash
            )).ToArray();

            result[x] = new FilePieceHashes(
                file.FullPath,
                file.FileSize,
                pieceHashes,
                (startingPiecesBytes / SHA1.HashSizeInBytes)..(endingPiecesBytes / SHA1.HashSizeInBytes));
        }

        return result;
    }

    /// <summary>
    /// To-be file on disk based on torrent bencode data
    /// </summary>
    /// <param name="DiskPath">Path</param>
    /// <param name="Size">File size</param>
    public readonly record struct FileWithSize(string DiskPath, long Size);

    /// <summary>
    /// Perform file existence, size, and random piece hash verification for a torrent's files on disk
    /// </summary>
    /// <param name="BTorrent">.torrent</param>
    /// <param name="BaseDirectory">Target base directory</param>
    /// <param name="Logger"></param>
    /// <param name="TorrentData">.torrent bencode root, required for v2 only torrents</param>
    /// <returns></returns>
    public static async Task<bool> FastVerifyFilesOnDisk(Torrent BTorrent, string BaseDirectory, ILogger Logger, BDictionary? TorrentData = null)
    {
        /*
         * Prefer the v2 path whenever v2 metadata is present, even for hybrid torrents
         * that also carry v1 pieces. The v1 path cannot fast verify a hybrid torrent at
         * all: BEP 52 requires v1 padding file entries, which have no counterpart on
         * disk, so its existence check always fails.
         */
        if (TorrentData?.Get<BDictionary>(TorrentFields.Info)?.ContainsKey("file tree") == true)
            return await FastVerifyFilesOnDiskV2(TorrentData, BaseDirectory, Logger);

        if (BTorrent.Pieces == null || BTorrent.Pieces.Length <= 0) {
            Logger.LogWarning("Fast verify unavailable: torrent has no v1 piece hashes");
            return false;
        }

        var files = new List<FileWithSize>();
        if (BTorrent.Files?.Count > 0) {
            var contentDir = Path.Combine(BaseDirectory, BTorrent.DisplayName);
            foreach (var f in BTorrent.Files)
                files.Add(new FileWithSize(Path.Combine([contentDir, .. (f.Path ?? f.PathUtf8)]), f.FileSize));
        } else {
            files.Add(new FileWithSize(Path.Combine(BaseDirectory, BTorrent.DisplayName), BTorrent.File.FileSize));
        }

        if (!CheckFilesExistWithSize(files, Logger))
            return false;

        var fileHashes = AssignHashesToPieces(BTorrent);
        var toVerify = new List<FileToVerify>(Math.Min(fileHashes.Length, files.Count));
        for (var x = 0;x < toVerify.Capacity;x++)
            toVerify.Add(new FileToVerify(files[x], fileHashes[x].PieceHashes, static (Data, Expected) => SHA1.HashData(Data.Span).AsSpan().SequenceEqual(Expected)));

        return await VerifyRandomPieces(toVerify, (int)BTorrent.PieceSize, Logger);
    }

    private static bool CheckFilesExistWithSize(IReadOnlyList<FileWithSize> Files, ILogger Logger)
    {
        foreach (var file in Files) {
            var fi = new FileInfo(file.DiskPath);
            if (!fi.Exists || fi.Length != file.Size) {
                Logger.LogWarning($"Mismatch for \"{file.DiskPath}\" (exists: {fi.Exists}, expected size: {file.Size})");
                return false;
            }
        }

        return true;
    }

    private record FileToVerify(FileWithSize File, PieceHash[] Pieces, Func<ReadOnlyMemory<byte>, byte[], bool> FxHashMatches);

    private static async Task<bool> VerifyRandomPieces(IReadOnlyList<FileToVerify> Files, int BufferSize, ILogger Logger)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        var verified = 0;

        try {
            foreach (var file in Files) {
                var pieceHashes = file.Pieces;
                if (pieceHashes.Length == 0)
                    continue;

                var scaled = 50.0 * Math.Sqrt(file.File.Size / (1024d * 1024 * 1024));
                var toCheck = Math.Min(Math.Clamp((int)Math.Ceiling(scaled), 1, 200), pieceHashes.Length);

                try {
                    using var handle = File.OpenHandle(file.File.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);

                    for (var i = 0;i < toCheck;i++) {
                        var piece = pieceHashes[Random.Shared.Next(pieceHashes.Length)];

                        if (!await VerifyPiece(handle, piece.Start, (int)(piece.End - piece.Start), piece.Hash, buf, file.FxHashMatches)) {
                            Logger.LogWarning($"Fast resume: piece hash mismatch in \"{file.File.DiskPath}\" at offset {piece.Start}");
                            return false;
                        }

                        verified++;
                    }
                } catch (Exception ex) {
                    Logger.LogWarning(ex, $"Fast resume: failed to read piece in \"{file.File.DiskPath}\"");
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
