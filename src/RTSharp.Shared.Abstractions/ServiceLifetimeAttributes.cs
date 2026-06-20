
namespace RTSharp.Shared.Abstractions
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
    public class SingletonAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
    public class ScopedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
    public class TransientAttribute : Attribute
    {
    }
}
