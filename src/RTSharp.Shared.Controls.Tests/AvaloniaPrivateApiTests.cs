using Avalonia;
using Avalonia.Controls.Primitives;

using Xunit;

namespace RTSharp.Shared.Controls.Tests;

public class AvaloniaPrivateApiTests
{
    [Fact]
    public void ReevaluateImplicitThemeExists()
    {
        AvaloniaPrivateApi.ReevaluateImplicitTheme(new StyledElement());
    }

    [Fact]
    public void StylingBatchExists()
    {
        var valueStore = AvaloniaPrivateApi.GetValueStore(new StyledElement());
        AvaloniaPrivateApi.BeginStyling(valueStore);
        AvaloniaPrivateApi.EndStyling(valueStore);
    }

    [Fact]
    public void AppliedTemplateExists()
    {
        Assert.Null(AvaloniaPrivateApi.AppliedTemplate(new TemplatedControl()));
    }

    [Fact]
    public void TemplatedParentSetterExists()
    {
        AvaloniaPrivateApi.SetTemplatedParent(new StyledElement(), null);
    }

    [Fact]
    public void LocatorRegistryExists()
    {
        Assert.NotNull(AvaloniaPrivateApi.Registry(AvaloniaPrivateApi.CurrentMutable(null)));
    }
}
