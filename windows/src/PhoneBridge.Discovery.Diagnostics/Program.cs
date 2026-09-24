using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhoneBridge.Discovery;
using PhoneBridge.Discovery.Windows;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(args.Contains("--en-US") ? "en-US" : "zh-CN");
var strings = new ResourceManager("PhoneBridge.Discovery.Diagnostics.Resources.Strings", typeof(Program).Assembly);
string Text(string key) => strings.GetString(key) ?? key;
var seconds = 60;
var json = args.Contains("--json");
var manual = new List<DeviceEndpoint>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--json" or "--en-US") continue;
    if (args[i] == "--seconds" && i + 1 < args.Length && int.TryParse(args[++i], out seconds) && seconds is >= 1 and <= 300) continue;
    if (args[i] == "--manual" && i + 2 < args.Length && int.TryParse(args[i + 2], out var port) &&
        CandidateParser.TryManual(args[i + 1], port, out var candidate))
    { manual.Add(candidate!.Endpoints[0]); i += 2; continue; }
    Console.Error.WriteLine(Text("Usage"));
    return 2;
}
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
Console.CancelKeyPress += cancel;
Console.Error.WriteLine(Text("Notice"));
try
{
    await new DiscoveryService().RunAsync(change =>
    {
        if (json) Console.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, change }, options));
        else Console.WriteLine($"{Text(change.Kind.ToString())} {change.Candidate?.DisplayName ?? change.Id} " +
            $"{string.Join(", ", change.Candidate?.Endpoints.Select(e => e.HttpsAddress) ?? [])} [{change.Reason}]");
    }, stop.Token, manual);
    return 0;
}
catch (Exception error)
{
    // Only type/HResult; exception messages can contain untrusted network data.
    Console.Error.WriteLine($"{Text("Failed")}: {error.GetType().Name} (0x{error.HResult:X8})");
    return 1;
}
finally { Console.CancelKeyPress -= cancel; }
