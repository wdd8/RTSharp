using InfluxDB.Client.Api.Domain;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace RTSharp.DataProvider.Rtorrent.Server.Utils
{
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

    public class SpeedMovingAverageTests
    {
		[Fact]
		public void TestCalculation()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 1);
			cls.Value(time.AddSeconds(1), 2000);
			cls.Value(time.AddSeconds(2), 3000);
			cls.Value(time.AddSeconds(3), 4000);
			cls.Value(time.AddSeconds(4), 5000);
			cls.Value(time.AddSeconds(5), 6000);

			var avg = cls.CalculateSpeed();

			Assert.Equal(1000, avg);
		}

		[Fact]
		public void TestCalculation2()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 1);
			cls.Value(time.AddSeconds(1), 2000);
			cls.Value(time.AddSeconds(2), 3000);
			cls.Value(time.AddSeconds(3), 4000);
			cls.Value(time.AddSeconds(4), 5000);
			cls.Value(time.AddSeconds(5), 7000);

			var avg = cls.CalculateSpeed();

			Assert.Equal(1800, avg);
		}

		[Fact]
		public void TestCalculation3()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 1);
			cls.Value(time.AddSeconds(1), 2000);
			cls.Value(time.AddSeconds(2), 3000);
			cls.Value(time.AddSeconds(3), 4000);
			cls.Value(time.AddSeconds(4), 6000);
			cls.Value(time.AddSeconds(5), 7000);

			var avg = cls.CalculateSpeed();

			Assert.Equal(1160, avg);
		}

		[Fact]
		public void TestCalculation4()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time.AddSeconds(1), 2000);
			cls.Value(time.AddSeconds(2), 3000);
			cls.Value(time.AddSeconds(3), 4000);
			cls.Value(time.AddSeconds(4), 5000);
			cls.Value(time.AddSeconds(4.1), 6000);

			var avg = cls.CalculateSpeed();

			Assert.Equal(8200, avg, 0.01);
		}

		[Fact]
		public void TestCalculation5()
		{
			var cls = new SpeedMovingAverage(5);

			var avg = cls.CalculateSpeed();

			Assert.Equal(0, avg);
		}

		[Fact]
		public void TestCalculation6()
		{
			var cls = new SpeedMovingAverage(5);
			cls.Value(DateTime.UtcNow, 100);

			var avg = cls.CalculateSpeed();

			Assert.Equal(0, avg);
		}

		[Fact]
		public void TestCalculation7()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 100);
			cls.Value(time, 200);

			var avg = cls.CalculateSpeed();

			Assert.Equal(Double.PositiveInfinity, avg);
		}

		[Fact]
		public void TestCalculation8()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 100);
			cls.Value(time.AddSeconds(-1), 200);

			var avg = cls.CalculateSpeed();

			Assert.Equal(-100, avg);
		}

		[Fact]
		public void TestCalculation9()
		{
			var time = DateTime.UtcNow;
			var cls = new SpeedMovingAverage(5);
			cls.Value(time, 200);
			cls.Value(time.AddSeconds(1), 100);

			var avg = cls.CalculateSpeed();

			Assert.Equal(-100, avg);
		}

		[Fact]
		public void TestCalculation10()
		{
			var time = DateTime.UtcNow;

			var avg = SpeedMovingAverage.CalculateSpeed(new[] {
				(time.AddSeconds(1), 1000L),
				(time.AddSeconds(2), 2000L),
				(time.AddSeconds(3), 3000L),
				(time.AddSeconds(4), 4000L),
				(time.AddSeconds(5), 5000L)
			});

			Assert.Equal(1000, avg);
		}
	}
}
