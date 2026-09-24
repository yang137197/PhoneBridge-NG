using System.Globalization;
using PhoneBridge.Discovery;

namespace PhoneBridge.Desktop;

internal sealed class ManualEndpointSession
{
    internal const string DefaultPort = "8273";
    private readonly Dictionary<string, DeviceEndpoint> endpoints = new(StringComparer.Ordinal);

    internal bool TrySet(string deviceId, string address, string portText, out DeviceEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(deviceId) || !TryPort(portText, out int port) ||
            !CandidateParser.TryManual(address, port, out var candidate)) return false;
        endpoint = candidate!.Endpoints[0];
        endpoints[deviceId] = endpoint;
        return true;
    }

    internal bool TryGet(string deviceId, out DeviceEndpoint endpoint) =>
        endpoints.TryGetValue(deviceId, out endpoint!);

    internal bool Clear(string deviceId) => endpoints.Remove(deviceId);

    internal IReadOnlyList<DeviceEndpoint> Merge(string deviceId, IEnumerable<DeviceEndpoint> automatic)
    {
        var merged = automatic.Distinct().ToList();
        if (endpoints.TryGetValue(deviceId, out var manual) && !merged.Contains(manual)) merged.Add(manual);
        return merged;
    }

    internal DeviceEndpoint? SelectForReconnect(string deviceId, IEnumerable<DeviceCandidate> candidates)
    {
        var discovered = candidates.FirstOrDefault(candidate => candidate.Protocol == CandidateProtocol.PairedV3 &&
            string.Equals(candidate.DeviceIdHint, deviceId, StringComparison.Ordinal));
        return discovered?.Endpoints.OrderBy(endpoint => endpoint.Address.Contains(':')).FirstOrDefault() ??
            (endpoints.TryGetValue(deviceId, out var manual) ? manual : null);
    }

    private static bool TryPort(string value, out int port)
    {
        port = 0;
        if (string.IsNullOrEmpty(value) || value.Length > 5 || value[0] == '0' ||
            value.Any(c => c is < '0' or > '9')) return false;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
            port is >= 1 and <= 65535 && value == port.ToString(CultureInfo.InvariantCulture);
    }
}
