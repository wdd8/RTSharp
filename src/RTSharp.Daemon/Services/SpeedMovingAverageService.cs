using Microsoft.Extensions.Caching.Memory;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Daemon.Services
{
    /// <summary>
    /// Exponentially Weighted Moving Average
    /// </summary>
    public class SpeedMovingAverage : ISpeedMovingAverage
    {
        private int ItemsMax;
        private LinkedList<(DateTime TimeUtc, long TotalBytes)> Items = new();
        private double Alpha;

        public SpeedMovingAverage(int ItemsMax, double Alpha = 0.8)
        {
            this.ItemsMax = ItemsMax;
            this.Alpha = Alpha;
        }

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

        public static double CalculateSpeed(IList<(DateTime TimeUtc, long TotalBytes)> Items, double Alpha = 0.8)
        {
            var count = Items.Count;
            if (count < 2) {
                return 0;
            }

            var deltas = new List<double>(count - 1);
            for (var x = 0;x < Items.Count - 1;x++) {
                var next = Items[x + 1];
                var item = Items[x];
                var time = next.TimeUtc - item.TimeUtc;

                deltas.Add((next.TotalBytes - item.TotalBytes) / time.TotalSeconds);
            }

            return CalculateAvg(deltas, Alpha);
        }

        public double CalculateSpeed()
        {
            if (Items.Count < 2) {
                return 0;
            }

            var deltas = new List<double>(Items.Count - 1);
            var item = Items.First;
            while (item.Next != null) {
                var next = item.Next.Value;
                var time = next.TimeUtc - item.Value.TimeUtc;

                deltas.Add((next.TotalBytes - item.Value.TotalBytes) / time.TotalSeconds);
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

            return cacheEntry;
        }
    }
}
