namespace RTSharp.Shared.Abstractions;

public class FileSystemItem
{
    public FileSystemItem(IList<FileSystemItem>? Children, bool Directory, string Path, ulong? Size, DateTime LastModified, string Permissions, string Owner, string Group, string? LinkTarget)
    {
        this.Children = Children;
        this.Directory = Directory;
        this.Path = Path;
        this.Size = Size;
        this.LastModified = LastModified;
        this.Permissions = Permissions;
        this.Owner = Owner;
        this.Group = Group;
        this.LinkTarget = LinkTarget;
    }

    public IList<FileSystemItem>? Children { get; set; }
    public bool Directory { get; init; }
    public string Path { get; init; }
    public ulong? Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Permissions { get; init; }
    public string Owner { get; init; }
    public string Group { get; init; }
    public string? LinkTarget { get; init; }
}
