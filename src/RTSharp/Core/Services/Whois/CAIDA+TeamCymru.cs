using NetTools;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Whois;

[Singleton]
public sealed partial class CaidaTeamCymruWhoisProvider(DomainParser DomainParser) : IWhoisProvider
{
    public string Id => "CAIDA_PLUS_TEAM_CYMRU";

    public string DisplayName => "CAIDA + Team Cymru";

    public string HelpText => "CAIDA (Center for Applied Internet Data Analysis) + Team Cymru maps IPs to ASNs via whois.cymru.com, then resolves ASNs to names using the CAIDA AS->org dataset. Produces more accurate company names than RDAP, however requires a dataset download and queries an API for every lookup.";

    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly TeamCymruConnection CymruConnection = new();

    private static MemoryMappedFile? CaidaMmap;
    private static MemoryMappedViewAccessor? CaidaMmapView;
    private static int CaidaCount;
    private static DateTime CaidaLoadedAt = DateTime.MinValue;

    private static readonly TimeSpan CAIDA_REFRESH_INTERVAL = TimeSpan.FromDays(7);
    private const uint CAIDA_MAGIC = 0x3E852054;
    private const int CAIDA_HEADER_SIZE = 8;
    private const int CAIDE_RECORD_SIZE = 8;

    private static string CaidaIndexPath => Path.Combine(Shared.Abstractions.Consts.USER_DATA_PATH, "caida-as-org.bin");

    public async Task<WhoisInfo?> GetWhoisInfo(IPAddress In, IReadOnlyDictionary<string, string> domainReplacements, CancellationToken Token = default)
    {
        if (IpUtils.IsPrivate(In))
            return new WhoisInfo(Range: new IPAddressRange(In), Country: null, Organization: "LAN", Domain: null);

        var cymruTask = CymruConnection.Query(In);
        var domainTask = ReverseDns(In);

        var cymru = await cymruTask;
        if (cymru == null)
            return null;

        var caidaOrg = await LookupCaidaOrgNameByAsn(cymru.Asn);

        var domain = await domainTask;
        if (domain != null && domainReplacements.TryGetValue(domain, out var replacement))
            domain = replacement;

        return new WhoisInfo(
            Range: cymru.Prefix,
            Country: cymru.Country,
            Organization: caidaOrg ?? cymru.OrgName ?? "Unknown",
            Domain: domain
        );
    }

    private async Task<string?> ReverseDns(IPAddress In)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            var hostEntry = await Dns.GetHostEntryAsync(In.ToString(), AddressFamily.Unspecified, cts.Token);
            return DomainParser.Parse(hostEntry.HostName)?.RegistrableDomain;
        } catch {
            return null;
        }
    }

    private record TeamCymruData(uint Asn, IPAddressRange Prefix, string? Country, string? OrgName);

    private sealed class TeamCymruConnection
    {
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

        private readonly SemaphoreSlim ConnLock = new(1, 1);
        private readonly Timer IdleTimer;

        private TcpClient? Tcp;
        private NetworkStream? Stream;
        private StreamReader? Reader;
        private StreamWriter? Writer;

        public TeamCymruConnection()
        {
            IdleTimer = new Timer(_ => CloseIdle(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task<TeamCymruData?> Query(IPAddress In)
        {
            await ConnLock.WaitAsync(); try {
                IdleTimer.Change(Timeout.Infinite, Timeout.Infinite);

                for (int x = 0; x < 2; x++) {
                    using var cts = new CancellationTokenSource(QueryTimeout);
                    try {
                        await EnsureOpen(cts.Token);

                        await Writer!.WriteAsync($"{In}\n".AsMemory(), cts.Token);
                        await Writer.FlushAsync(cts.Token);

                        string? line;
                        while ((line = await Reader!.ReadLineAsync(cts.Token)) != null) {
                            var result = ParseLine(line, In);
                            if (result != null)
                                return result;
                        }
                        // invalid data?
                        Close();
                    } catch (OperationCanceledException) {
                        Close();
                        Log.Logger.Warning($"Team Cymru WHOIS query timed out for {In}");
                        return null;
                    } catch (Exception ex) {
                        Close();
                        if (x == 1) {
                            Log.Logger.Warning(ex, $"Team Cymru WHOIS query failed for {In}");
                            return null;
                        }
                    }
                }
                return null;
            } finally {
                if (Tcp != null)
                    IdleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
                ConnLock.Release();
            }
        }

        private async Task EnsureOpen(CancellationToken Token)
        {
            if (Tcp?.Connected == true)
                return;

            Close();

            var tcp = new TcpClient();
            await tcp.ConnectAsync("whois.cymru.com", 43, Token);
            var stream = tcp.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) {
                AutoFlush = false
            };

            await writer.WriteAsync("begin\nverbose\n".AsMemory(), Token);
            await writer.FlushAsync(Token);

            Tcp = tcp;
            Stream = stream;
            Reader = reader;
            Writer = writer;
        }

        private void CloseIdle()
        {
            if (!ConnLock.Wait(0))
                return;
            try {
                if (Tcp == null)
                    return;
                try {
                    Writer?.Write("end\n");
                    Writer?.Flush();
                } catch { }
                Close();
            } finally {
                ConnLock.Release();
            }
        }

        private void Close()
        {
            try { Writer?.Dispose(); } catch { }
            try { Reader?.Dispose(); } catch { }
            try { Stream?.Dispose(); } catch { }
            try { Tcp?.Dispose(); } catch { }
            Writer = null;
            Reader = null;
            Stream = null;
            Tcp = null;
        }

        private static TeamCymruData? ParseLine(string Line, IPAddress In)
        {
            Line = Line.Trim();
            if (Line.Length == 0 || Line[0] == '#')
                return null;

            var parts = Line.Split('|');
            if (parts.Length < 7)
                return null;

            if (!UInt32.TryParse(parts[0].Trim(), out var asn))
                return null;

            var prefixStr = parts[2].Trim();
            var country = parts[3].Trim();
            var orgName = ParseCymruOrgName(parts[6].Trim());

            IPAddressRange prefix;
            try {
                prefix = IPAddressRange.Parse(prefixStr);
            } catch {
                prefix = new IPAddressRange(In);
            }

            return new TeamCymruData(
                Asn: asn,
                Prefix: prefix,
                Country: country.Length == 2 ? country : null,
                OrgName: string.IsNullOrWhiteSpace(orgName) ? null : orgName
            );
        }

        private static string? ParseCymruOrgName(string In)
        {
            if (string.IsNullOrWhiteSpace(In))
                return null;

            var dashIdx = In.IndexOf(" - ", StringComparison.Ordinal);
            var description = dashIdx >= 0 ? In[(dashIdx + 3)..] : In;

            // Strip trailing ", XX" where XX is a 2-letter country code
            if (description.Length > 4 && description[^4] == ',' && char.IsUpper(description[^2]) && char.IsUpper(description[^1]))
                description = description[..^4].TrimEnd();

            return string.IsNullOrWhiteSpace(description) ? In : description;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = CAIDE_RECORD_SIZE)]
    private readonly struct CaidaRecord
    {
        public readonly uint Asn;
        public readonly uint BlobOffset;
    }

    private readonly struct CaidaAsnKey(uint Asn) : IComparable<CaidaRecord>
    {
        public int CompareTo(CaidaRecord Other) => Asn.CompareTo(Other.Asn);
    }

    private static async Task<string?> LookupCaidaOrgNameByAsn(uint In)
    {
        await EnsureCaidaLoaded();

        var accessor = CaidaMmapView;
        if (accessor == null)
            return null;

        try {
            uint blobOffset;
            unsafe {
                byte* ptr = null;
                var handle = accessor.SafeMemoryMappedViewHandle;
                handle.AcquirePointer(ref ptr);
                try {
                    var records = new ReadOnlySpan<CaidaRecord>(ptr + accessor.PointerOffset + CAIDA_HEADER_SIZE, CaidaCount);
                    int idx = records.BinarySearch(new CaidaAsnKey(In));
                    if (idx < 0)
                        return null;
                    blobOffset = records[idx].BlobOffset;
                } finally {
                    handle.ReleasePointer();
                }
            }
            return ReadBlobString(accessor, blobOffset);
        } catch (ObjectDisposedException) {
            return null;
        }
    }

    private static string ReadBlobString(MemoryMappedViewAccessor Accessor, uint Offset)
    {
        int len = Accessor.ReadInt32(Offset);
        var buf = new byte[len];
        Accessor.ReadArray(Offset + 4, buf, 0, len);
        return Encoding.UTF8.GetString(buf);
    }

    [GeneratedRegex(@"\d{8}\.as-org2info\.txt\.gz")]
    private static partial Regex CaidaFilenamePattern();
    private static async Task EnsureCaidaLoaded()
    {
        if (CaidaMmapView != null && DateTime.UtcNow - CaidaLoadedAt < CAIDA_REFRESH_INTERVAL)
            return;

        await Lock.WaitAsync(); try {
            if (CaidaMmapView != null && DateTime.UtcNow - CaidaLoadedAt < CAIDA_REFRESH_INTERVAL)
                return;

            // The compressed dataset is never persisted; it is piped straight into the binary index.
            if (!System.IO.File.Exists(CaidaIndexPath) || (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(CaidaIndexPath)) > CAIDA_REFRESH_INTERVAL) {
                string? datasetUri = null;

                try {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var index = await Http.Client.GetStringAsync("https://publicdata.caida.org/datasets/as-organizations/", cts.Token);
                    var matches = CaidaFilenamePattern().Matches(index);
                    if (matches.Count > 0)
                        datasetUri = $"https://publicdata.caida.org/datasets/as-organizations/{matches[^1].Value}";
                } catch {
                    var date = DateTime.UtcNow;
                    for (int x = 0;x < 12 && datasetUri == null;x++) {
                        var url = $"https://publicdata.caida.org/datasets/as-organizations/{date:yyyyMM}01.as-org2info.txt.gz";
                        try {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                            if (response.IsSuccessStatusCode)
                                datasetUri = url;
                        } catch { }
                        date = date.AddMonths(-1);
                    }
                }

                if (datasetUri == null) {
                    Log.Logger.Warning("Could not locate CAIDA AS->org dataset");
                } else {
                    try {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        await using var stm = await Http.Client.GetStreamAsync(datasetUri, cts.Token);

                        Directory.CreateDirectory(Path.GetDirectoryName(CaidaIndexPath)!);
                        BuildCaidaIndex(stm, CaidaIndexPath);

                        Log.Logger.Information($"Downloaded and indexed CAIDA dataset from {datasetUri}");
                    } catch (Exception ex) {
                        Log.Logger.Warning(ex, $"Failed to download CAIDA dataset from {datasetUri}");
                    }
                }
            }

            if (System.IO.File.Exists(CaidaIndexPath)) {
                OpenCaidaIndex();
                CaidaLoadedAt = DateTime.UtcNow;
            }
        } catch (Exception ex) {
            Log.Logger.Warning(ex, "Failed to load CAIDA dataset");
        } finally {
            Lock.Release();
        }
    }

    private static void OpenCaidaIndex()
    {
        CaidaMmapView?.Dispose();
        CaidaMmap?.Dispose();
        CaidaMmapView = null;
        CaidaMmap = null;

        var mmap = MemoryMappedFile.CreateFromFile(CaidaIndexPath, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        var accessor = mmap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        if (accessor.ReadUInt32(0) != CAIDA_MAGIC) {
            accessor.Dispose();
            mmap.Dispose();
            throw new InvalidDataException("CAIDA index magic mismatch");
        }

        CaidaCount = accessor.ReadInt32(4);
        CaidaMmap = mmap;
        CaidaMmapView = accessor;
    }

    private static void BuildCaidaIndex(Stream GzStream, string IndexPath)
    {
        var asnToOrg = new Dictionary<uint, string>();

        bool inOrgSection = false, inAsnSection = false;

        using (var gz = new GZipStream(GzStream, CompressionMode.Decompress, leaveOpen: true))
        using (var reader = new StreamReader(gz, Encoding.UTF8)) {
            var orgIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? line;
            while ((line = reader.ReadLine()) != null) {
                if (line.StartsWith("# format:org_id", StringComparison.Ordinal)) {
                    inOrgSection = true; inAsnSection = false; continue;
                }
                if (line.StartsWith("# format:aut", StringComparison.Ordinal)) {
                    inOrgSection = false; inAsnSection = true; continue;
                }
                if (line.StartsWith('#'))
                    continue;

                var parts = line.Split('|');

                if (inOrgSection && parts.Length >= 3) {
                    // org_id|changed|org_name|country|source
                    orgIdToName[parts[0]] = parts[2];
                } else if (inAsnSection && parts.Length >= 4) {
                    // aut|changed|aut_name|org_id|opaque_id|source
                    if (UInt32.TryParse(parts[0], out var asn) && orgIdToName.TryGetValue(parts[3], out var orgName))
                        asnToOrg[asn] = orgName;
                }
            }
        }

        var sorted = asnToOrg.OrderBy(x => x.Key).ToArray();
        var count = sorted.Length;

        var stringOffsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        var uniqueOrgs = new List<byte[]>();
        var strOffset = (uint)(CAIDA_HEADER_SIZE + (long)count * CAIDE_RECORD_SIZE);
        foreach (var (_, org) in sorted) {
            if (stringOffsets.ContainsKey(org))
                continue;
            var bytes = Encoding.UTF8.GetBytes(org);
            stringOffsets[org] = strOffset;
            uniqueOrgs.Add(bytes);
            strOffset += (uint)(sizeof(int) + bytes.Length);
        }

        using (var fs = new FileStream(IndexPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs, Encoding.UTF8)) {
            writer.Write(CAIDA_MAGIC);
            writer.Write(count);

            // offsets
            foreach (var (asn, org) in sorted) {
                writer.Write(asn);
                writer.Write(stringOffsets[org]);
            }

            // strings
            foreach (var utf8 in uniqueOrgs) {
                writer.Write(utf8.Length);
                writer.Write(utf8);
            }
        }

        Log.Logger.Information($"Built CAIDA index: {count} ASNs");
    }
}
