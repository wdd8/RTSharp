using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Controls.DataGridFilters;

public partial class SelectedOption<T> : ObservableObject, IComparable
    where T : IComparable
{
    public T? Display { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public int CompareTo(object? obj)
    {
        if (Display == null)
            return 1;

        if (obj is not SelectedOption<T> b)
            return 1;

        return Display.CompareTo(b.Display);
    }
}
