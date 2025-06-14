using Nager.PublicSuffix.RuleProviders.CacheProviders;
using Nager.PublicSuffix.RuleProviders;

using System.Threading.Tasks;
using Nager.PublicSuffix;

namespace RTSharp.Core.Services
{
    public class DomainParser
    {
        Nager.PublicSuffix.DomainParser Parser;

        public async Task Initialize()
        {
            var provider = new CachedHttpRuleProvider(new LocalFileSystemCacheProvider(), Http.Client);
            await provider.BuildAsync();
            Parser = new(provider);
        }

        public DomainInfo Parse(string Domain)
        {
            return Parser.Parse(Domain);
        }
    }
}
