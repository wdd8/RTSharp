namespace RTSharp.Daemon.Utils;

public class SpeedMovingAverage
{
    private int ItemsMax;
    private LinkedList<(DateTime TimeUtc, long TotalBytes)> Items = new();
    private const double Alpha = 0.8;

    public SpeedMovingAverage(int ItemsMax)
    {
        this.ItemsMax = ItemsMax;
    }

    public void Value(long TotalBytes) => Value(DateTime.UtcNow, TotalBytes);

    public void Value(DateTime Time, long TotalBytes)
    {
        Items.AddLast((Time, TotalBytes));
        if (Items.Count > ItemsMax) {
            Items.RemoveFirst();
        }
    }

    public static double CalculateSpeed(IList<(DateTime TimeUtc, long TotalBytes)> Items)
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

        return CalculateAvg(deltas);
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

        return CalculateAvg(deltas);
    }

    private static double CalculateAvg(IEnumerable<double> Deltas)
    {
        return Deltas.Aggregate((ema, nextQuote) => Alpha * nextQuote + (1 - Alpha) * ema);
    }
}
