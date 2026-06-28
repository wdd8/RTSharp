using Microsoft.Extensions.Caching.Memory;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Daemon.Services
{
    /// <summary>
    /// Exponentially Weighted Moving Average
    /// </summary>
    public class SpeedMovingAverage(int ItemsMax, double Alpha = 0.8) : ISpeedMovingAverage
    {
        private readonly LinkedList<(DateTime TimeUtc, long TotalBytes)> Items = new();

        public void Value(long TotalBytes) => Value(DateTime.UtcNow, TotalBytes);

        public void Value(DateTime Time, long TotalBytes)
        {
            Items.AddLast((Time, TotalBytes));
            if (Items.Count > ItemsMax) {
                Items.RemoveFirst();
            }
        }

        public void ValueIfChanged(long TotalBytes) => ValueIfChanged(DateTime.UtcNow, TotalBytes);

        public void ValueIfChanged(DateTime Time, long TotalBytes)
        {
            if (Items.Last?.Value.TotalBytes == TotalBytes)
                return;

            Items.AddLast((Time, TotalBytes));
            if (Items.Count > ItemsMax) {
                Items.RemoveFirst();
            }
        }

        public static double CalculateSpeed(ReadOnlySpan<(DateTime TimeUtc, long TotalBytes)> Items, double Alpha = 0.8)
        {
            if (Items.Length < 2) {
                return 0;
            }

            double? ema = null;
            for (var x = 0;x < Items.Length - 1;x++) {
                var seconds = (Items[x + 1].TimeUtc - Items[x].TimeUtc).TotalSeconds;
                var delta = (Items[x + 1].TotalBytes - Items[x].TotalBytes) / seconds;
                ema = ema == null ? delta : Alpha * delta + (1 - Alpha) * ema;
            }

            return ema ?? 0;
        }

        public double CalculateSpeed()
        {
            if (Items.Count < 2) {
                return 0;
            }

            var deltas = new List<double>(Items.Count - 1);
            var item = Items.First;
            while (item?.Next != null) {
                var (timeUtc, totalBytes) = item.Next.Value;
                var time = timeUtc - item.Value.TimeUtc;

                deltas.Add((totalBytes - item.Value.TotalBytes) / time.TotalSeconds);
                item = item.Next;
            }

            return CalculateAvg(deltas, Alpha);
        }

        private static double CalculateAvg(IEnumerable<double> Deltas, double Alpha = 0.8)
        {
            return Deltas.Aggregate((ema, nextQuote) => Alpha * nextQuote + (1 - Alpha) * ema);
        }
    }


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
}
