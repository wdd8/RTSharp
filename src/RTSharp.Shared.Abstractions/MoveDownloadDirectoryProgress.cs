using RTSharp.Shared.Utils;

namespace RTSharp.Shared.Abstractions;

public record MoveDownloadDirectoryProgress(byte[] InfoHash, string File, string Text, bool? Success)
    : IScriptMessage<MoveDownloadDirectoryProgress>
{
    public Dictionary<string, object?> ToMessage() => new() {
        [nameof(InfoHash)] = InfoHash,
        [nameof(File)] = File,
        [nameof(Text)] = Text,
        [nameof(Success)] = Success
    };

    public static MoveDownloadDirectoryProgress FromMessage(Dictionary<string, object?> Message) => new(
        (byte[])Message[nameof(InfoHash)]!,
        (string)Message[nameof(File)]!,
        (string)Message[nameof(Text)]!,
        Message[nameof(Success)] is null ? null : Convert.ToBoolean(Message[nameof(Success)])
    );
}
