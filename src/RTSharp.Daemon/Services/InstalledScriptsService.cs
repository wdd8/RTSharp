using Microsoft.Extensions.Options;

using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Shared.Abstractions;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using File = System.IO.File;

namespace RTSharp.Daemon.Services;

public record InstalledScriptInfo(
    string Name,
    string OwnerId,
    Dictionary<string, string> Variables,
    DateTimeOffset InstalledAt,
    bool OrphanAcknowledged
);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstalledScriptInfo))]
internal partial class InstalledScriptInfoContext : JsonSerializerContext { }

public record InstalledScript(string Id, InstalledScriptInfo Info, byte[] ScriptHash, ScriptSession? Session);

public class InstalledScriptsService(
    SessionsService Sessions,
    IOptions<ServerOptions> Options,
    ILogger<InstalledScriptsService> Logger) : IHostedService
{
    private const string INFO_JSON = "info.json";
    private const string SCRIPT_CS = "script.cs";
    private static readonly TimeSpan STOP_TIMEOUT = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> IdLocks = new(StringComparer.Ordinal);

    private string StorePath => String.IsNullOrEmpty(Options.Value.ScriptStorePath) ? DefaultStorePath() : Options.Value.ScriptStorePath;

    private static string DefaultStorePath()
    {
        var stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return Path.Combine(String.IsNullOrEmpty(stateDirectory) ? AppContext.BaseDirectory : stateDirectory, "scripts");
    }

    public async Task StartAsync(CancellationToken CancellationToken)
    {
        var store = StorePath;

        if (!Directory.Exists(store)) {
            Logger.LogDebug($"No installed script store at {store}");
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(store)) {
            var id = Path.GetFileName(dir);
            InstalledScriptInfo? info = null;

            try {
                info = await ReadInfo(id, CancellationToken);
                var source = await File.ReadAllTextAsync(Path.Combine(dir, SCRIPT_CS), CancellationToken);

                var compilation = await DynamicCompilation.Compile<IScript>(source, info.Name);
                var session = Sessions.RunScript(info.Name, compilation, info.Variables, id);

                Logger.LogInformation($"Installed script {id} ({info.Name}) is running as session {session.Id}");
            } catch (Exception ex) {
                Logger.LogError(ex, $"Installed script {id} could not be started");

                Sessions.RunSession(info?.Name ?? id, new CancellationTokenSource(), _ => Task.FromException(ex), id);
            }
        }
    }

    public Task StopAsync(CancellationToken CancellationToken)
    {
        foreach (var session in Sessions.GetInstalledScriptSessions().Values)
            session.Cts.Cancel();

        return Task.CompletedTask;
    }

    public async Task<InstalledScript> Install(string Id, string Name, string OwnerId, string Source, Dictionary<string, string> Variables)
    {
        if (String.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Installed script id must not be empty", nameof(Id));
        if (Id.AsSpan().ContainsAny([..Path.GetInvalidFileNameChars(), Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) || Id is "." or "..")
            throw new ArgumentException($"Installed script id {Id} is not usable as a directory name", nameof(Id));

        var compilation = await DynamicCompilation.Compile<IScript>(Source, Name);

        var idLock = IdLocks.GetOrAdd(Id, _ => new SemaphoreSlim(1, 1));
        await idLock.WaitAsync(); try {
            InstalledScriptInfo info;

            try {
                var dir = Path.Combine(StorePath, Id);
                var previous = Directory.Exists(dir) ? await ReadInfo(Id, CancellationToken.None) : null;

                if (Sessions.GetInstalledScriptSessions().TryGetValue(Id, out var running))
                    await Stop(Id, running);

                info = new InstalledScriptInfo(
                    Name: Name,
                    OwnerId: OwnerId,
                    Variables: Variables,
                    InstalledAt: previous?.InstalledAt ?? DateTimeOffset.UtcNow,
                    OrphanAcknowledged: false
                );

                Directory.CreateDirectory(dir);
                await WriteAtomic(Path.Combine(dir, SCRIPT_CS), Source);
                await WriteAtomic(Path.Combine(dir, INFO_JSON), JsonSerializer.Serialize(info, InstalledScriptInfoContext.Default.InstalledScriptInfo));
            } catch {
                compilation.RemoveRef();
                throw;
            }

            var session = Sessions.RunScript(Name, compilation, Variables, Id);

            Logger.LogInformation($"Installed script {Id} ({Name}) is running as session {session.Id}");

            return new InstalledScript(Id, info, SHA256.HashData(Encoding.UTF8.GetBytes(Source)), session);
        } finally {
            idLock.Release();
        }
    }

    public async Task Uninstall(string Id)
    {
        var idLock = IdLocks.GetOrAdd(Id, _ => new SemaphoreSlim(1, 1));
        await idLock.WaitAsync();
        try {
            var dir = Path.Combine(StorePath, Id);
            if (!Directory.Exists(dir))
                throw new KeyNotFoundException($"No installed script {Id}");

            Directory.Delete(dir, recursive: true);

            if (Sessions.GetInstalledScriptSessions().TryGetValue(Id, out var running))
                await Stop(Id, running);

            Logger.LogInformation($"Installed script {Id} uninstalled");
        } finally {
            idLock.Release();
        }
    }

    public async Task<InstalledScript> AcknowledgeOrphaned(string Id)
    {
        var idLock = IdLocks.GetOrAdd(Id, _ => new SemaphoreSlim(1, 1));
        await idLock.WaitAsync();
        try {
            var info = await ReadInfo(Id, CancellationToken.None) with { OrphanAcknowledged = true };

            await WriteAtomic(Path.Combine(StorePath, Id, INFO_JSON), JsonSerializer.Serialize(info, InstalledScriptInfoContext.Default.InstalledScriptInfo));

            return await Read(Id, info, Sessions.GetInstalledScriptSessions(), CancellationToken.None);
        } finally {
            idLock.Release();
        }
    }

    public async Task<IReadOnlyList<InstalledScript>> List(CancellationToken CancellationToken)
    {
        var store = StorePath;
        if (!Directory.Exists(store))
            return [];

        var sessions = Sessions.GetInstalledScriptSessions();
        var ret = new List<InstalledScript>();

        foreach (var dir in Directory.EnumerateDirectories(store)) {
            var id = Path.GetFileName(dir);

            try {
                ret.Add(await Read(id, await ReadInfo(id, CancellationToken), sessions, CancellationToken));
            } catch (Exception ex) {
                Logger.LogError(ex, $"Installed script {id} could not be read");
            }
        }

        return ret;
    }

    private async Task<InstalledScript> Read(string Id, InstalledScriptInfo Info, Dictionary<string, ScriptSession> SessionsById, CancellationToken CancellationToken)
    {
        var source = await File.ReadAllBytesAsync(Path.Combine(StorePath, Id, SCRIPT_CS), CancellationToken);

        return new InstalledScript(Id, Info, SHA256.HashData(source), SessionsById.GetValueOrDefault(Id));
    }

    private async Task<InstalledScriptInfo> ReadInfo(string Id, CancellationToken CancellationToken)
    {
        var path = Path.Combine(StorePath, Id, INFO_JSON);

        return JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, CancellationToken), InstalledScriptInfoContext.Default.InstalledScriptInfo)
            ?? throw new InvalidDataException($"{path} is empty");
    }

    private async Task Stop(string Id, ScriptSession Session)
    {
        Session.Cts.Cancel();

        if (await Task.WhenAny(Session.Execution!, Task.Delay(STOP_TIMEOUT)) != Session.Execution) {
            Logger.LogWarning($"Installed script {Id} did not stop within {STOP_TIMEOUT}");
            return;
        }

        Sessions.RemoveCompletedScriptSession(Session.Id);
    }

    private static async Task WriteAtomic(string Path, string Contents)
    {
        var tmp = Path + ".tmp";
        await File.WriteAllTextAsync(tmp, Contents);
        File.Move(tmp, Path, overwrite: true);
    }
}
