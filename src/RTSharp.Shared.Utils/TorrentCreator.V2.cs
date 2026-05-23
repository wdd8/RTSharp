using BencodeNET.Objects;

using System.Security.Cryptography;

namespace RTSharp.Shared.Utils
{
    public partial class TorrentCreator
    {
        private static readonly Lock V2ZeroHashesLock = new();
        private static readonly List<byte[]> V2ZeroHashes = [new byte[32]];

        private static void AddV2Info(BDictionary TorrentDict, BDictionary Info, List<TorrentFileEntry> AllFiles, Dictionary<TorrentFileEntry, V2FileHash> V2Hashes)
        {
            Info.Add("meta version", 2);

            var fileTree = new BDictionary();
            foreach (var entry in AllFiles) {
                V2Hashes.TryGetValue(entry, out var hash);
                AddFileToFileTree(fileTree, entry.RelativePath, entry.Info.Length, hash?.Root);
            }

            Info["file tree"] = fileTree;

            var pieceLayers = new BDictionary();
            foreach (var hash in V2Hashes.Values)
                if (hash.PieceLayer != null)
                    pieceLayers[hash.Root] = hash.PieceLayer;

            TorrentDict["piece layers"] = pieceLayers;
        }

        private static void AddFileToFileTree(BDictionary Root, string[] Path, long Length, BString? PiecesRoot)
        {
            var current = Root;
            for (var x = 0;x < Path.Length;x++) {
                string segment = Path[x];
                var next = current[segment] as BDictionary;
                if (next == null) {
                    next = new BDictionary();
                    current[segment] = next;
                }

                if (x == Path.Length - 1) {
                    var fileInfo = new BDictionary {
                        { "length", Length }
                    };
                    if (PiecesRoot != null)
                        fileInfo["pieces root"] = PiecesRoot;

                    next[""] = fileInfo;
                } else {
                    current = next;
                }
            }
        }

        private static V2FileHash BuildV2FileHash(ReadOnlySpan<byte> LeafHashes, long FileLength, int PieceLength)
        {
            int pieceLayerDepth = GetPieceLayerDepth(PieceLength);
            long pieceLayerHashCount = (FileLength - 1) / PieceLength + 1;
            BString? pieceLayer = null;
            int hashCount = LeafHashes.Length / 32;
            int depth = 0;

            while (true) {
                if (depth == pieceLayerDepth && FileLength > PieceLength) {
                    int pieceLayerByteLength = GetHashByteLength(pieceLayerHashCount);
                    var pieceLayerBytes = new byte[pieceLayerByteLength];
                    LeafHashes[..pieceLayerByteLength].CopyTo(pieceLayerBytes);
                    pieceLayer = new BString(pieceLayerBytes);
                }

                if (hashCount == 1)
                    return new V2FileHash(new BString(LeafHashes[..32].ToArray()), pieceLayer);

                int nextHashCount = (hashCount + 1) / 2;
                var nextLevel = new byte[GetHashByteLength(nextHashCount)];
                var zeroHash = hashCount % 2 == 0 ? [] : GetZeroHash(depth);

                for (var x = 0;x < hashCount;x += 2) {
                    var left = LeafHashes.Slice(x * 32, 32);
                    var right = x + 1 < hashCount ? LeafHashes.Slice((x + 1) * 32, 32) : zeroHash;
                    HashPair(left, right, nextLevel.AsSpan((x / 2) * 32, 32));
                }

                LeafHashes = nextLevel;
                hashCount = nextHashCount;
                depth++;
            }
        }

        private static void HashPair(ReadOnlySpan<byte> Left, ReadOnlySpan<byte> Right, Span<byte> Destination)
        {
            Span<byte> buffer = stackalloc byte[Left.Length + Right.Length];
            Left.CopyTo(buffer);
            Right.CopyTo(buffer[Left.Length..]);
            SHA256.HashData(buffer, Destination);
        }

        private static ReadOnlySpan<byte> GetZeroHash(int Depth)
        {
            lock (V2ZeroHashesLock) {
                while (V2ZeroHashes.Count <= Depth) {
                    var previous = V2ZeroHashes[^1];
                    var next = new byte[32];
                    HashPair(previous, previous, next);
                    V2ZeroHashes.Add(next);
                }

                return V2ZeroHashes[Depth];
            }
        }

        private static int GetHashByteLength(long HashCount)
        {
            if (HashCount > int.MaxValue / 32)
                throw new InvalidOperationException("Torrent metadata is too large to build in memory.");

            return (int)HashCount * 32;
        }

        private static int GetPieceLayerDepth(int PieceLength)
        {
            int leavesPerPiece = PieceLength / MerkleBlockSize;
            int depth = 0;
            while (leavesPerPiece > 1) {
                leavesPerPiece >>= 1;
                depth++;
            }

            return depth;
        }

        private class V2FileHasher(TorrentCreationProgress TorrentCreationProgress, int PieceLength)
        {
            private byte[]? LeafBuffer;
            private readonly MemoryStream LeafHashes = new();
            private long FileLength;
            private int LeafBufferOffset;

            public void Reset(long FileLength)
            {
                this.FileLength = FileLength;
                LeafBufferOffset = 0;
                LeafHashes.SetLength(0);

                long leafHashCount = FileLength == 0 ? 0 : (FileLength - 1) / MerkleBlockSize + 1;
                int capacity = GetHashByteLength(leafHashCount);
                if (LeafHashes.Capacity < capacity)
                    LeafHashes.Capacity = capacity;
            }

            public void Add(Span<byte> In)
            {
                if (LeafBufferOffset > 0) {
                    var leafBuffer = LeafBuffer!;
                    int bytesToCopy = Math.Min(MerkleBlockSize - LeafBufferOffset, In.Length);
                    In[..bytesToCopy].CopyTo(leafBuffer.AsSpan()[LeafBufferOffset..]);
                    In = In[bytesToCopy..];
                    LeafBufferOffset += bytesToCopy;

                    if (LeafBufferOffset < MerkleBlockSize)
                        return;

                    AddLeafHash(leafBuffer);
                }

                while (In.Length >= MerkleBlockSize) {
                    AddLeafHash(In[..MerkleBlockSize]);
                    In = In[MerkleBlockSize..];
                }

                if (In.Length > 0) {
                    LeafBuffer ??= new byte[MerkleBlockSize];
                    In.CopyTo(LeafBuffer);
                    LeafBufferOffset = In.Length;
                }
            }

            public V2FileHash? BuildHash()
            {
                if (FileLength == 0)
                    return null;

                if (LeafBufferOffset > 0)
                    AddLeafHash(LeafBuffer.AsSpan()[..LeafBufferOffset]);

                if (!LeafHashes.TryGetBuffer(out var leafHashBuffer) || leafHashBuffer.Array == null)
                    throw new InvalidOperationException("Unable to access V2 leaf hash buffer.");

                return BuildV2FileHash(new ReadOnlySpan<byte>(leafHashBuffer.Array, leafHashBuffer.Offset, leafHashBuffer.Count), FileLength, PieceLength);
            }

            private void AddLeafHash(ReadOnlySpan<byte> In)
            {
                Span<byte> hash = stackalloc byte[32];
                SHA256.HashData(In, hash);
                TorrentCreationProgress.ReportHashProgress(hash);
                LeafHashes.Write(hash);
                LeafBufferOffset = 0;
            }
        }
    }
}
