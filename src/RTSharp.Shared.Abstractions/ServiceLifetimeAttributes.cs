
namespace RTSharp.Shared.Abstractions
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class SingletonAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Interface)]
    public class ScopedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Interface)]
    public class TransientAttribute : Attribute
    {
    }
}
