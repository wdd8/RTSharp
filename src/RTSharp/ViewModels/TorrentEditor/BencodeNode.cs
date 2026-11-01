using BencodeNET.Objects;

using CommunityToolkit.Mvvm.ComponentModel;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Unicode;

namespace RTSharp.ViewModels.TorrentEditor;

public enum BencodeKind
{
    Dictionary,
    List,
    Integer,
    String
}

public partial class BencodeNode : ObservableObject
{
    public const int LARGE_STRING_BYTES = 4096;

    public BencodeNode? Parent { get; private set; }

    /// <summary>
    /// Dictionary key, or index in a list
    /// </summary>
    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContainer))]
    [NotifyPropertyChangedFor(nameof(IsString))]
    [NotifyPropertyChangedFor(nameof(IsTextEditable))]
    public partial BencodeKind Kind { get; set; }

    /// <summary>
    /// Integer or string text
    /// </summary>
    [ObservableProperty]
    public partial string Value { get; set; } = "";

    /// <summary>
    /// Set for a string that is not valid UTF-8, which <see cref="Value" /> holds as hex
    /// </summary>
    [ObservableProperty]
    public partial bool IsHex { get; set; }

    /// <summary>Set for a string too large to edit inline</summary>
    public byte[]? LargeBytes { get; private set; }

    public bool IsLarge => LargeBytes != null;

    public bool IsContainer => Kind is BencodeKind.Dictionary or BencodeKind.List;

    public bool IsString => Kind == BencodeKind.String;

    /// <summary>Whether <see cref="Value" /> is edited inline, as opposed to a container or large string</summary>
    public bool IsTextEditable => Kind == BencodeKind.Integer || (Kind == BencodeKind.String && !IsLarge);

    public ObservableCollection<BencodeNode> Children { get; } = [];

    // Dictionary keys may be binary (v2 "piece layers"), so an unedited key is written back as it was read
    private BString? OriginalKey;
    private string? OriginalKeyText;
    private bool Loading;

    public static BencodeNode FromBObject(BString? Key, string KeyText, IBObject Value, BencodeNode? Parent)
    {
        var node = new BencodeNode {
            Parent = Parent,
            Key = KeyText,
            OriginalKey = Key,
            OriginalKeyText = KeyText,
            Loading = true
        };

        switch (Value) {
            case BDictionary dict:
                node.Kind = BencodeKind.Dictionary;
                foreach (var (key, value) in dict) {
                    var bytes = key.Value.Span;
                    node.Children.Add(FromBObject(key, Utf8.IsValid(bytes) ? key.ToString() : "0x" + Convert.ToHexString(bytes), value, node));
                }
                break;
            case BList list:
                node.Kind = BencodeKind.List;
                for (var x = 0;x < list.Count;x++)
                    node.Children.Add(FromBObject(null, x.ToString(), list[x], node));
                break;
            case BNumber number:
                node.Kind = BencodeKind.Integer;
                node.Value = number.Value.ToString();
                break;
            case BString str:
                node.Kind = BencodeKind.String;
                if (str.Length > LARGE_STRING_BYTES) {
                    node.LargeBytes = str.Value.ToArray();
                    node.IsHex = true;
                    node.Value = $"<{str.Length} bytes>";
                } else {
                    node.IsHex = !Utf8.IsValid(str.Value.Span);
                    node.Value = node.IsHex ? Convert.ToHexString(str.Value.Span) : str.ToString();
                }
                break;
        }

        node.Loading = false;
        return node;
    }

    /// <exception cref="InvalidDataException">Invalid integer, hex or duplicate key</exception>
    public IBObject ToBObject()
    {
        switch (Kind) {
            case BencodeKind.Dictionary:
                var dict = new BDictionary();
                foreach (var child in Children) {
                    var key = child.OriginalKey != null && child.Key == child.OriginalKeyText ? child.OriginalKey : new BString(child.Key);
                    if (dict.ContainsKey(key))
                        throw new InvalidDataException($"Duplicate key \"{child.Key}\" in \"{Key}\"");

                    dict[key] = child.ToBObject();
                }
                return dict;
            case BencodeKind.List:
                return new BList(Children.Select(c => c.ToBObject()));
            case BencodeKind.Integer:
                return long.TryParse(Value, out var number) ? new BNumber(number) : throw new InvalidDataException($"\"{Key}\" is not a valid integer");
            default:
                if (LargeBytes != null)
                    return new BString(LargeBytes);

                try {
                    return new BString(IsHex ? Convert.FromHexString(Value) : Encoding.UTF8.GetBytes(Value));
                } catch (FormatException) {
                    throw new InvalidDataException($"\"{Key}\" is not valid hex");
                }
        }
    }

    public void SetBytes(byte[] Bytes)
    {
        if (Bytes.Length > LARGE_STRING_BYTES) {
            LargeBytes = Bytes;
            IsHex = true;
            Value = $"<{Bytes.Length} bytes>";
        } else {
            LargeBytes = null;
            IsHex = !Utf8.IsValid(Bytes);
            Value = IsHex ? Convert.ToHexString(Bytes) : Encoding.UTF8.GetString(Bytes);
        }

        OnPropertyChanged(nameof(IsLarge));
        OnPropertyChanged(nameof(IsTextEditable));
    }

    partial void OnKindChanged(BencodeKind value)
    {
        if (Loading)
            return;

        Children.Clear();
        LargeBytes = null;
        OnPropertyChanged(nameof(IsLarge));
        IsHex = false;
        Value = value == BencodeKind.Integer ? "0" : "";
    }
}
