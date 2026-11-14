using Microsoft.Extensions.Caching.Memory;

using RTSharp.Shared.Abstractions;

using System;

namespace RTSharp.Core.Services;

public class SpeedMovingAverageService : ISpeedMovingAverageService
{
    private readonly MemoryCache Cache = new(new MemoryCacheOptions {
        ExpirationScanFrequency = TimeSpan.FromMinutes(5)
    });

    public ISpeedMovingAverage Get(string Namespace, string Id, int itemsMax = 5, double Alpha = 0.8)
    {
        var key = Namespace + "_" + Id;

        var cacheEntry = Cache.GetOrCreate(key, entry => {
            entry.SetSlidingExpiration(TimeSpan.FromMinutes(1));
            return new SpeedMovingAverage(itemsMax, Alpha);
        });

        return cacheEntry!;
    }
}
