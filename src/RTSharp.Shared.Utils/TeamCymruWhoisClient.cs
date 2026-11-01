using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RTSharp.Shared.Utils;

public sealed record TeamCymruWhoisInfo(uint Asn, IPNetwork Prefix, string? Country, string? OrgName);

public sealed class TeamCymruWhoisClient : IDisposable
{
    public static TeamCymruWhoisClient Shared { get; } = new();

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);
    private readonly string Host;
    private readonly int Port;
    private readonly SemaphoreSlim ConnectionLock = new(1, 1);
    private readonly Timer IdleTimer;
    private TcpClient? Tcp;
    private StreamReader? Reader;
    private StreamWriter? Writer;
    private bool Disposed;

    public TeamCymruWhoisClient(string Host = "whois.cymru.com", int Port = 43)
    {
        this.Host = Host;
        this.Port = Port;
        IdleTimer = new Timer(_ => CloseIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task<TeamCymruWhoisInfo?> Query(IPAddress Address, CancellationToken Token = default)
    {
        var results = await QueryMany([Address], Token).ConfigureAwait(false);
        return results.GetValueOrDefault(Normalize(Address));
    }

    public async Task<Dictionary<IPAddress, TeamCymruWhoisInfo>> QueryMany(IEnumerable<IPAddress> Addresses, CancellationToken Token = default)
    {
        var results = new Dictionary<IPAddress, TeamCymruWhoisInfo>();
        await ConnectionLock.WaitAsync(Token).ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(Disposed, this);
            IdleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var batch in Addresses.Select(Normalize).Distinct().Chunk(256)) {
                for (var x = 0; ; x++) {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(batch.Length == 1 ? 10 : 30));
                    try {
                        await EnsureOpen(timeout.Token).ConfigureAwait(false);
                        await Writer!.WriteAsync((String.Join("\n", batch.Select(x => x.ToString())) + "\n").AsMemory(), timeout.Token).ConfigureAwait(false);
                        await Writer.FlushAsync(timeout.Token).ConfigureAwait(false);

                        var pending = batch.ToHashSet();
                        while (pending.Count > 0) {
                            var line = await Reader!.ReadLineAsync(timeout.Token).ConfigureAwait(false) ?? throw new IOException("Incomplete Team Cymru WHOIS response");
                            if (!TryParseLine(line, out var address, out var info) || !pending.Remove(address!))
                                continue;
                            if (info != null)
                                results[address!] = info;
                            else
                                results.Remove(address!);
                        }
                        break;
                    } catch (OperationCanceledException) {
                        Close();
                        throw;
                    } catch {
                        Close();
                        if (x == 1)
                            throw;
                    }
                }
            }
            return results;
        } finally {
            if (Tcp != null)
                IdleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            ConnectionLock.Release();
        }
    }

    private async Task EnsureOpen(CancellationToken Token)
    {
        if (Tcp?.Connected == true)
            return;
        Close();
        Tcp = new TcpClient();
        await Tcp.ConnectAsync(Host, Port, Token).ConfigureAwait(false);
        var stream = Tcp.GetStream();
        Reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        Writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        await Writer.WriteAsync("begin\nverbose\n".AsMemory(), Token).ConfigureAwait(false);
        await Writer.FlushAsync(Token).ConfigureAwait(false);
    }

    private static IPAddress Normalize(IPAddress Address) => Address.IsIPv4MappedToIPv6 ? Address.MapToIPv4() : Address;

    private static bool TryParseLine(string Line, out IPAddress? Address, out TeamCymruWhoisInfo? Info)
    {
        Address = null;
        Info = null;
        var parts = Line.Split('|');
        if (parts.Length < 3 || !IPAddress.TryParse(parts[1].Trim(), out var address))
            return false;
        Address = Normalize(address);
        var asnText = parts[0].Trim();
        if (asnText == "NA")
            return true;
        if (parts.Length < 7 || !uint.TryParse(asnText, out var asn) || asn == 0)
            return false;
        if (!IPNetwork.TryParse(parts[2].Trim(), out var prefix))
            prefix = new IPNetwork(Address, Address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        var country = parts[3].Trim();
        Info = new TeamCymruWhoisInfo(asn, prefix, country.Length == 2 ? country : null, ParseOrgName(parts[6].Trim()));
        return true;
    }

    private static string? ParseOrgName(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
            return null;
        var dash = Value.IndexOf(" - ", StringComparison.Ordinal);
        var description = dash >= 0 ? Value[(dash + 3)..] : Value;
        if (description.Length > 4 && description[^4] == ',' && char.IsUpper(description[^2]) && char.IsUpper(description[^1]))
            description = description[..^4].TrimEnd();
        return string.IsNullOrWhiteSpace(description) ? Value : description;
    }

    private void CloseIdle()
    {
        if (!ConnectionLock.Wait(0))
            return;
        try {
            Close();
        } finally {
            ConnectionLock.Release();
        }
    }

    private void Close()
    {
        Tcp?.Dispose();
        try {
            Writer?.Dispose();
        } catch (IOException) {
        } catch (ObjectDisposedException) {
        }
        Reader?.Dispose();
        Tcp = null;
        Reader = null;
        Writer = null;
    }

    public void Dispose()
    {
        ConnectionLock.Wait();
        try {
            if (Disposed)
                return;
            Disposed = true;
            IdleTimer.Dispose();
            Close();
        } finally {
            ConnectionLock.Release();
        }
    }
}
