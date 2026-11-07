using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Core.Services;

namespace RTSharp.Models
{
    public class NameWithIcon
    {
        public Task<Bitmap?> Icon => FileIconService.GetIconAsync(Name, Item.IsDirectory);

        public string Name {
            get => Item.Path == "/" || Item.Path == "\\" ? Item.Path : Path.GetFileName(Item.Path);
            set {
                Item.Path = String.Join('/', Item.Path.Split('/')[..^1].Append(value));
            }
        }

        private FileSystemItem Item;

        public NameWithIcon(FileSystemItem Item)
        {
            this.Item = Item;
        }
    }

    public partial class FileSystemItem : ObservableObject
    {
        public FileSystemItem(string Path, bool IsDirectory, ulong? Size, DateTime? LastModified, string? Permissions, string? Owner, string? Group, string? LinkTarget)
        {
            this.IsDirectory = IsDirectory;
            this.Path = Path;
            this.Display = new NameWithIcon(this);
            this.Size = Size;
            this.LastModified = LastModified;
            this.Permissions = Permissions;
            this.PermissionBits = Permissions?.Select(x => x.ToString()).ToArray();
            this.Owner = Owner;
            this.Group = Group;
            this.LinkTarget = LinkTarget;
        }

        public FileSystemItem(Shared.Abstractions.FileSystemItem Input)
            : this(Input.Path, Input.Directory, Input.Size, Input.LastModified, Input.Permissions, Input.Owner, Input.Group, Input.LinkTarget)
        {
        }

        public ObservableCollection<FileSystemItem>? Children { get; }

        public bool IsDirectory { get; init; }
        public NameWithIcon Display { get; init; }
        public string Path { get; set; }
        public ulong? Size { get; init; }
        public DateTime? LastModified { get; init; }
        public string? Permissions { get; init; }
        public string[]? PermissionBits { get; }

        public string? Owner { get; init; }
        public string? Group { get; init; }

        public string? LinkTarget { get; init; }

        /// <summary>
        /// Torrents whose data is this item or contains it
        /// </summary>
        public IReadOnlyList<Torrent> Torrents { get; init; } = [];
    }

    public class FileSystemItemEqualityComparer : IEqualityComparer<FileSystemItem>
    {
        public bool Equals(FileSystemItem? x, FileSystemItem? y)
        {
            return x != null && y != null && x.Path == y.Path && x.IsDirectory == y.IsDirectory && x.Size == y.Size && x.LastModified == y.LastModified && x.Permissions == y.Permissions && x.Torrents.SequenceEqual(y.Torrents);
        }

        public int GetHashCode([DisallowNull] FileSystemItem obj)
        {
            return HashCode.Combine(obj.Path, obj.IsDirectory, obj.Size, obj.LastModified, obj.Permissions);
        }
    }
}
