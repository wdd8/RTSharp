namespace RTSharp.Shared.Abstractions;

public record AddTorrentsOptions(
    object? DataProviderOptions,
    string RemoteTargetPath,
    bool FastResume
);
