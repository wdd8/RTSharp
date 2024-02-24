using RTSharp.Shared.Abstractions;

namespace RTSharp.Shared.Abstractions
{
    public interface ISpeedMovingAverage
    {
        double CalculateSpeed();
        void Value(DateTime Time, long TotalBytes);
        void Value(long TotalBytes);
        void ValueIfChanged(DateTime Time, long TotalBytes);
        void ValueIfChanged(long TotalBytes);
    }

    [Singleton]
    public interface ISpeedMovingAverageService
    {
        ISpeedMovingAverage Get(IPlugin Plugin, string Id, int itemsMax = 5, double Alpha = 0.8);
    }
}