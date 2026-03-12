using Avalonia.Data.Core;

namespace RTSharp.Shared.Controls.DataGridFilters;

public static class Utils
{
    public static ClrPropertyInfo CreateProperty<TValue>(
        string name,
        Func<object, object> getter,
        Action<object, object?>? setter = null)
    {
        return new ClrPropertyInfo(
            name,
            getter,
            setter,
            typeof(TValue));
    }
}
