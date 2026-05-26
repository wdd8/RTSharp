using System;

namespace RTSharp.Core.Services.Database.TrackerDb;

public class TrackerInfo
{
    [Obsolete("For serialization only", true)]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public TrackerInfo()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
    }

    public TrackerInfo(string Domain, string? Name, byte[]? ImageHash)
    {
        this.Domain = Domain;
        this.Name = Name;
        this.ImageHash = ImageHash;
    }

    public string Domain { get; init; }
    public string? Name { get; set; }
    public byte[]? ImageHash { get; set; }

    public void Deconstruct(out string Domain, out string? Name, out byte[]? ImageHash)
    {
        Domain = this.Domain;
        Name = this.Name;
        ImageHash = this.ImageHash;
    }
}
