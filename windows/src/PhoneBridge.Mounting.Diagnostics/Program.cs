using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhoneBridge.Mounting;

// Machine-only, local experimental harness. Secrets arrive through stdin and are never printed or persisted.
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
void Emit(string kind, object result) => Console.WriteLine(JsonSerializer.Serialize(new { kind, result }, json));
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
await using var manager = new ReadOnlyMountManager();
try
{
    var input = await ReadBoundedLineAsync(lifetime.Token) ?? throw new MountException("input-missing");
    var config = JsonSerializer.Deserialize<Input>(input) ?? throw new MountException("input-missing");
    input = "";
    var request = new MountRequest(new ConfirmedIdentity(Convert.FromBase64String(config.Certificate), config.Fingerprint),
        new SessionCredentials(config.User, config.Password), config.Address, config.Port, config.Directory,
        config.Drive, config.Rclone, config.SessionRoot);
    Emit("mounted", await manager.StartAsync(request, lifetime.Token));
    if (config.Probe)
    {
        // A probe is restricted to the explicitly retained synthetic directory; never arbitrary phone data.
        if (config.Directory != "P0-010-84926bfb5cd4") throw new MountException("probe-directory-not-authorized");
        var root = $"{config.Drive}:\\";
        var names = Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order().ToArray();
        var text = await File.ReadAllBytesAsync(Path.Combine(root, "phone-origin.txt"), lifetime.Token);
        var blocked = false;
        int? writeError = null;
        var probe = Path.Combine(root, "readonly-rejected-" + Guid.NewGuid().ToString("N") + ".txt");
        try { using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write); }
        catch (UnauthorizedAccessException error) { writeError = error.HResult & 0xffff; blocked = writeError == 5; }
        catch (IOException error) { writeError = error.HResult & 0xffff; blocked = writeError is 5 or 19; }
        Emit("probe", new { names, bytes = text.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(text)), writeBlocked = blocked, writeError });
        if (!blocked) throw new MountException("readonly-probe-failed"); // preserve an unexpected new empty file as evidence
    }
    else
    {
        // EOF, a line, or the bounded lifetime ends this session. No implicit long-lived background mount.
        await ReadBoundedLineAsync(lifetime.Token);
    }
    Emit("stopped", await manager.StopAsync());
    return manager.Snapshot.State == MountState.Stopped ? 0 : 3;
}
catch (Exception error)
{
    Emit("error", new { code = error is MountException known ? known.Code : "diagnostic-failed" });
    Emit("stopped", await manager.StopAsync());
    return 2;
}

// Console.In's synchronized reader can block before returning its Async task.
// Move the bounded read off the caller and cancel the wait; process exit ends any pending pool read.
static Task<string?> ReadBoundedLineAsync(CancellationToken token) => Task.Run<string?>(() =>
{
    var line = new StringBuilder();
    while (line.Length < 32768)
    {
        var value = Console.In.Read();
        if (value < 0) return null;
        if (value == '\n') return line.ToString();
        line.Append((char)value);
    }
    throw new MountException("input-too-long");
}).WaitAsync(token);

internal sealed record Input(string Certificate, string Fingerprint, string User, string Password, string Address,
    int Port, string Directory, char Drive, string Rclone, string SessionRoot, bool Probe = false);
