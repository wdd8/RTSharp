namespace RTSharp.Shared.Abstractions;

/// <summary>
/// Exponentially Weighted Moving Average
/// </summary>
public class SpeedMovingAverage(int ItemsMax, double Alpha = 0.8) : ISpeedMovingAverage
{
    private readonly LinkedList<(DateTime TimeUtc, long TotalBytes)> Items = new();
    private readonly object ItemsLock = new();

    public void Value(long TotalBytes) => Value(DateTime.UtcNow, TotalBytes);

    public void Value(DateTime Time, long TotalBytes)
    {
        lock (ItemsLock) {
            Items.AddLast((Time, TotalBytes));
            if (Items.Count > ItemsMax) {
                Items.RemoveFirst();
            }
        }
    }

    public void ValueIfChanged(long TotalBytes) => ValueIfChanged(DateTime.UtcNow, TotalBytes);

    public void ValueIfChanged(DateTime Time, long TotalBytes)
    {
        lock (ItemsLock) {
            if (Items.Last?.Value.TotalBytes == TotalBytes)
                return;

            Items.AddLast((Time, TotalBytes));
            if (Items.Count > ItemsMax) {
                Items.RemoveFirst();
            }
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

        return Math.Max(0, ema ?? 0);
    }

    public double CalculateSpeed()
    {
        lock (ItemsLock) {
            Span<(DateTime TimeUtc, long TotalBytes)> snapshot = Items.Count <= 64
                ? stackalloc (DateTime, long)[Items.Count]
                : new (DateTime, long)[Items.Count];
            var x = 0;
            foreach (var item in Items)
                snapshot[x++] = item;

            return CalculateSpeed(snapshot, Alpha);
        }
    }
}
