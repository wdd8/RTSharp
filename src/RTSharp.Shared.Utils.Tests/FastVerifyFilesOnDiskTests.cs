using BencodeNET.Objects;
using BencodeNET.Parsing;
using BencodeNET.Torrents;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace RTSharp.Shared.Utils.Tests;

public class FastVerifyFilesOnDiskTests : IDisposable
{
    private const int PIECE_LENGTH = 64 * 1024;

    private readonly string Root;
    private readonly string ContentDirectory;

    public FastVerifyFilesOnDiskTests()
    {
        Root = Path.Combine(Path.GetTempPath(), $"rtsharp-fastverify-{Guid.NewGuid():N}");
        ContentDirectory = Path.Combine(Root, "content");
        Directory.CreateDirectory(Path.Combine(ContentDirectory, "sub"));

        WriteRandom(Path.Combine(ContentDirectory, "empty.bin"), 0);

        // below one 16 KiB block
        WriteRandom(Path.Combine(ContentDirectory, "tiny.bin"), 5000);

        // multi block, but one piece
        WriteRandom(Path.Combine(ContentDirectory, "small.bin"), 40000);

        // multi piece, short tail
        WriteRandom(Path.Combine(ContentDirectory, "sub", "large.bin"), PIECE_LENGTH * 3 + 1234);
    }

    public void Dispose() => Directory.Delete(Root, true);

    private static void WriteRandom(string Path, int Length)
    {
        var data = new byte[Length];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(Path, data);
    }

    private static async Task<(Torrent Torrent, BDictionary BTorrent)> CreateTorrent(string Path, TorrentCreator.VERSION Version)
    {
        var bytes = await TorrentCreator.Create(Path, null, null, null, null, false, false, false, Version, PIECE_LENGTH);

        using var ms = new MemoryStream(bytes);
        var btorrent = new TorrentParser().Parse(ms);
        ms.Position = 0;

        return (btorrent, new BencodeParser().Parse<BDictionary>(ms));
    }

    private static void CorruptEveryPiece(string Path)
    {
        var data = File.ReadAllBytes(Path);
        for (var x = 0;x < data.Length;x += PIECE_LENGTH)
            data[x] ^= 0xFF;

        File.WriteAllBytes(Path, data);
    }

    [Fact]
    public async Task AssignHashesToPiecesV2_AssignsPiecesPerFile()
    {
        var (_, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V2);

        var fileHashes = BencodeUtils.AssignHashesToPiecesV2(bTorrent);
        var byPath = fileHashes.ToDictionary(x => x.FullPath);

        Assert.Equal(4, fileHashes.Length);

        // Zero length files carry no pieces root and no pieces
        var empty = byPath["empty.bin"];
        Assert.Empty(empty.PieceHashes);
        Assert.Equal(0, empty.FileSize);

        // Below one block: the pieces root is the lone leaf hash, so nothing to reduce
        var tiny = byPath["tiny.bin"];
        var tinyPiece = Assert.Single(tiny.PieceHashes);
        Assert.Equal(0, tiny.MerkleHeight);
        Assert.Equal((0ul, 5000ul), (tinyPiece.Start, tinyPiece.End));

        // Three 16 KiB blocks in a single piece, reduced to the pieces root
        var small = byPath["small.bin"];
        var smallPiece = Assert.Single(small.PieceHashes);
        Assert.Equal(2, small.MerkleHeight);
        Assert.Equal((0ul, 40000ul), (smallPiece.Start, smallPiece.End));

        // Four pieces from the piece layer, the last one short, each rooted at the piece layer depth
        var large = byPath["sub/large.bin"];
        Assert.Equal(4, large.PieceHashes.Length);
        Assert.Equal(MerkleUtils.GetPieceLayerDepth(PIECE_LENGTH), large.MerkleHeight);
        Assert.Equal((0ul, (ulong)PIECE_LENGTH), (large.PieceHashes[0].Start, large.PieceHashes[0].End));
        Assert.Equal(((ulong)PIECE_LENGTH * 3, (ulong)(PIECE_LENGTH * 3 + 1234)), (large.PieceHashes[^1].Start, large.PieceHashes[^1].End));

        // Pieces never span files, so the global ranges are just the per file counts laid end to end
        Assert.Equal(0, fileHashes[0].PieceRange.Start.Value);
        for (var x = 1;x < fileHashes.Length;x++)
            Assert.Equal(fileHashes[x - 1].PieceRange.End.Value, fileHashes[x].PieceRange.Start.Value);
    }

    [Fact]
    public async Task V2Only_VerifiesIntactData()
    {
        var (torrent, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V2);

        Assert.Empty(torrent.Pieces);
        Assert.True(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));
    }

    [Fact]
    public async Task V2Only_SingleFileTorrent_VerifiesIntactData()
    {
        var path = Path.Combine(ContentDirectory, "sub", "large.bin");
        var (btorrent, torrentData) = await CreateTorrent(path, TorrentCreator.VERSION.V2);

        Assert.True(await BencodeUtils.FastVerifyFilesOnDisk(btorrent, Path.GetDirectoryName(path)!, NullLogger.Instance, torrentData));
    }

    [Theory]
    // The piece layer path
    [InlineData("sub/large.bin")]
    // The pieces root path, where the whole file is a single piece
    [InlineData("small.bin")]
    [InlineData("tiny.bin")]
    public async Task V2Only_RejectsCorruptedData(string RelativePath)
    {
        var (torrent, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V2);

        CorruptEveryPiece(Path.Combine([ContentDirectory, .. RelativePath.Split('/')]));

        Assert.False(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));
    }

    [Fact]
    public async Task V2Only_RejectsWrongFileSize()
    {
        var (torrent, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V2);

        var path = Path.Combine(ContentDirectory, "small.bin");
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);

        Assert.False(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));
    }

    [Fact]
    public async Task V2Only_WithoutTorrentData_CannotFastVerify()
    {
        var (torrent, _) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V2);

        Assert.False(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance));
    }

    [Fact]
    public async Task V1Only_StillTakesTheSha1Path()
    {
        var (torrent, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V1);

        Assert.NotEmpty(torrent.Pieces);
        Assert.True(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));

        CorruptEveryPiece(Path.Combine(ContentDirectory, "sub", "large.bin"));
        Assert.False(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));
    }

    [Fact]
    public async Task Hybrid_FastVerifiesViaV2Path_DespiteV1PaddingFiles()
    {
        var (torrent, bTorrent) = await CreateTorrent(ContentDirectory, TorrentCreator.VERSION.V1 | TorrentCreator.VERSION.V2);

        Assert.NotEmpty(torrent.Pieces);
        Assert.Contains(torrent.Files, x => (x.Path ?? x.PathUtf8).Contains(".pad"));

        Assert.True(await BencodeUtils.FastVerifyFilesOnDisk(torrent, Root, NullLogger.Instance, bTorrent));
    }
}
