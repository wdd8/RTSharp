using System.Reactive.Disposables;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Abstractions.Client;

public abstract partial class ObservableViewModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable VMDisposables = [];

    public virtual void OnContextPopulated() { }

    public void AddDisposable(IDisposable In) => VMDisposables.Add(In);

    public void Dispose() {
        VMDisposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
