using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTSharp.Shared.Utils;

/// <summary>
/// Parses release names which are commonly used on .torrent folder names or single-file file names
/// </summary>
public static partial class ReleaseNameParser
{
    /// <param name="Type">Release type, such as "movie", "episode", "series" or "music"</param>
    /// <param name="Year">0 if none</param>
    /// <param name="Series">0 if none</param>
    /// <param name="Episode">0 if none</param>
    public record ReleaseName(string Type, string Artist, string Title, int Year, int Series, int Episode);

    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(ReleaseName))]
    private partial class ReleaseNameJsonContext : JsonSerializerContext;

    private static readonly SemaphoreSlim HelperLock = new(1, 1);
    private static Process? Helper;

    public static async Task<ReleaseName> Parse(string Name, CancellationToken CancellationToken = default)
    {
        await HelperLock.WaitAsync(CancellationToken);

        try {
            if (Helper is null or { HasExited: true }) {
                Helper?.Dispose();
                Helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "rls.exe" : "rls")) {
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8
                })!;
            }

            await Helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(Name, ReleaseNameJsonContext.Default.String));
            await Helper.StandardInput.FlushAsync();

            var response = await Helper.StandardOutput.ReadLineAsync() ?? throw new IOException("The rls helper exited");

            return JsonSerializer.Deserialize(response, ReleaseNameJsonContext.Default.ReleaseName)!;
        } finally {
            HelperLock.Release();
        }
    }
}
