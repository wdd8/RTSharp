namespace RTSharp.Shared.Abstractions
{
    public class Version
    {
        public string VersionDisplayString { get; }

        public uint Major { get; }
        public uint Minor { get; }
        public uint Patch { get; }
        public string PreRelease { get; }
        public string BuildMetadata { get; }

        public Version(string VersionDisplayString, uint Major, uint Minor, uint Patch, string PreRelease = null, string BuildMetadata = null)
        {
            this.VersionDisplayString = VersionDisplayString;
            this.Major = Major;
            this.Minor = Minor;
            this.Patch = Patch;
            this.PreRelease = PreRelease;
            this.BuildMetadata = BuildMetadata;
        }
    }
}
