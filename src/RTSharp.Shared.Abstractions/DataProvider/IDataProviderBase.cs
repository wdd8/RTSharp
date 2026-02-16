namespace RTSharp.Shared.Abstractions.DataProvider;

public interface IDataProviderBase<T>
{
    T Capabilities { get; }
}
