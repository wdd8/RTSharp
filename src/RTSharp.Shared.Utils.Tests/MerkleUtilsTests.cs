using System.Security.Cryptography;

using Xunit;

namespace RTSharp.Shared.Utils.Tests;

public class MerkleUtilsTests
{
    /// <summary>
    /// Independent BEP 52 merkle root, formulated differently to <see cref="MerkleUtils"/>: it pads the
    /// leaf layer out to a full power of two with zero leaves and then reduces the complete tree,
    /// rather than padding with a precomputed zero hash at each level.
    /// </summary>
    private static byte[] ReferenceBlockMerkleRoot(byte[] Data, int Height)
    {
        var leaves = new List<byte[]>();
        for (var x = 0;x < Data.Length;x += MerkleUtils.BLOCK_SIZE)
            leaves.Add(SHA256.HashData(Data.AsSpan(x, Math.Min(MerkleUtils.BLOCK_SIZE, Data.Length - x)).ToArray()));

        while (leaves.Count < 1 << Height)
            leaves.Add(new byte[32]);

        while (leaves.Count > 1) {
            var next = new List<byte[]>(leaves.Count / 2);
            for (var x = 0;x < leaves.Count;x += 2)
                next.Add(SHA256.HashData([.. leaves[x], .. leaves[x + 1]]));

            leaves = next;
        }

        return leaves[0];
    }

    public static TheoryData<int, int> BlockMerkleRootCases => new() {
        // Exactly one leaf, no reduction at all
        { 1, 0 },
        { MerkleUtils.BLOCK_SIZE, 0 },
        // Partial trailing leaf, padded up
        { MerkleUtils.BLOCK_SIZE + 1, 1 },
        { MerkleUtils.BLOCK_SIZE * 2, 1 },
        { MerkleUtils.BLOCK_SIZE * 3, 2 },
        { MerkleUtils.BLOCK_SIZE * 4, 2 },
        { MerkleUtils.BLOCK_SIZE * 4 + 7, 3 },
        // A short final piece of a large file: far fewer leaves than the level count implies, which is where reduction has to keep pairing a lone hash with zero hashes
        { MerkleUtils.BLOCK_SIZE + 5, 4 },
        { 3, 5 },
    };

    [Theory]
    [MemberData(nameof(BlockMerkleRootCases))]
    public void ComputeBlockMerkleRoot_MatchesIndependentImplementation(int Length, int Height)
    {
        var data = new byte[Length];
        Random.Shared.NextBytes(data);

        Span<byte> root = stackalloc byte[32];
        MerkleUtils.ComputeBlockMerkleRoot(data, Height, root);

        Assert.Equal(ReferenceBlockMerkleRoot(data, Height), root.ToArray());
    }

    [Fact]
    public void GetZeroHash_ChainsFromThirtyTwoZeroBytes()
    {
        Assert.Equal(new byte[32], MerkleUtils.GetZeroHash(0).ToArray());

        var expected = new byte[32];
        for (var depth = 1;depth < 8;depth++) {
            expected = SHA256.HashData([ ..expected, ..expected ]);
            Assert.Equal(expected, MerkleUtils.GetZeroHash(depth).ToArray());
        }
    }

    [Theory]
    [InlineData(16 * 1024, 0)]
    [InlineData(32 * 1024, 1)]
    [InlineData(64 * 1024, 2)]
    [InlineData(256 * 1024, 4)]
    public void GetPieceLayerDepth_CountsLeavesPerPiece(int PieceLength, int Expected)
    {
        Assert.Equal(Expected, MerkleUtils.GetPieceLayerDepth(PieceLength));
    }
}
