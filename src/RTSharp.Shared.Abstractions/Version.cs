namespace RTSharp.Shared.Abstractions;

public record Version(string VersionDisplayString, uint Major, uint Minor, uint Patch, string? PreRelease = null, string? BuildMetadata = null);
