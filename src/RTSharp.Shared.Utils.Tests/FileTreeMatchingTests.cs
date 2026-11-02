using BencodeNET.Objects;

using Xunit;

namespace RTSharp.Shared.Utils.Tests;

public class FileTreeMatchingTests
{
    private const string BASE_DIRECTORY = "base";

    private static BDictionary SingleFile(string Name, long Length) => new() {
        { "name", new BString(Name) },
        { "length", new BNumber(Length) }
    };

    private static BDictionary MultiFile(string Name, params (string[] Path, long Length)[] Files)
    {
        var files = new BList();
        foreach (var (path, length) in Files) {
            files.Add(new BDictionary {
                { "path", new BList(path) },
                { "length", new BNumber(length) }
            });
        }

        return new BDictionary {
            { "name", new BString(Name) },
            { "files", files }
        };
    }

    private static string DiskPath(params string[] Components) => Path.Combine([ BASE_DIRECTORY, .. Components ]);

    [Fact]
    public void Strict_PaddingFilesAreNotPartOfTheFileTree()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100), ([ "sub", "b.nfo" ], 5));

        var candidate = MultiFile("Release", ([ "a.mkv" ], 100), ([ "sub", "b.nfo" ], 5));
        candidate.Get<BList>("files")!.Insert(1, new BDictionary {
            { "attr", new BString("p") },
            { "path", new BList(new[] { ".pad", "28" }) },
            { "length", new BNumber(28) }
        });

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.True(match.Strict);
        Assert.Equal(28, BencodeUtils.GetPaddingSize(candidate));
        Assert.Equal(DiskPath("Release", "sub", "b.nfo"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void UnsafePaddingFilePathIsNotSafe()
    {
        var torrent = MultiFile("Release", ([ "a.mkv" ], 100));
        torrent.Get<BList>("files")!.Add(new BDictionary {
            { "attr", new BString("p") },
            { "path", new BList(new[] { "..", "28" }) },
            { "length", new BNumber(28) }
        });

        Assert.False(BencodeUtils.HasSafePaths(torrent));
    }

    [Fact]
    public void Strict_IdenticalMultiFile()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100), ([ "sub", "b.nfo" ], 5));

        // Listed in a different order than the source
        var candidate = MultiFile("Release", ([ "sub", "b.nfo" ], 5), ([ "a.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.True(match.Strict);
        Assert.Equal(DiskPath("Release", "sub", "b.nfo"), match.DiskFiles[0].Path);
        Assert.Equal(DiskPath("Release", "a.mkv"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void Strict_IdenticalSingleFile()
    {
        var match = BencodeUtils.MatchFileTrees(SingleFile("a.mkv", 100), [ (SingleFile("a.mkv", 100), BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.True(match.Strict);
        Assert.Equal(DiskPath("a.mkv"), match.DiskFiles[0].Path);
    }

    [Fact]
    public void NotStrict_RenamedRoot_KeepsSourcePaths()
    {
        var source = MultiFile("Release.Name", ([ "a.mkv" ], 100));
        var candidate = MultiFile("Release Name", ([ "a.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
        Assert.Equal(DiskPath("Release.Name", "a.mkv"), match.DiskFiles[0].Path);
    }

    [Fact]
    public void NotStrict_SingleFileAgainstFolder()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100));
        var candidate = SingleFile("Release.mkv", 100);

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
        Assert.Equal(DiskPath("Release", "a.mkv"), match.DiskFiles[0].Path);
    }

    [Fact]
    public void NotStrict_SameFileNameDifferentLayoutSameRootName()
    {
        var source = SingleFile("Release", 100);
        var candidate = MultiFile("Release", ([ "Release" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
    }

    [Fact]
    public void NotStrict_RenamedFiles_PairedByUniqueSize()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100), ([ "b.nfo" ], 5));
        var candidate = MultiFile("Release", ([ "renamed.nfo" ], 5), ([ "renamed.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
        Assert.Equal(DiskPath("Release", "b.nfo"), match.DiskFiles[0].Path);
        Assert.Equal(DiskPath("Release", "a.mkv"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void NotStrict_OneRenamedFile()
    {
        var source = MultiFile("Name.Name.Name", ([ "a b.txt" ], 100), ([ "a.c.txt" ], 5));
        var candidate = MultiFile("Name.Name.Name", ([ "a.b.txt" ], 100), ([ "a.c.txt" ], 5));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
        Assert.Equal(DiskPath("Name.Name.Name", "a b.txt"), match.DiskFiles[0].Path);
        Assert.Equal(DiskPath("Name.Name.Name", "a.c.txt"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void Rejected_ContentsSwappedBetweenPaths()
    {
        var source = MultiFile("Name.Name.Name", ([ "a.txt" ], 100), ([ "b.txt" ], 200));
        var candidate = MultiFile("Name.Name.Name", ([ "a.txt" ], 200), ([ "b.txt" ], 100));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]));
    }

    [Fact]
    public void Rejected_ContentsSwappedBetweenPaths_RenamedRoot()
    {
        var source = MultiFile("Name.Name.Name", ([ "a.txt" ], 100), ([ "b.txt" ], 200));
        var candidate = MultiFile("Name Name Name", ([ "a.txt" ], 200), ([ "b.txt" ], 100));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]));
    }

    [Fact]
    public void SharedSize_PairedByPathWhenNamesMatch()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100), ([ "b.mkv" ], 100));
        var candidate = MultiFile("Other", ([ "b.mkv" ], 100), ([ "a.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]);

        Assert.NotNull(match);
        Assert.Equal(DiskPath("Release", "b.mkv"), match.DiskFiles[0].Path);
        Assert.Equal(DiskPath("Release", "a.mkv"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void Rejected_AmbiguousSizes()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100), ([ "b.mkv" ], 100));
        var candidate = MultiFile("Release", ([ "c.mkv" ], 100), ([ "d.mkv" ], 100));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]));
    }

    [Fact]
    public void Rejected_DifferentSizes()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100));
        var candidate = MultiFile("Release", ([ "a.mkv" ], 101));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]));
    }

    [Fact]
    public void Rejected_DifferentFileCount()
    {
        var source = MultiFile("Release", ([ "a.mkv" ], 100));
        var candidate = MultiFile("Release", ([ "a.mkv" ], 100), ([ "a.nfo" ], 5));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [ (source, BASE_DIRECTORY) ]));
    }

    [Fact]
    public void NotStrict_SeasonPackAgainstSingleFileEpisodes()
    {
        var candidate = MultiFile("Show.S01", ([ "Show.S01E02.mkv" ], 200), ([ "Show.S01E01.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [
            (SingleFile("Show.S01E01.mkv", 100), "first"),
            (SingleFile("Show.S01E02.mkv", 200), "second")
        ]);

        Assert.NotNull(match);
        Assert.False(match.Strict);
        Assert.Equal(Path.Combine("second", "Show.S01E02.mkv"), match.DiskFiles[0].Path);
        Assert.Equal(Path.Combine("first", "Show.S01E01.mkv"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void NotStrict_SeasonPackWithRenamedFiles_PairedByUniqueSize()
    {
        var candidate = MultiFile("Show.S01", ([ "e2.mkv" ], 200), ([ "e1.mkv" ], 100));

        var match = BencodeUtils.MatchFileTrees(candidate, [
            (SingleFile("Show.S01E01.mkv", 100), "first"),
            (SingleFile("Show.S01E02.mkv", 200), "second")
        ]);

        Assert.NotNull(match);
        Assert.Equal(Path.Combine("second", "Show.S01E02.mkv"), match.DiskFiles[0].Path);
        Assert.Equal(Path.Combine("first", "Show.S01E01.mkv"), match.DiskFiles[1].Path);
    }

    [Fact]
    public void Rejected_SeasonPackWithMoreEpisodesThanOnDisk()
    {
        var candidate = MultiFile("Show.S01", ([ "Show.S01E01.mkv" ], 100), ([ "Show.S01E02.mkv" ], 200), ([ "Show.S01E03.mkv" ], 300));

        Assert.Null(BencodeUtils.MatchFileTrees(candidate, [
            (SingleFile("Show.S01E01.mkv", 100), "first"),
            (SingleFile("Show.S01E02.mkv", 200), "second")
        ]));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("/etc")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void HasSafePaths_RejectsUnsafeComponents(string Component)
    {
        Assert.False(BencodeUtils.HasSafePaths(MultiFile("Release", ([ "sub", Component, "a.mkv" ], 100))));
        Assert.False(BencodeUtils.HasSafePaths(MultiFile(Component, ([ "a.mkv" ], 100))));
        Assert.False(BencodeUtils.HasSafePaths(SingleFile(Component, 100)));
    }

    [Fact]
    public void HasSafePaths_AcceptsOrdinaryNames()
    {
        Assert.True(BencodeUtils.HasSafePaths(MultiFile("Release..Name", ([ "sub", "..a.mkv" ], 100))));
        Assert.True(BencodeUtils.HasSafePaths(SingleFile("Release.mkv", 100)));
    }
}
