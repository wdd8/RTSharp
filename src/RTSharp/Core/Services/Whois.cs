using NetTools;
using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whois;
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

        public static async Task<WhoisInfo?> GetWhoisInfo(IPAddress In, Dictionary<string, string> DomainReplacements)
        {
            global::Whois.NET.WhoisResponse? whois;
            try {
                whois = await global::Whois.NET.WhoisClient.QueryAsync(In.ToString(), encoding: Encoding.UTF8, timeout: 5);
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
