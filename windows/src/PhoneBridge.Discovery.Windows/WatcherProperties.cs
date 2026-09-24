namespace PhoneBridge.Discovery.Windows;

public static class WatcherProperties
{
    private const string Prefix = "System.Devices.Dnssd.";
    public const string InstanceName = Prefix + "InstanceName";
    public const string ServiceName = Prefix + "ServiceName";
    public const string Domain = Prefix + "Domain";
    public const string Port = Prefix + "PortNumber";
    public const string Addresses = "System.Devices.IpAddress";
    public const string Text = Prefix + "TextAttributes";
    public static readonly string[] Requested = [InstanceName, ServiceName, Domain, Port, Addresses, Text];

    public static bool AreBounded(IReadOnlyDictionary<string, object> properties) => properties.All(p => p.Value switch
    {
        null => true, // a deleted/missing property makes the assembled advertisement incomplete
        string text => text.Length <= 1024,
        string[] list => list.Length <= 32 && list.All(v => v is not null && v.Length <= 255) &&
            list.Sum(v => System.Text.Encoding.UTF8.GetByteCount(v)) <= 4096,
        ushort or uint or int => true,
        _ => false
    });

    public static ServiceAdvertisement? Parse(string id, IReadOnlyDictionary<string, object> properties)
    {
        if (properties.GetValueOrDefault(InstanceName) is not string instance ||
            properties.GetValueOrDefault(ServiceName) is not string service ||
            properties.GetValueOrDefault(Domain) is not string domain ||
            properties.GetValueOrDefault(Addresses) is not string[] addresses ||
            properties.GetValueOrDefault(Text) is not string[] txt) return null;
        var port = properties.GetValueOrDefault(Port) switch
        {
            ushort number => number,
            uint number when number <= 65535 => (int)number,
            int number => number,
            _ => 0
        };
        return new(id, instance, service, domain, port, addresses, txt);
    }
}
