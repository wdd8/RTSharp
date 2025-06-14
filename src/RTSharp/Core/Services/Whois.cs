using Arin.NET.Client;
using Arin.NET.Entities;

using NetTools;
using Serilog;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
        private static ArinClient ArinClient = new();

        private static RegionInfo[] Regions = [ ..CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(x => new RegionInfo(x.Name)) ];

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

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            RdapResponse rdap;
            try {
                rdap = await ArinClient.Query(In, cts.Token);
            } catch {
                return null;
            }

            if (rdap is ErrorResponse error) {
                if (error.ErrorCode == 429) {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
                Log.Logger.Error($"{error.ErrorCode}: {error.Title}");
                return null;
            }

            var resp = (IpResponse)rdap;

            var entities = new List<Entity>();
            void addEntities(Entity entity)
            {
                entities.Add(entity);
                foreach (var e in entity.Entities) {
                    entities.Add(e);
                    addEntities(e);
                }
            }
            foreach (var e in resp.Entities) {
                addEntities(e);
            }

            var vcards = entities.OrderBy(x => x.Roles?.Contains("technical") == true).Select(x => x.VCard);
            var domain = vcards.Select(x => x?.FirstOrDefault(i => i.Key == "email").Value).FirstOrDefault(x => x != null)?.Value?.FirstOrDefault();
            IPAddressRange range;
            string organization = "Unknown";
            string country = vcards.Select(x => x?.FirstOrDefault(i => i.Key == "adr").Value).FirstOrDefault(x => x != null)?.Value?.Where(x => !String.IsNullOrWhiteSpace(x))?.LastOrDefault();

            if (!String.IsNullOrWhiteSpace(domain)) {
                domain = domain.Split('@')[^1];
            }

            // Domain replacements
            if (!String.IsNullOrWhiteSpace(domain) && DomainReplacements.TryGetValue(domain, out var domainReplacement))
                domain = domainReplacement;

            if (String.IsNullOrWhiteSpace(country)) {
                country = vcards.Select(x => x?.FirstOrDefault(i => i.Key == "adr").Value).FirstOrDefault(x => x != null)?.Parameters?.FirstOrDefault().Value?.ToString()?.Split('\n')?.LastOrDefault();
            }
            if (!String.IsNullOrWhiteSpace(country)) {
                Log.Logger.Information("-> " + country);
                country = Regions.FirstOrDefault(region => region.EnglishName.Contains(country))?.TwoLetterISORegionName;
                Log.Logger.Information("?-> " + country);

                if (country == null && domain?.Contains('.') == true) {
                    try {
                        country = new RegionInfo(domain.Split('.')[^1]).TwoLetterISORegionName;
                        Log.Logger.Information("??-> " + country);
                    } catch { }
                }
            }
            if (String.IsNullOrWhiteSpace(country) && domain?.Contains('.') == true) {
                try {
                    country = new RegionInfo(domain.Split('.')[^1]).TwoLetterISORegionName;
                    Log.Logger.Information("??!-> " + country);
                } catch { }
            }

            // Fallback on range
            if (String.IsNullOrWhiteSpace(resp.Handle))
                range = new IPAddressRange(In);
            else
                range = IPAddressRange.Parse(resp.StartAddress + " - " + resp.EndAddress);

            var fnOrg = vcards.Select(x => x?.FirstOrDefault(i => i.Key == "fn").Value).FirstOrDefault(x => x != null)?.Value?.FirstOrDefault();;
            if (!String.IsNullOrWhiteSpace(resp.Name)) {
                organization = resp.Name;

                // Prefer other org string if original is in upper case
                if (fnOrg != null && organization.ToUpperInvariant() == organization) {
                    organization = fnOrg;
                }
            } else {
                organization = fnOrg;
            }

            return new WhoisInfo
            {
                Domain = String.IsNullOrWhiteSpace(domain) ? null : domain,
                Range = range,
                Organization = organization,
                Country = String.IsNullOrWhiteSpace(country) ? null : country
            };
        }
    }
}
