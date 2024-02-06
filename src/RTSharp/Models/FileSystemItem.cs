using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Models
{
    public class NameWithIcon
    {
        public Geometry Icon { get; set; }

        public string Name {
            get => Item.Path == "/" || Item.Path == "\\" ? Item.Path : Path.GetFileName(Item.Path);
            set {
                Item.Path = String.Join('/', Item.Path.Split('/')[..^1].Append(value));
            }
        }

        private FileSystemItem Item;

        public NameWithIcon(FileSystemItem Item, bool IsDirectory)
        {
            this.Item = Item;

            if (IsDirectory)
                Icon = Core.FontAwesomeIcons.Get("fa-regular fa-folder");
            else
                Icon = Core.FontAwesomeIcons.Get("fa-regular fa-file");
        }
    }

    public partial class FileSystemItem : ObservableObject
    {
        public FileSystemItem(string Path, bool IsDirectory, ulong? Size, DateTime? LastModified, string Permissions)
        {
            this.IsDirectory = IsDirectory;
            this.Path = Path;
            this.Display = new NameWithIcon(this, IsDirectory);
            this.Size = Size;
            this.LastModified = LastModified;
            this.Permissions = Permissions;
        }

        public FileSystemItem(Shared.Abstractions.FileSystemItem Input)
            : this(Input.Path, Input.Directory, Input.Size, Input.LastModified, Input.Permissions)
        {
        }

        public ObservableCollection<FileSystemItem>? Children { get; }

        public bool IsDirectory { get; init; }
        public NameWithIcon Display { get; init; }
        public string Path { get; set; }
        public ulong? Size { get; init; }
        public DateTime? LastModified { get; init; }
        public string Permissions { get; init; }
    }

    public class FileSystemItemEqualityComparer : IEqualityComparer<FileSystemItem>
    {
        public bool Equals(FileSystemItem x, FileSystemItem y)
        {
            return x.Path == y.Path && x.IsDirectory == y.IsDirectory && x.Size == y.Size && x.LastModified == y.LastModified && x.Permissions == y.Permissions;
        }

        public int GetHashCode([DisallowNull] FileSystemItem obj)
        {
            return HashCode.Combine(obj.Path, obj.IsDirectory, obj.Size, obj.LastModified, obj.Permissions);
        }
    }
}
