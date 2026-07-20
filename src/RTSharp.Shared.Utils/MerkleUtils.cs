using System.Numerics;
using System.Security.Cryptography;

namespace RTSharp.Shared.Utils
{
    /// <summary>
    /// BitTorrent v2 (BEP 52) merkle tree utils
    /// </summary>
    public static class MerkleUtils
    {
        public const int BLOCK_SIZE = 16 * 1024;
        private const int MAX_TREE_DEPTH = 64;

        private static readonly byte[][] V2ZeroHashes = BuildZeroHashes();

        private static byte[][] BuildZeroHashes()
        {
            var hashes = new byte[MAX_TREE_DEPTH][];
            hashes[0] = new byte[32];
            for (var depth = 1;depth < MAX_TREE_DEPTH;depth++) {
                hashes[depth] = new byte[32];
                HashPair(hashes[depth - 1], hashes[depth - 1], hashes[depth]);
            }

            return hashes;
        }

        public static void HashPair(ReadOnlySpan<byte> Left, ReadOnlySpan<byte> Right, Span<byte> Destination)
        {
            Span<byte> buffer = stackalloc byte[Left.Length + Right.Length];
            Left.CopyTo(buffer);
            Right.CopyTo(buffer[Left.Length..]);
            SHA256.HashData(buffer, Destination);
        }

        /// <summary>
        /// Hash of an all-zero subtree at <paramref name="Depth"/>. Depth 0 is 32 zero bytes.
        /// </summary>
        public static ReadOnlySpan<byte> GetZeroHash(int Depth) => V2ZeroHashes[Depth];

        public static int GetHashByteLength(long HashCount)
        {
            if (HashCount > int.MaxValue / 32)
                throw new InvalidOperationException("Torrent metadata is too large to build in memory.");

            return (int)HashCount * 32;
        }

        /// <summary>
        /// Depth at which merkle tree nodes cover exactly one piece
        /// </summary>
        public static int GetPieceLayerDepth(int PieceLength) => BitOperations.Log2((uint)(PieceLength / BLOCK_SIZE));

        /// <summary>
        /// Reduces 32 byte <paramref name="Hashes"/>, which sit at <paramref name="StartDepth"/>, upward
        /// exactly <paramref name="Levels"/> times, padding odd tails and lone hashes with the zero hash
        /// of the level being reduced.
        /// </summary>
        /// <param name="Hashes">Hashes at <paramref name="StartDepth"/>, concatenated</param>
        /// <param name="Levels">Number of levels to reduce by</param>
        /// <param name="StartDepth">Absolute tree depth of <paramref name="Hashes"/></param>
        /// <param name="Root">Destination for the resulting 32 byte hash</param>
        public static void HashTree(ReadOnlySpan<byte> Hashes, int Levels, int StartDepth, Span<byte> Root)
        {
            int count = Hashes.Length / 32;

            for (var level = 0;level < Levels;level++) {
                int depth = StartDepth + level;
                int nextCount = (count + 1) / 2;
                var next = new byte[GetHashByteLength(nextCount)];
                var zeroHash = GetZeroHash(depth);

                for (var x = 0;x < count;x += 2) {
                    var left = Hashes.Slice(x * 32, 32);
                    var right = x + 1 < count ? Hashes.Slice((x + 1) * 32, 32) : zeroHash;
                    HashPair(left, right, next.AsSpan((x / 2) * 32, 32));
                }

                Hashes = next;
                count = nextCount;
            }

            Hashes[..32].CopyTo(Root);
        }

        /// <summary>
        /// SHA-256 hashes each <see cref="BLOCK_SIZE"/> block of <paramref name="Data"/> and reduces the
        /// resulting leaves up to the root of a subtree of the given <paramref name="Height"/>, giving the
        /// merkle root of a v2 piece.
        /// </summary>
        public static void ComputeBlockMerkleRoot(ReadOnlySpan<byte> Data, int Height, Span<byte> Root)
        {
            var blockCount = (Data.Length - 1) / BLOCK_SIZE + 1;
            var leafHashes = new byte[GetHashByteLength(blockCount)];

            for (var x = 0;x < blockCount;x++) {
                var block = Data.Slice(x * BLOCK_SIZE, Math.Min(BLOCK_SIZE, Data.Length - (x * BLOCK_SIZE)));
                SHA256.HashData(block, leafHashes.AsSpan(x * 32, 32));
            }

            // From the block leaves (depth 0), climbing Height levels reaches the subtree root
            HashTree(leafHashes, Height, 0, Root);
        }
    }
}
