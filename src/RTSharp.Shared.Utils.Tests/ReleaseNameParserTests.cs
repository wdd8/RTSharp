using System.ComponentModel;

using Xunit;

namespace RTSharp.Shared.Utils.Tests;

public class ReleaseNameParserTests
{
    private static async Task<ReleaseNameParser.ReleaseName> Parse(string Name)
    {
        try {
            return await ReleaseNameParser.Parse(Name);
        } catch (Win32Exception) {
            Assert.Skip("The rls helper wasn't built, which needs Go");
            throw;
        }
    }

    [Fact]
    public async Task ParsesEpisode()
    {
        var release = await Parse("30.Grader.I.Februari.S01E01.SWEDiSH.HDTV.XviD-HDR");

        Assert.Equal("episode", release.Type);
        Assert.Equal("30 Grader I Februari", release.Title);
        Assert.Equal(1, release.Series);
        Assert.Equal(1, release.Episode);
    }

    [Fact]
    public async Task ParsesEpisodeFileName()
    {
        var release = await Parse("The.Show.S02E05.1080p.WEB-DL.x264-GROUP.mkv");

        Assert.Equal("episode", release.Type);
        Assert.Equal("The Show", release.Title);
        Assert.Equal(2, release.Series);
        Assert.Equal(5, release.Episode);
    }

    [Fact]
    public async Task ParsesMovie()
    {
        var release = await Parse("deep.web.2015.1080p.web.x264-group");

        Assert.Equal("movie", release.Type);
        Assert.Equal("deep web", release.Title);
        Assert.Equal(2015, release.Year);
    }

    [Fact]
    public async Task ParsesMusicWithArtist()
    {
        var release = await Parse("The_Velvet_Underground-The_Complete_Matrix_Tapes-Reissue_Limited_Edition_Boxset-8LP-2019-NOiR");

        Assert.Equal("music", release.Type);
        Assert.Equal("The Velvet Underground", release.Artist);
        Assert.Equal("The Complete Matrix Tapes", release.Title);
    }

    [Fact]
    public async Task KeepsNonAsciiNames()
    {
        var release = await Parse("Daleká.cesta.1950.1080p.BluRay.x264-GROUP");

        Assert.Equal("Daleká cesta", release.Title);
    }

    [Fact]
    public async Task NewlineInNameKeepsResponsesPaired()
    {
        Assert.Equal("foo bar", (await Parse("foo\nbar")).Title);
        Assert.Equal("deep web", (await Parse("deep.web.2015.1080p.web.x264-group")).Title);
    }

    [Fact]
    public async Task ConcurrentCallersGetTheirOwnResults()
    {
        var results = await Task.WhenAll(Enumerable.Range(1, 50).Select(x => Parse($"The.Show.S01E{x:00}.1080p.WEB-DL.x264-GROUP")));

        Assert.Equal(Enumerable.Range(1, 50), results.Select(x => x.Episode));
    }
}
