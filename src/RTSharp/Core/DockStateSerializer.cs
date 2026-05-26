using Dock.Model.Avalonia.Controls;
using Dock.Model.Avalonia.Core;
using Dock.Model.Core;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTSharp.Core;

internal class DockNodeDto
{
    public string? Id { get; set; }
    public string? TypeName { get; set; }
    public double? Proportion { get; set; }
    public string? ActiveDockableId { get; set; }
    public Alignment? Alignment { get; set; }
    public Orientation? Orientation { get; set; }
    public List<DockNodeDto>? Children { get; set; }
}

[JsonSerializable(typeof(DockNodeDto))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class DockNodeContext : JsonSerializerContext { }

public static class DockStateSerializer
{
    public static string Serialize(IDockable root)
    {
        return JsonSerializer.Serialize(BuildDto(root), DockNodeContext.Default.DockNodeDto);
    }

    public static bool Restore(IDock root, IFactory factory, IReadOnlyDictionary<string, IDockable> registry, string json)
    {
        try {
            var dto = JsonSerializer.Deserialize(json, DockNodeContext.Default.DockNodeDto);
            if (dto?.Children == null || dto.Children.Count == 0)
                return false;

            var newChildren = BuildChildren(dto.Children, factory, registry);
            if (newChildren.Count == 0)
                return false;

            root.VisibleDockables = factory.CreateList<IDockable>(newChildren.ToArray());
            root.DefaultDockable = newChildren.FirstOrDefault(c => c is IDock);
            root.ActiveDockable = root.DefaultDockable;

            factory.InitLayout(root);
            return true;
        } catch {
            return false;
        }
    }

    private static DockNodeDto BuildDto(IDockable dockable)
    {
        List<DockNodeDto>? children = null;
        string? activeDockableId = null;
        Alignment? alignment = null;
        Orientation? orientation = null;

        if (dockable is IDock dock) {
            activeDockableId = dock.ActiveDockable?.Id;
            if (dock.VisibleDockables?.Count > 0) {
                children = new List<DockNodeDto>(dock.VisibleDockables.Count);
                foreach (var child in dock.VisibleDockables)
                    children.Add(BuildDto(child));
            }
        }

        if (dockable is ToolDock toolDock)
            alignment = toolDock.Alignment;

        if (dockable is ProportionalDock propDock)
            orientation = propDock.Orientation;

        return new DockNodeDto {
            Id = string.IsNullOrEmpty(dockable.Id) ? null : dockable.Id,
            TypeName = dockable.GetType().Name,
            Proportion = double.IsNaN(dockable.Proportion) ? null : dockable.Proportion,
            ActiveDockableId = activeDockableId,
            Alignment = alignment,
            Orientation = orientation,
            Children = children
        };
    }

    private static List<IDockable> BuildChildren(List<DockNodeDto> childDtos, IFactory factory, IReadOnlyDictionary<string, IDockable> registry)
    {
        var result = new List<IDockable>(childDtos.Count);
        foreach (var dto in childDtos) {
            var node = BuildNode(dto, factory, registry);
            if (node != null)
                result.Add(node);
        }
        RemoveDanglingSplitters(result);
        return result;
    }

    private static IDockable? BuildNode(DockNodeDto dto, IFactory factory, IReadOnlyDictionary<string, IDockable> registry)
    {
        // custom
        if (dto.Id != null && registry.TryGetValue(dto.Id, out var leaf)) {
            if (dto.Proportion.HasValue)
                leaf.Proportion = dto.Proportion.Value;
            return leaf;
        }

        DockBase dock;
        List<IDockable> children;

        // built-in
        switch (dto.TypeName) {
            case nameof(ProportionalDockSplitter):
                return new ProportionalDockSplitter {
                    Proportion = dto.Proportion ?? double.NaN
                };
            case nameof(ProportionalDock):
                dock = new ProportionalDock {
                    Id = dto.Id!,
                    Proportion = dto.Proportion ?? double.NaN,
                    Orientation = dto.Orientation ?? Orientation.Horizontal,
                    DockCapabilityPolicy = new(),
                    DockCapabilityOverrides = new()
                };
                if (dto.Children != null) {
                    children = BuildChildren(dto.Children, factory, registry);
                    dock.VisibleDockables = factory.CreateList<IDockable>(children.ToArray());
                    dock.ActiveDockable = dto.ActiveDockableId != null
                        ? children.FirstOrDefault(d => d.Id == dto.ActiveDockableId)
                        : null;
                }
                return dock;
            case nameof(ToolDock):
                children = dto.Children != null
                    ? BuildChildren(dto.Children, factory, registry)
                    : [];
                if (children.Count == 0)
                    return null; // Skip empty tool docks — adjacent splitter cleaned by caller
                dock = new ToolDock {
                    Id = dto.Id!,
                    Proportion = dto.Proportion ?? double.NaN,
                    Alignment = dto.Alignment ?? Alignment.Unset,
                    DockCapabilityPolicy = new(),
                    DockCapabilityOverrides = new()
                };
                dock.VisibleDockables = factory.CreateList<IDockable>(children.ToArray());
                dock.ActiveDockable = dto.ActiveDockableId != null
                    ? children.FirstOrDefault(d => d.Id == dto.ActiveDockableId)
                    : children.FirstOrDefault();
                return dock;
            case nameof(DocumentDock):
                children = dto.Children != null
                    ? BuildChildren(dto.Children, factory, registry)
                    : [];
                if (children.Count == 0)
                    return null;
                dock = new DocumentDock {
                    Id = dto.Id!,
                    Proportion = dto.Proportion ?? double.NaN,
                    DockCapabilityPolicy = new(),
                    DockCapabilityOverrides = new()
                };
                dock.VisibleDockables = factory.CreateList<IDockable>(children.ToArray());
                dock.ActiveDockable = dto.ActiveDockableId != null
                    ? children.FirstOrDefault(d => d.Id == dto.ActiveDockableId)
                    : children.FirstOrDefault();
                return dock;
            default:
                return null;
        }
    }

    private static void RemoveDanglingSplitters(List<IDockable> children)
    {
        while (children.Count > 0 && children[0] is ProportionalDockSplitter)
            children.RemoveAt(0);
        while (children.Count > 0 && children[^1] is ProportionalDockSplitter)
            children.RemoveAt(children.Count - 1);
        for (int i = children.Count - 2; i >= 0; i--) {
            if (children[i] is ProportionalDockSplitter && children[i + 1] is ProportionalDockSplitter)
                children.RemoveAt(i + 1);
        }
    }
}
