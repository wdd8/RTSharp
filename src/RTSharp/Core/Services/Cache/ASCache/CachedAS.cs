namespace RTSharp.Core.Services.Cache.ASCache
{
    public class CachedAS
    {
        public CachedAS()
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
