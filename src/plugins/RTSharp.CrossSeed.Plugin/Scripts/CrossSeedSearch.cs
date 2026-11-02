#pragma usings
#pragma contract CrossSeedSearchMessages

using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using BencodeNET.Torrents;
using Microsoft.Extensions.Logging;

namespace RTSharp.Scripts.CrossSeedSearch;

public class Main(ILogger<Main> Logger) : IScript
{
    private record Release(string Title, string? Link, long? Size, string? InfoHash);

    private record IndexerResult(string Indexer, string? Error, Release[] Releases);

    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;

        var input = await Session.Rx<CrossSeedSearchInput>(CancellationToken);

        var matches = new List<CrossSeedMatch>();
        var skipped = new List<CrossSeedSkip>();
        var plannedLinks = new Dictionary<string, string>();

        static string describe(Exception Ex) => Ex is HttpRequestException { StatusCode: not null } failure ? $"HTTP {(int)failure.StatusCode}" : Ex.GetType().Name;

        var sources = input.Sources.Select(source => {
            using var mem = new MemoryStream(source.Torrent);
            var info = new BencodeParser().Parse<BDictionary>(mem).Get<BDictionary>(TorrentFields.Info) ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

            return (Info: info, source.BaseDirectory);
        }).ToArray();

        var totalSize = sources.Sum(((BDictionary Info, string BaseDirectory) x) => BencodeUtils.GetFiles(x.Info).Files.Sum((BencodeUtils.TorrentFile y) => y.FileSize));
        var paddedSize = totalSize + sources.Sum(((BDictionary Info, string BaseDirectory) x) => BencodeUtils.GetPaddingSize(x.Info));
        var sourceName = BencodeUtils.GetName(sources[0].Info);

        // Trackers differ on whether the size they report includes padding
        bool isOfSourceSize(Release Release) => Release.Size == totalSize || Release.Size == paddedSize;

        /*
         * Narrowest first. Trackers differ on whether they find a title with the year or episode in it,
         * and a broad search is cheap because only exact size matches get downloaded.
         */
        string[] queries;
        try {
            var release = await ReleaseNameParser.Parse(sourceName, CancellationToken);
            var title = String.IsNullOrEmpty(release.Artist) ? release.Title : $"{release.Artist} {release.Title}";

            // Several sources are the episodes of one season, so what's wanted is the season
            var qualifier = release switch {
                { Type: "episode" or "series", Series: > 0, Episode: > 0 } when sources.Length == 1 => $"S{release.Series:00}E{release.Episode:00}",
                { Type: "episode" or "series", Series: > 0 } => $"S{release.Series:00}",
                { Type: "movie", Year: > 0 } => release.Year.ToString(CultureInfo.InvariantCulture),
                _ => null
            };

            queries = String.IsNullOrWhiteSpace(title) ? [ sourceName ] : qualifier == null ? [ title ] : [ $"{title} {qualifier}", title ];
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Logger.LogError(ex, "Parsing the release name with the rls helper failed");
            skipped.Add(new CrossSeedSkip("", "", "Release name parser is unavailable, searched by full name"));
            queries = [ sourceName ];
        }

        var existing = new HashSet<string>(input.ExistingInfoHashes, StringComparer.OrdinalIgnoreCase);

        using var http = new HttpClient {
            Timeout = TimeSpan.FromSeconds(60),
            MaxResponseContentBufferSize = 64 * 1024 * 1024
        };

        async Task<(string? Error, Release[] Releases)> search(Uri Url, string Query)
        {
            var separator = Url.Query.Length == 0 ? '?' : '&';
            var xml = await http.GetStringAsync($"{Url.AbsoluteUri}{separator}t=search&limit=100&q={Uri.EscapeDataString(Query)}", CancellationToken);
            const string notAFeed = "Not a Torznab feed, the URL should end in /api";

            XElement? root;
            try {
                root = XDocument.Parse(xml).Root;
            } catch (XmlException) {
                return (notAFeed, []);
            }

            if (root?.Name.LocalName == "error")
                return ($"Indexer error {root.Attribute("code")?.Value}", []);

            var channel = root?.Element("channel");
            if (channel == null)
                return (notAFeed, []);

            var releases = channel.Elements("item").Select(item => {
                var enclosure = item.Element("enclosure");

                string? attr(string Name) => item.Elements()
                    .FirstOrDefault(x => x.Name.LocalName == "attr" && x.Attribute("name")?.Value == Name)?
                    .Attribute("value")?.Value;

                var size = attr("size") ?? item.Element("size")?.Value ?? enclosure?.Attribute("length")?.Value;

                return new Release(
                    item.Element("title")?.Value ?? "",
                    enclosure?.Attribute("url")?.Value ?? item.Element("link")?.Value,
                    long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                    attr("infohash"));
            }).ToArray();

            return (null, releases);
        }

        // Returns why the release can't be cross seeded, or null if it was added to matches
        async Task<string?> evaluate(string Indexer, Release Release)
        {
            if (!Uri.TryCreate(Release.Link, UriKind.Absolute, out var link) || (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps))
                return "No .torrent download";

            using var response = await http.GetAsync(link, CancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"Download failed: HTTP {(int)response.StatusCode}";

            var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);

            using var mem = new MemoryStream(bytes);
            var bTorrent = new BencodeParser().Parse<BDictionary>(mem);
            var info = bTorrent.Get<BDictionary>(TorrentFields.Info);
            if (info == null)
                return "Not a .torrent file";

            if (!BencodeUtils.HasSafePaths(info))
                return "Unsafe file paths";

            var (v1, v2) = BencodeUtils.GetInfoHashes(bTorrent);

            if ((v1 != null && existing.Contains(Convert.ToHexStringLower(v1))) || (v2 != null && existing.Contains(Convert.ToHexStringLower(v2))))
                return "Already in client";

            var match = BencodeUtils.MatchFileTrees(info, sources);
            if (match == null)
                return "Different file tree";

            var verifiable = info.ContainsKey("file tree") ? BencodeUtils.AssignHashesToPiecesV2(bTorrent).Any(x => x.PieceHashes.Length != 0) : BencodeUtils.AssignHashesToPieces(bTorrent).Any(x => x.PieceHashes.Length != 0);
            if (!verifiable)
                return "No pieces can be verified";

            // Verify against the files where they are now, before anything is written or added
            if (!await BencodeUtils.FastVerifyFilesOnDisk(bTorrent, _ => match.DiskFiles, Logger))
                return "Piece hashes don't match";

            if (!match.Strict) {
                static bool isBlocked(string Target)
                {
                    if (Directory.Exists(Target))
                        return true;

                    for (var dir = Path.GetDirectoryName(Target);!String.IsNullOrEmpty(dir);dir = Path.GetDirectoryName(dir)) {
                        if (System.IO.File.Exists(dir))
                            return true;
                    }

                    return false;
                }

                // Links go in the base directory of the source, where the torrent's own name makes the folder
                var targetFiles = BencodeUtils.GetFilesOnDisk(info, sources[0].BaseDirectory);

                if (targetFiles.Any(x => isBlocked(x.Path)))
                    return "A file and a folder would have the same path";

                // Linking keeps the files that are already there, so they have to be this torrent's

                if (targetFiles.Any(x => System.IO.File.Exists(x.Path)) && !await BencodeUtils.FastVerifyFilesOnDisk(bTorrent, _ => [ .. targetFiles.Select((x, i) => System.IO.File.Exists(x.Path) ? x : match.DiskFiles[i]) ], Logger))
                    return "Existing files in the target folder don't match";

                // Earlier matches aren't linked yet, so the disk doesn't show which file they'd put at the same path
                if (targetFiles.Where((x, i) => plannedLinks.TryGetValue(x.Path, out var source) && source != match.DiskFiles[i].Path).Any())
                    return "Different files at the same paths as another match";

                for (var x = 0;x < targetFiles.Length;x++)
                    plannedLinks[targetFiles[x].Path] = match.DiskFiles[x].Path;
            }

            if (v1 != null)
                existing.Add(Convert.ToHexStringLower(v1));
            if (v2 != null)
                existing.Add(Convert.ToHexStringLower(v2));

            matches.Add(new CrossSeedMatch(Indexer, Release.Title, bytes, !match.Strict));
            return null;
        }

        Session.Progress.Text = $"Searching {input.Indexers.Length} indexer(s)...";

        var results = await Task.WhenAll(input.Indexers.Select(async entry => {
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
                return new IndexerResult(entry.Name, "Not a valid Torznab URL", [ ]);

            try {
                var found = new List<Release>();

                foreach (var query in queries) {
                    var (error, releases) = await search(uri, query);
                    if (error != null)
                        return new IndexerResult(entry.Name, error, [ ]);

                    found.AddRange(releases);

                    if (found.Any(isOfSourceSize))
                        break;
                }

                return new IndexerResult(entry.Name, null, [ ..found.DistinctBy(x => x.Link) ]);
            } catch (Exception ex) when (ex is not OperationCanceledException || !CancellationToken.IsCancellationRequested) {
                Logger.LogWarning("Cross-seed search failed on {Indexer}: {Reason}", entry.Name, describe(ex));
                return new IndexerResult(entry.Name, describe(ex), []);
            }
        }));

        // One at a time, since candidates share the existing hashes and planned links and all read from the same disk
        foreach (var (indexer, error, releases) in results) {
            if (error != null) {
                skipped.Add(new CrossSeedSkip(indexer, "", error));
                continue;
            }

            var ofSize = releases.Where(isOfSourceSize).ToArray();
            var otherSize = releases.Length - ofSize.Length;

            if (releases.Length == 0)
                skipped.Add(new CrossSeedSkip(indexer, "", "No results"));
            else if (otherSize != 0)
                skipped.Add(new CrossSeedSkip(indexer, "", $"{otherSize} result(s) with a different or unknown size"));

            foreach (var release in ofSize.DistinctBy(x => x.Link)) {
                CancellationToken.ThrowIfCancellationRequested();

                if (release.InfoHash != null && existing.Contains(release.InfoHash)) {
                    skipped.Add(new CrossSeedSkip(indexer, release.Title, "Already in client"));
                    continue;
                }

                Session.Progress.Text = $"{indexer}: checking {release.Title}";

                try {
                    var reason = await evaluate(indexer, release);
                    if (reason != null)
                        skipped.Add(new CrossSeedSkip(indexer, release.Title, reason));
                } catch (Exception ex) when (ex is not OperationCanceledException || !CancellationToken.IsCancellationRequested) {
                    skipped.Add(new CrossSeedSkip(indexer, release.Title, describe(ex)));
                }
            }
        }

        Session.Send(new CrossSeedSearchResult([.. matches], [.. skipped]));
        Session.Progress.State = TASK_STATE.DONE;
    }
}
