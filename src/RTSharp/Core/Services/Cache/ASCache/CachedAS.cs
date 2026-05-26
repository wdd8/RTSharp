using System;

namespace RTSharp.Core.Services.Cache.ASCache
{
    public class CachedAS
    {
        [Obsolete("For serialization only", true)]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public CachedAS()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        {
        }

        public CachedAS(string? Domain, string? Organization, string? Country, byte[] ImageHash)
        {
            this.Domain = Domain;
            this.Organization = Organization;
            this.Country = Country;
            this.ImageHash = ImageHash;
        }

        public string? Domain { get; init; }
        public string? Organization { get; init; }
        public string? Country { get; set; }
        public byte[] ImageHash { get; init; }

        public void Deconstruct(out string? Domain, out string? Organization, out string? Country, out byte[] ImageHash)
        {
            Domain = this.Domain;
            Organization = this.Organization;
            Country = this.Country;
            ImageHash = this.ImageHash;
        }
    }
}
