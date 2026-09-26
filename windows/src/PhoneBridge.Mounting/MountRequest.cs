using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PhoneBridge.Discovery;

namespace PhoneBridge.Mounting;

public enum MountAccessMode { ReadOnly, Safe, ReadWrite }

public sealed class MountException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// Construct only from an independently confirmed identity. This is NOT a pairing protocol.
public sealed class ConfirmedIdentity
{
    private readonly byte[] certificate;
    public string Sha256 { get; }
    public string DeviceId => "pbng-" + Sha256;
    internal string CertificatePem => PemEncoding.WriteString("CERTIFICATE", certificate);

    public ConfirmedIdentity(byte[] certificateDer, string confirmedSha256)
    {
        if (certificateDer is null || certificateDer.Length is 0 or > 16384 || confirmedSha256 is null || confirmedSha256.Length != 64)
            throw new MountException("invalid-identity");
        certificate = certificateDer.ToArray();
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(certificate));
        if (!string.Equals(Sha256, confirmedSha256, StringComparison.OrdinalIgnoreCase))
            throw new MountException("identity-mismatch");
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificate);
            var ca = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
            var usage = cert.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
            if (ca?.CertificateAuthority != true || usage is null ||
                !usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign) || cert.Subject != cert.Issuer ||
                cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new MountException("invalid-identity-ca");
        }
        catch (CryptographicException) { throw new MountException("invalid-identity-ca"); }
    }
}

public sealed class SessionCredentials
{
    internal string User { get; }
    internal string Password { get; }
    public SessionCredentials(string user, string password)
    {
        if (string.IsNullOrEmpty(user) || user.Length > 128 || user.Any(c => char.IsControl(c) || c == ':') ||
            string.IsNullOrEmpty(password) || password.Length > 1024 || password.Any(char.IsControl))
            throw new MountException("invalid-credentials");
        User = user;
        Password = password;
    }
    public override string ToString() => "[redacted]";
}

public sealed class MountRequest
{
    public ConfirmedIdentity Identity { get; }
    internal SessionCredentials Credentials { get; }
    public DeviceEndpoint Endpoint { get; }
    public string RemoteDirectory { get; }
    public char DriveLetter { get; }
    public string RclonePath { get; }
    public string SessionRoot { get; }
    public string CacheRoot { get; }
    public MountAccessMode AccessMode { get; }
    internal string WebDavUrl => Endpoint.HttpsAddress + string.Join('/',
        RemoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString)) +
        (RemoteDirectory.Length == 0 ? "" : "/");

    public MountRequest(ConfirmedIdentity identity, SessionCredentials credentials, string address, int port,
        string remoteDirectory, char driveLetter, string rclonePath, string sessionRoot,
        MountAccessMode accessMode = MountAccessMode.ReadOnly, string? cacheBaseRoot = null)
    {
        if (address is null || !CandidateParser.TryManual(address, port, out var candidate)) throw new MountException("invalid-endpoint");
        if (remoteDirectory is null || remoteDirectory.Length > 1024 || remoteDirectory.StartsWith('/') || remoteDirectory.EndsWith('/') ||
            remoteDirectory.Any(c => char.IsControl(c) || c is '\\' or ':' or '%' or '?' or '#') ||
            remoteDirectory.Split('/').Any(s => s is "." or ".." || (s.Length == 0 && remoteDirectory.Length != 0)))
            throw new MountException("invalid-remote-directory");
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (driveLetter is < 'C' or > 'Z') throw new MountException("invalid-drive");
        if (!Path.IsPathFullyQualified(rclonePath) || !Path.IsPathFullyQualified(sessionRoot) ||
            (cacheBaseRoot is not null && !Path.IsPathFullyQualified(cacheBaseRoot)) ||
            rclonePath.StartsWith("\\\\", StringComparison.Ordinal) || sessionRoot.StartsWith("\\\\", StringComparison.Ordinal) ||
            (cacheBaseRoot?.StartsWith("\\\\", StringComparison.Ordinal) ?? false))
            throw new MountException("local-absolute-path-required");
        Identity = identity ?? throw new MountException("identity-required");
        Credentials = credentials ?? throw new MountException("credentials-required");
        Endpoint = candidate!.Endpoints.Single();
        RemoteDirectory = remoteDirectory;
        DriveLetter = driveLetter;
        RclonePath = Path.GetFullPath(rclonePath);
        SessionRoot = Path.GetFullPath(sessionRoot);
        string cacheBase = cacheBaseRoot is null ? SessionRoot : Path.GetFullPath(cacheBaseRoot);
        CacheRoot = Path.Combine(cacheBase, "VfsCache-v1", Identity.Sha256);
        AccessMode = accessMode;
    }
}
