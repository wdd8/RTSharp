namespace RTSharp.Shared.Abstractions
{
    public interface IDataProviderBase<T>
    {
        IPluginHost PluginHost { get; }

        T Capabilities { get; }
    }
}
