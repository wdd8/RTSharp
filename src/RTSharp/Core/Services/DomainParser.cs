using Nager.PublicSuffix.RuleProviders.CacheProviders;
using Nager.PublicSuffix.RuleProviders;

using System.Threading.Tasks;
using Nager.PublicSuffix;
using System;

namespace RTSharp.Core.Services;

public class DomainParser
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    Nager.PublicSuffix.DomainParser Parser;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public async Task Initialize()
    {
        var provider = new CachedHttpRuleProvider(new LocalFileSystemCacheProvider(), Http.Client);
        await provider.BuildAsync();
        Parser = new(provider);
    }

    public DomainInfo? Parse(string Domain)
    {
        if (Parser == null)
            throw new InvalidOperationException($"{nameof(DomainParser)} not initialized");

        return Parser.Parse(Domain);
    }
}
