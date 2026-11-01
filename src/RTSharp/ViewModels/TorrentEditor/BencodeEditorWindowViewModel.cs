using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;

using BencodeNET.Objects;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentListing;

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.TorrentEditor;

public partial class BencodeEditorWindowViewModel : ObservableViewModel
{
    private readonly BencodeNode root;

    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; } = [];

    public HierarchicalModel<BencodeNode> Model { get; }

    [ObservableProperty]
    public partial BencodeNode? SelectedNode { get; set; }

    /// <summary>
    /// Returns the edited bytes, or <c>null</c> to discard
    /// </summary>
    public Func<byte[], Task<byte[]?>> ShowBytesEditor { get; set; } = null!; // view set

    /// <summary>
    /// Closes with the edited root, or <c>null</c> to discard
    /// </summary>
    public Action<BDictionary?> CloseWindow { get; set; } = null!; // view set

    public BencodeEditorWindowViewModel(BDictionary Root)
    {
        root = BencodeNode.FromBObject(null, "root", Root, null);

        Model = new HierarchicalModel<BencodeNode>(new HierarchicalOptions<BencodeNode> {
            ChildrenSelector = x => x.Children,
            IsLeafSelector = x => !x.IsContainer,
            AutoExpandRoot = true,
            MaxAutoExpandDepth = 1,
            VirtualizeChildren = true
        });
        Model.SetRoot(root);

        ColumnDefinitions.AddRange([
            new DataGridHierarchicalColumnDefinition {
                Header = "Key",
                Binding = ColumnDefinitionBindingFactory.CreateBinding<HierarchicalNode, BencodeNode>("Item", node => (BencodeNode)node.Item),
                CellTemplateKey = "KeyTemplate",
                ColumnKey = nameof(BencodeNode.Key),
                Width = new DataGridLength(250)
            },
            new DataGridTemplateColumnDefinition {
                Header = "Type",
                CellTemplateKey = "KindTemplate",
                ColumnKey = nameof(BencodeNode.Kind),
                Width = new DataGridLength(110)
            },
            new DataGridTemplateColumnDefinition {
                Header = "Value",
                CellTemplateKey = "ValueTemplate",
                ColumnKey = nameof(BencodeNode.Value),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 150
            }
        ]);
    }

    [RelayCommand]
    public void AddClick(IList In)
    {
        if (In.Cast<BencodeNode>().FirstOrDefault() is not { } selected)
            return;

        var parent = selected.IsContainer ? selected : selected.Parent!;

        var key = parent.Kind == BencodeKind.List ? parent.Children.Count.ToString() : "new key";
        for (var x = 2;parent.Children.Any(c => c.Key == key);x++)
            key = "new key " + x;

        parent.Children.Add(BencodeNode.FromBObject(null, key, new BString(""), parent));
    }

    [RelayCommand]
    public void DeleteClick(IList In)
    {
        foreach (var node in In.Cast<BencodeNode>().Where(x => x.Parent != null).ToArray()) {
            var parent = node.Parent!;
            parent.Children.Remove(node);

            if (parent.Kind == BencodeKind.List) {
                for (var x = 0;x < parent.Children.Count;x++)
                    parent.Children[x].Key = x.ToString();
            }
        }
    }

    [RelayCommand]
    public async Task DuplicateClick(IList In)
    {
        if (In.Cast<BencodeNode>().FirstOrDefault() is not { Parent: { } parent } node)
            return;

        IBObject value;
        try {
            value = node.ToBObject();
        } catch (InvalidDataException ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Edit bencode", ex.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
            return;
        }

        var key = node.Key + " copy";
        while (parent.Kind == BencodeKind.Dictionary && parent.Children.Any(c => c.Key == key))
            key += "_";

        parent.Children.Insert(parent.Children.IndexOf(node) + 1, BencodeNode.FromBObject(null, key, value, parent));

        if (parent.Kind == BencodeKind.List) {
            for (var x = 0;x < parent.Children.Count;x++)
                parent.Children[x].Key = x.ToString();
        }
    }

    [RelayCommand]
    public void MoveUpClick(IList In)
    {
        if (In.Cast<BencodeNode>().FirstOrDefault() is not { Parent: { Kind: BencodeKind.List } parent } node)
            return;

        var index = parent.Children.IndexOf(node);
        if (index == 0)
            return;

        parent.Children.Move(index, index - 1);
        parent.Children[index].Key = index.ToString();
        parent.Children[index - 1].Key = (index - 1).ToString();
    }

    [RelayCommand]
    public void MoveDownClick(IList In)
    {
        if (In.Cast<BencodeNode>().FirstOrDefault() is not { Parent: { Kind: BencodeKind.List } parent } node)
            return;

        var index = parent.Children.IndexOf(node);
        if (index == parent.Children.Count - 1)
            return;

        parent.Children.Move(index, index + 1);
        parent.Children[index].Key = index.ToString();
        parent.Children[index + 1].Key = (index + 1).ToString();
    }

    [RelayCommand]
    public async Task EditBytesClick(BencodeNode? node)
    {
        if (node is not { Kind: BencodeKind.String })
            return;

        byte[] bytes;
        try {
            bytes = ((BString)node.ToBObject()).Value.ToArray();
        } catch (InvalidDataException ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Edit bencode", ex.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
            return;
        }

        if (await ShowBytesEditor(bytes) is { } edited)
            node.SetBytes(edited);
    }

    [RelayCommand]
    public async Task OkClick()
    {
        try {
            CloseWindow((BDictionary)root.ToBObject());
        } catch (InvalidDataException ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Edit bencode", ex.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
        }
    }

    [RelayCommand]
    private void CancelClick()
    {
        CloseWindow(null);
    }
}
