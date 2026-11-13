using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.VisualTree;

using System.Runtime.CompilerServices;

namespace RTSharp.Shared.Controls;

public static class AvaloniaPrivateApi
{
    // Avalonia looks up a control's implicit ControlTheme with it only as the control attaches to the logical tree
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ReevaluateImplicitTheme")]
    internal static extern void ReevaluateImplicitTheme(StyledElement Element);

    private const string ValueStoreType = "Avalonia.PropertyStore.ValueStore, Avalonia.Base";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetValueStore")]
    [return: UnsafeAccessorType(ValueStoreType)]
    internal static extern object GetValueStore(AvaloniaObject Object);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "BeginStyling")]
    internal static extern void BeginStyling([UnsafeAccessorType(ValueStoreType)] object ValueStore);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "EndStyling")]
    internal static extern void EndStyling([UnsafeAccessorType(ValueStoreType)] object ValueStore);

    // The template the control's template parts were built from, which a different Template replaces as the control is measured
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_appliedTemplate")]
    internal static extern ref IControlTemplate? AppliedTemplate(TemplatedControl Control);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_TemplatedParent")]
    internal static extern void SetTemplatedParent(StyledElement Element, AvaloniaObject? TemplatedParent);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_CurrentMutable")]
    internal static extern AvaloniaLocator CurrentMutable(AvaloniaLocator? Type);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_registry")]
    internal static extern ref Dictionary<Type, Func<object?>> Registry(AvaloniaLocator Locator);

    /// <summary>
    /// Looks up the implicit control theme of the element and its logical descendants again, and applies it, for them to take the
    /// control themes of styles that replaced the ones they were attached with
    /// </summary>
    public static void RefreshImplicitControlThemes(StyledElement Element)
    {
        /*
         * Reevaluating the implicit theme removes the previous control theme's values, and applying styling adds the new one's.
         * In between, the element's properties would fall back to their defaults, which for a window reaches the native window,
         * and it handles a resize in the middle of the theme switch. Batched, a property goes from the previous theme's value to
         * the new one's, and doesn't change when both themes set it the same
         */
        var valueStore = GetValueStore(Element);
        BeginStyling(valueStore);
        try {
            ReevaluateImplicitTheme(Element);
            Element.ApplyStyling();
        } finally {
            EndStyling(valueStore);
        }

        /*
         * Applying a new template detaches the old one's parts as far as the control's visual tree reaches, which leaves out the
         * content of a closed popup. An items presenter in it would keep the items in its panel, for a menu item's submenu the
         * menu items themselves, so the new template's panel couldn't take them. Detached, it releases them.
         * The new template can come before the control is reached here, as an items control gives its containers its item
         * container theme. The template's root is the control's visual child but not among its logical children, and the
         * logical tree from the root reaches into a popup
         */
        if (Element is TemplatedControl templated && templated.Template != AppliedTemplate(templated)) {
            var unreachedParts = templated.GetVisualChildren()
                .SelectMany(x => x.GetSelfAndLogicalDescendants())
                .OfType<Visual>()
                .Where(x => x.TemplatedParent == templated && !templated.IsVisualAncestorOf(x))
                .ToArray();

            foreach (var part in unreachedParts)
                SetTemplatedParent(part, null);
        }

        // A new control theme replaces the element's template, and the logical children in it
        foreach (var child in Element.GetLogicalChildren().ToArray()) {
            if (child is StyledElement styled)
                RefreshImplicitControlThemes(styled);
        }
    }

    /// <summary>
    /// Replaces the loader that <see cref="AssetLoader"/> and avares:// links go through with one wrapping it
    /// </summary>
    public static T ReplaceAssetLoader<T>(Func<IAssetLoader, T> Replace) where T : IAssetLoader
    {
        var registry = Registry(CurrentMutable(null));
        var replacement = Replace((IAssetLoader)registry[typeof(IAssetLoader)]()!);
        registry[typeof(IAssetLoader)] = () => replacement;
        return replacement;
    }
}
