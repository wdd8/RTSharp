using System.Net;

using NetTools;

namespace RTSharp.Shared.Abstractions.Client;

/// <summary>
/// Result peer origin lookup
/// </summary>
/// <param name="Range">Network range the address belongs to</param>
/// <param name="Country">Two letter country code, or <c>null</c> if unknown</param>
/// <param name="Organization">Owning organization name, <c>null</c> if unknown</param>
/// <param name="Domain">Registrable domain associated with the address, or <c>null</c></param>
public record WhoisInfo(IPAddressRange Range, string? Country, string? Organization, string? Domain);

/// <summary>
/// Resolves the origin of a peer IP address
/// </summary>
[Singleton]
public interface IWhoisProvider
{
    /// <summary>
    /// Unique whois data provider ID
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name of the provider
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Help text as provided to the user
    /// </summary>
    string HelpText { get; }

    /// <summary>
    /// Looks up origin information for an IP address.
    /// </summary>
    /// <param name="Address">Address to look up</param>
    /// <param name="DomainReplacements">Domain replacements</param>
    /// <param name="Token">Cancellation token</param>
    /// <returns>Origin information, or <c>null</c> if the lookup failed</returns>
    Task<WhoisInfo?> GetWhoisInfo(IPAddress Address, IReadOnlyDictionary<string, string> DomainReplacements, CancellationToken Token = default);
}
