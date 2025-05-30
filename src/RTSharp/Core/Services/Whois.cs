using NetTools;
using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whois;
using Whois.NET;
using Whois.Parsers;

namespace RTSharp.Core.Services
{
    public class WhoisInfo
    {
        public IPAddressRange Range { get; set; }
        public string? Country { get; set; }
        public string? Organization { get; set; }
        public string? Domain { get; set; }
    }

    public class Whois
    {
        private static Regex DomainRegex = new(@"@(([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,6})", RegexOptions.Compiled);
        private static Regex CidrRegex = new(@"(?<adr>[\da-f\.:]+)/(?<mask>\d+)", RegexOptions.Compiled);

        public static bool IsPrivate(IPAddress ip)
        {
            // Map back to IPv4 if mapped to IPv6, for example "::ffff:1.2.3.4" to "1.2.3.4".
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            // Checks loopback ranges for both IPv4 and IPv6.
            if (IPAddress.IsLoopback(ip))
                return true;

            // IPv4
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return IsPrivateIPv4(ip.GetAddressBytes());

            // IPv6
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) {
                return ip.IsIPv6LinkLocal ||
                       ip.IsIPv6UniqueLocal ||
                       ip.IsIPv6SiteLocal;
            }

            throw new NotSupportedException(
                    $"IP address family {ip.AddressFamily} is not supported, expected only IPv4 (InterNetwork) or IPv6 (InterNetworkV6).");
        }

        private static bool IsPrivateIPv4(byte[] ipv4Bytes)
        {
            // Link local (no IP assigned by DHCP): 169.254.0.0 to 169.254.255.255 (169.254.0.0/16)
            bool IsLinkLocal() => ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254;

            // Class A private range: 10.0.0.0 – 10.255.255.255 (10.0.0.0/8)
            bool IsClassA() => ipv4Bytes[0] == 10;

            // Class B private range: 172.16.0.0 – 172.31.255.255 (172.16.0.0/12)
            bool IsClassB() => ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31;

            // Class C private range: 192.168.0.0 – 192.168.255.255 (192.168.0.0/16)
            bool IsClassC() => ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168;

            return IsLinkLocal() || IsClassA() || IsClassC() || IsClassB();
        }

        public static async Task<WhoisInfo?> GetWhoisInfo(IPAddress In, Dictionary<string, string> DomainReplacements)
        {
            if (IsPrivate(In)) {
                return new WhoisInfo {
                    Country = null,
                    Domain = null,
                    Organization = "LAN",
                    Range = new IPAddressRange(In)
                };
            }

            global::Whois.NET.WhoisResponse? whois;
            try {
                whois = await global::Whois.NET.WhoisClient.QueryAsync(In.ToString(), new WhoisQueryOptions {
                    Timeout = 5000,
                    RethrowExceptions = true,
                    Encoding = Encoding.UTF8
                });
            } catch (Exception ex) {
                Log.Logger.Debug(ex, "Failed to fetch information for peer " + In);
                return null;
            }

            var raw = whois.Raw;

            var parser = new WhoisParser();
            var parsed = parser.Parse(whois.RespondedServers.Last(), raw);

            var org = parsed?.Registrant?.Name ?? parsed?.AdminContact?.Organization ?? parsed?.TechnicalContact?.Organization ?? parsed?.Registrant?.Organization ?? null;
            var ret = new WhoisInfo {
                Organization = org == null ? null : org.Trim(),
            };
            
            var domainMatches = DomainRegex.Match(raw);
            if (domainMatches.Groups[0].Captures.Count != 0)
                ret.Domain = domainMatches.Groups[0].Captures[0].Value[1..];

            if (ret.Domain != null) {
                if (ret.Domain.StartsWith("abuse."))
                    ret.Domain = ret.Domain[6..];
            }

            foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
                if (line.StartsWith('%'))
                    continue;

                var colIdx = line.IndexOf(':');
                if (colIdx == -1)
                    continue;

                var key = line[..colIdx];
                var val = line[(colIdx+1)..].Trim();

                switch (key.ToLowerInvariant()) {
                    case "country":
                        ret.Country = val.ToLower();
                        break;
                    case "inetnum":
                    case "netrange":
                    case "cidr":
                        try {
                            ret.Range = IPAddressRange.Parse(val);
                        } catch { }
                        
                        if (ret.Range == null || !ret.Range.Contains(In)) {
                            var cidrMatch = CidrRegex.Match(val);
                            if (!cidrMatch.Success)
                                continue;

                            var mask = Int32.Parse(cidrMatch.Groups["mask"].Value);
                            ret.Range = IPAddressRange.Parse(In + "/" + mask);
                        }
                        break;
                    case "organization":
                    case "orgname":
                    case "org-name":
                    case "responsible":
                    case "owner":
                    case "person":
                    case "mnt-by":
                    case "role":
                    case "descr":
                        ret.Organization ??= val.Trim();
                        break;
                }
            }

            // Domain replacements
            if (ret.Domain != null && DomainReplacements.TryGetValue(ret.Domain, out var domainReplacement))
                ret.Domain = domainReplacement;

            // Fallback on range
            if (ret.Range == null)
                ret.Range = new IPAddressRange(In);

            if (ret.Organization == null)
                ret.Organization = "Unknown";

            return ret;
        }
    }
}
