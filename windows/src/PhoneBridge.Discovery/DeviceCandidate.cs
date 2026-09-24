using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PhoneBridge.Discovery;

public enum CandidateSource { Mdns, Manual }
public enum CandidateProtocol { Manual, ExperimentalV2, PairedV3 }
public sealed record PairingAdvertisement(int Port, string Window);

// A discovery key identifies an advertisement, NEVER a paired cryptographic identity.
public sealed record DeviceCandidate(string Id, CandidateSource Source, string DisplayName,
    ImmutableArray<DeviceEndpoint> Endpoints)
{
    public bool IsAuthenticated => false;
    public CandidateProtocol Protocol { get; init; }
    public string? DeviceIdHint { get; init; }
    public PairingAdvertisement? Pairing { get; init; }
}

public sealed record DeviceEndpoint(string Address, int Port)
{
    public string HttpsAddress => Address.Contains(':')
        ? $"https://[{Address}]:{Port}/" : $"https://{Address}:{Port}/";
}

public sealed record ServiceAdvertisement(string ServiceKey, string InstanceName,
    string ServiceType, string Domain, int Port, IReadOnlyList<string> Addresses,
    IReadOnlyList<string> TextAttributes);

public static class CandidateParser
{
    public const string ServiceType = "_phonebridge._tcp";
    public const int MaxCandidates = 256;

    public static string Key(CandidateSource source, string value) =>
        $"{source.ToString().ToLowerInvariant()}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    public static bool TryParse(ServiceAdvertisement advertisement, out DeviceCandidate? candidate,
        out string reason)
    {
        candidate = null;
        reason = "invalid-advertisement";
        if (!SafeText(advertisement.ServiceKey, 1024) ||
            !SafeText(advertisement.InstanceName, 255) ||
            !string.Equals(advertisement.ServiceType.TrimEnd('.'), ServiceType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(advertisement.Domain.TrimEnd('.'), "local", StringComparison.OrdinalIgnoreCase)) return false;

        if (advertisement.TextAttributes.Count is 0 or > 32) { reason = "invalid-txt"; return false; }
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var text in advertisement.TextAttributes)
        {
            if (!SafeText(text, 255)) { reason = "invalid-txt"; return false; }
            var bytes = Encoding.UTF8.GetByteCount(text);
            total += bytes;
            var split = text.IndexOf('=');
            if (bytes > 255 || total > 4096 || split is <= 0 or > 63 ||
                text[..split].Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) ||
                !properties.TryAdd(text[..split], text[(split + 1)..]))
            { reason = "invalid-txt"; return false; }
        }
        if (properties.GetValueOrDefault("protocol") != "https") { reason = "https-required"; return false; }
        var version = properties.GetValueOrDefault("version");
        if (version is not ("2" or "3")) { reason = "unsupported-version"; return false; }
        string? deviceId = null;
        PairingAdvertisement? pairing = null;
        if (version == "3")
        {
            deviceId = properties.GetValueOrDefault("device_id");
            if (properties.GetValueOrDefault("auth") != "paired-v1" || deviceId is null ||
                !deviceId.StartsWith("pbng-", StringComparison.Ordinal) || !Hex(deviceId[5..], 64))
            { reason = "invalid-v3-identity"; return false; }
            if (new[] { "pairing", "pair_port", "pair_window" }.Any(properties.ContainsKey))
            {
                string? port = properties.GetValueOrDefault("pair_port"), window = properties.GetValueOrDefault("pair_window");
                if (properties.GetValueOrDefault("pairing") != "jpake1" || port is null ||
                    !int.TryParse(port, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) ||
                    value is < 1 or > 65535 || port != value.ToString(System.Globalization.CultureInfo.InvariantCulture) || !Hex(window, 32))
                { reason = "invalid-pairing-window"; return false; }
                pairing = new(value, window!);
            }
        }
        var name = properties.GetValueOrDefault("deviceName", advertisement.InstanceName);
        if (!SafeText(name, 128)) { reason = "invalid-name"; return false; }
        if (!TryEndpoints(advertisement.Addresses, advertisement.Port, out var endpoints))
        { reason = "invalid-endpoint"; return false; }
        candidate = new(Key(CandidateSource.Mdns, advertisement.ServiceKey), CandidateSource.Mdns, name, endpoints)
        {
            Protocol = version == "3" ? CandidateProtocol.PairedV3 : CandidateProtocol.ExperimentalV2,
            DeviceIdHint = deviceId, Pairing = pairing
        };
        reason = "accepted";
        return true;
    }

    public static bool TryManual(string address, int port, out DeviceCandidate? candidate)
    {
        candidate = null;
        if (!TryEndpoints([address], port, out var endpoints)) return false;
        var endpoint = endpoints[0];
        candidate = new(Key(CandidateSource.Manual, endpoint.HttpsAddress), CandidateSource.Manual,
            endpoint.Address, endpoints);
        return true;
    }

    private static bool TryEndpoints(IReadOnlyList<string> addresses, int port,
        out ImmutableArray<DeviceEndpoint> endpoints)
    {
        endpoints = [];
        if (port is < 1 or > 65535 || addresses.Count is 0 or > 16) return false;
        var valid = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var input in addresses)
        {
            if (input.Length > 80 || input != input.Trim() || !IPAddress.TryParse(input, out var ip)) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
                ip.IsIPv6Multicast || (ip.IsIPv6LinkLocal && ip.ScopeId == 0)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPAddress.TryParse accepts integer/octal/abbreviated IPv4; require dotted decimal input.
                if (!input.Contains(':') && input != ip.ToString()) return false;
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 0 || bytes[0] >= 224) return false;
            }
            valid.Add(ip.ToString());
        }
        endpoints = [.. valid.Select(a => new DeviceEndpoint(a, port))];
        return endpoints.Length > 0;
    }

    private static bool Hex(string? value, int length) => value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool SafeText(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(c => char.IsControl(c) ||
            char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}
