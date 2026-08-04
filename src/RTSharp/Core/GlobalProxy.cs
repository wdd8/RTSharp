using Serilog;

using System;
using System.Net;
using System.Net.Http;

namespace RTSharp.Core;

public sealed class GlobalProxy : IWebProxy
{
    public const string SOCKS4_SCHEME = "socks4";
    public const string SOCKS4A_SCHEME = "socks4a";
    public const string SOCKS5_SCHEME = "socks5";

    public static readonly string[] SUPPORTED_SCHEMES = [ SOCKS4_SCHEME, SOCKS4A_SCHEME, SOCKS5_SCHEME ];

    public static GlobalProxy Instance { get; } = new();

    private volatile Uri? Address;

    public ICredentials? Credentials { get; set; }

    private GlobalProxy()
    {
    }

    public static void Install(Config.Models.Proxy? Config)
    {
        HttpClient.DefaultProxy = Instance;
        Instance.Apply(Config);
    }

    public void Apply(Config.Models.Proxy? Config)
    {
        if (Config?.Enabled != true || String.IsNullOrWhiteSpace(Config.Address)) {
            Address = null;
            Credentials = null;
            return;
        }

        if (!TryParseAddress(Config.Address, out var address)) {
            Log.Logger.Warning("Proxy address {Address} is not valid, proxy disabled", Config.Address);
            Address = null;
            Credentials = null;
            return;
        }

        Address = address;
        Credentials = String.IsNullOrEmpty(Config.Username) ? null : new NetworkCredential(Config.Username, Config.Password);

        Log.Logger.Information("Using proxy {Address}", address);
    }

    public static bool TryParseAddress(string Address, out Uri? Parsed)
    {
        Parsed = null;

        if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri))
            return false;

        if (Array.IndexOf(SUPPORTED_SCHEMES, uri.Scheme) == -1)
            return false;

        if (String.IsNullOrEmpty(uri.Host))
            return false;

        Parsed = uri;
        return true;
    }

    public Uri? GetProxy(Uri Destination) => IsBypassed(Destination) ? null : Address;

    public bool IsBypassed(Uri Host) => Address == null || Host.IsLoopback;
}
