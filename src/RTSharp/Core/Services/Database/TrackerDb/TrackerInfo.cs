using System;

namespace RTSharp.Core.Services.Cache.TrackerDb
{
    public class TrackerInfo
    {
        public TrackerInfo()
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
}
