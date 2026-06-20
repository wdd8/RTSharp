using Arin.NET.Client;
using Arin.NET.Entities;

using NetTools;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using Serilog;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Whois;

[Singleton]
public class RdapWhoisProvider : IWhoisProvider
{
    public string Id => "RDAP";

    public string DisplayName => "RDAP (via ARIN)";

    public string HelpText => "RDAP method is a direct query to ARIN, which returns whois data. Name is then resolved from whois data, which can lead to inaccuracies.";

    private static readonly ArinClient ArinClient = new();

    private static readonly RegionInfo[] Regions = [ ..CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(x => new RegionInfo(x.Name)) ];

    public async Task<WhoisInfo?> GetWhoisInfo(IPAddress In, IReadOnlyDictionary<string, string> DomainReplacements, CancellationToken Token = default)
    {
        if (IpUtils.IsPrivate(In)) {
            return new WhoisInfo(
                Country: null,
                Domain: null,
                Organization: "LAN",
                Range: new IPAddressRange(In)
            );
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
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
        var country = vcards.Select(x => x?.FirstOrDefault(i => i.Key == "adr").Value).FirstOrDefault(x => x != null)?.Value?.Where(x => !String.IsNullOrWhiteSpace(x))?.LastOrDefault();

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
            country = Regions.FirstOrDefault(region => region.EnglishName.Contains(country))?.TwoLetterISORegionName;

            if (country == null && domain?.Contains('.') == true) {
                try {
                    country = new RegionInfo(domain.Split('.')[^1]).TwoLetterISORegionName;
                } catch { }
            }
        }
        if (String.IsNullOrWhiteSpace(country) && domain?.Contains('.') == true) {
            try {
                country = new RegionInfo(domain.Split('.')[^1]).TwoLetterISORegionName;
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
            organization = fnOrg ?? "Unknown";
        }

        return new WhoisInfo(
            Domain: String.IsNullOrWhiteSpace(domain) ? null : domain,
            Range: range,
            Organization: organization,
            Country: String.IsNullOrWhiteSpace(country) ? null : country
        );
    }
}
