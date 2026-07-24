using BencodeNET.Objects;
using BencodeNET.Torrents;

using Microsoft.Extensions.Logging;

using System.Numerics;

namespace RTSharp.Shared.Utils;

public static partial class BencodeUtils
{
    /// <summary>
    /// Piece hashes of a single BitTorrent v2 file.
    /// </summary>
    /// <param name="MerkleHeight">Height of the merkle subtree from the piece's 16 KiB block leaves up to <see cref="PieceHash.Hash"/>, i.e. how many times those leaves are reduced.</param>
    public record FilePieceHashesV2(TorrentFile File, PieceHash[] PieceHashes, Range PieceRange, int MerkleHeight);

    /// <summary>
    /// A file as described by the torrent's "file tree"
    /// </summary>
    private readonly record struct FileTreeEntry(TorrentFile File, BString? PiecesRoot);

    /// <summary>
    /// For each file in a BitTorrent v2 torrent, computes its piece hashes with byte offsets relative
    /// to the start of the file.
    /// </summary>
    /// <param name="BTorrent">.torrent bencode root</param>
    /// <exception cref="InvalidDataException">Torrent metadata is malformed or unsupported</exception>
    public static FilePieceHashesV2[] AssignHashesToPiecesV2(BDictionary BTorrent)
    {
        var info = BTorrent.Get<BDictionary>(TorrentFields.Info)
            ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        var pieceLength = GetPieceLength(info);

        if (pieceLength > Int32.MaxValue || pieceLength < MerkleUtils.BLOCK_SIZE)
            throw new InvalidDataException($"Unsupported v2 piece length {pieceLength}");

        var fileTree = info.Get<BDictionary>("file tree")
            ?? throw new InvalidDataException("Torrent has no \"file tree\"");

        var pieceLayers = BTorrent.Get<BDictionary>("piece layers") ?? new BDictionary();
        var pieceLayerDepth = MerkleUtils.GetPieceLayerDepth((int)pieceLength);

        var entries = new List<FileTreeEntry>();
        WalkFileTree(fileTree, [], entries);

        var result = new FilePieceHashesV2[entries.Count];
        var globalPiece = 0;
        Span<byte> root = stackalloc byte[32];

        for (var x = 0;x < entries.Count;x++) {
            var (file, piecesRoot) = entries[x];
            var length = file.FileSize;
            var fullPath = String.Join('/', file.PathComponents);

            if (length == 0) {
                result[x] = new FilePieceHashesV2(file, [], globalPiece..globalPiece, 0);
                continue;
            }

            if (piecesRoot?.Length != 32)
                throw new InvalidDataException($"File \"{fullPath}\" has no valid \"pieces root\"");

            PieceHash[] pieceHashes;
            int merkleHeight;

            if (length <= pieceLength) {
                // The whole file is one piece, so its merkle root is the pieces root itself
                var blockCount = (length - 1) / MerkleUtils.BLOCK_SIZE + 1;
                merkleHeight = blockCount == 1 ? 0 : BitOperations.Log2((ulong)(blockCount - 1)) + 1;
                pieceHashes = [ new PieceHash(0, (ulong)length, piecesRoot.Value.ToArray()) ];
            } else {
                var pieceCount = (int)((length - 1) / pieceLength + 1);

                var pieceLayer = pieceLayers.Get<BString>(piecesRoot)
                    ?? throw new InvalidDataException($"File \"{fullPath}\" has no \"piece layers\" entry");

                if (pieceLayer.Length != pieceCount * 32)
                    throw new InvalidDataException($"File \"{fullPath}\" has a {pieceLayer.Length} byte piece layer, expected {pieceCount * 32}");

                var pieceLayerBytes = pieceLayer.Value.Span;

                MerkleUtils.HashTree(pieceLayerBytes, BitOperations.Log2((uint)(pieceCount - 1)) + 1, pieceLayerDepth, root);
                if (!root.SequenceEqual(piecesRoot.Value.Span))
                    throw new InvalidDataException($"File \"{fullPath}\" piece layer does not hash to its pieces root");

                merkleHeight = pieceLayerDepth;
                pieceHashes = new PieceHash[pieceCount];
                for (var i = 0;i < pieceCount;i++) {
                    pieceHashes[i] = new PieceHash(
                        Start: (ulong)((long)i * pieceLength),
                        End: (ulong)Math.Min((long)(i + 1) * pieceLength, length),
                        Hash: pieceLayerBytes.Slice(i * 32, 32).ToArray()
                    );
                }
            }

            result[x] = new FilePieceHashesV2(file, pieceHashes, globalPiece..(globalPiece + pieceHashes.Length), merkleHeight);
            globalPiece += pieceHashes.Length;
        }

        return result;
    }

    private static void WalkFileTree(BDictionary Node, List<string> Components, List<FileTreeEntry> Result)
    {
        // A node holding the empty key is a file, everything else is a path component
        var fileNode = Node.Get<BDictionary>("");
        if (fileNode != null) {
            var length = fileNode.Get<BNumber>("length")?.Value
                ?? throw new InvalidDataException($"File \"{String.Join('/', Components)}\" has no \"length\"");

            Result.Add(new FileTreeEntry(new TorrentFile([.. Components], length), fileNode.Get<BString>("pieces root")));
            return;
        }

        foreach (var (key, value) in Node) {
            if (value is not BDictionary child)
                throw new InvalidDataException("Malformed \"file tree\" entry");

            Components.Add(key.ToString());
            WalkFileTree(child, Components, Result);
            Components.RemoveAt(Components.Count - 1);
        }
    }

    /// <summary>
    /// <see cref="FastVerifyFilesOnDisk"/> for v2 only torrents, verifying pieces against the
    /// per file merkle trees instead of the v1 piece hash string
    /// </summary>
    private static async Task<bool> FastVerifyFilesOnDiskV2(BDictionary BTorrent, string BaseDirectory, ILogger Logger)
    {
        FilePieceHashesV2[] fileHashes;
        int pieceLen;
        TorrentFile[] files;

        try {
            var info = BTorrent.Get<BDictionary>(TorrentFields.Info)!;
            pieceLen = (int)GetPieceLength(info);

            fileHashes = AssignHashesToPiecesV2(BTorrent);
            // Same "file tree" walk order as AssignHashesToPiecesV2, so files[x] pairs with fileHashes[x]
            files = GetFilesOnDisk(info, BaseDirectory);
        } catch (Exception ex) {
            Logger.LogWarning(ex, "Fast verify unavailable: could not read v2 torrent metadata");
            return false;
        }

        if (fileHashes.Length == 0) {
            Logger.LogWarning("Fast verify unavailable: torrent has no files");
            return false;
        }

        if (!CheckFilesExistWithSize(files, Logger))
            return false;

        var toVerify = new List<FileToVerify>(files.Length);
        for (var x = 0;x < files.Length;x++) {
            var height = fileHashes[x].MerkleHeight;
            toVerify.Add(new FileToVerify(files[x], fileHashes[x].PieceHashes, (Data, Expected) => {
                Span<byte> root = stackalloc byte[32];
                MerkleUtils.ComputeBlockMerkleRoot(Data.Span, height, root);
                return root.SequenceEqual(Expected);
            }));
        }

        return await VerifyRandomPieces(toVerify, pieceLen, Logger);
    }
}
