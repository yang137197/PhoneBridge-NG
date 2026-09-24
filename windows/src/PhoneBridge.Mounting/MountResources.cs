using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace PhoneBridge.Mounting;

internal sealed class MountResources : IDisposable
{
    public const string RcloneSha256 = "033eee51c9ad47c2de2624b6674d355274bcd6cf0027a5f85db4437ba24ae81c";
    private Semaphore? lease;
    private Semaphore? cacheLease;
    private FileStream? executableLock;
    private FileStream? certificateLock;
    private FileStream? configLock;
    private string? directory;
    public string CertificatePath { get; private set; } = "";
    public string ConfigPath { get; private set; } = "";
    public string CacheMetadataRoot { get; private set; } = "";

    public static bool DrivePresent(char letter) => DriveInfo.GetDrives().Any(d => char.ToUpperInvariant(d.Name[0]) == letter);

    public static MountResources Acquire(MountRequest request)
    {
        var resources = new MountResources();
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new MountException("windows-identity-unavailable");
            var lease = new Semaphore(1, 1, $"Local\\PhoneBridge-NG-{sid.Value}-{request.DriveLetter}");
            if (!lease.WaitOne(0)) { lease.Dispose(); throw new MountException("drive-reserved"); }
            resources.lease = lease;
            if (DrivePresent(request.DriveLetter)) throw new MountException("drive-occupied");
            if (request.AccessMode != MountAccessMode.ReadOnly)
            {
                var cacheLease = new Semaphore(1, 1, $"Local\\PhoneBridge-NG-Cache-{sid.Value}-{request.Identity.Sha256}");
                if (!cacheLease.WaitOne(0)) { cacheLease.Dispose(); throw new MountException("device-cache-reserved"); }
                resources.cacheLease = cacheLease;
            }
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var winfsp = registry.OpenSubKey(@"SOFTWARE\WinFsp");
            if (winfsp?.GetValue("InstallDir") is not string install ||
                !File.Exists(Path.Combine(install, "bin", "winfsp-x64.dll"))) throw new MountException("winfsp-missing");
            resources.executableLock = new FileStream(request.RclonePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexStringLower(SHA256.HashData(resources.executableLock));
            if (hash != RcloneSha256) throw new MountException("rclone-hash-mismatch");

            for (var parent = new DirectoryInfo(request.SessionRoot); parent is not null; parent = parent.Parent)
                if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new MountException("session-path-reparse-point");
            Directory.CreateDirectory(request.SessionRoot);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(sid);
            foreach (var principal in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (request.AccessMode != MountAccessMode.ReadOnly)
            {
                for (var parent = new DirectoryInfo(request.CacheRoot); parent is not null; parent = parent.Parent)
                    if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new MountException("cache-path-reparse-point");
                var cache = new DirectoryInfo(request.CacheRoot);
                cache.Create(security);
                cache.SetAccessControl(security);
                resources.CacheMetadataRoot = Path.Combine(request.CacheRoot, "vfsMeta", RcloneMountSession.RemoteName);
            }
            resources.directory = Path.Combine(request.SessionRoot, Guid.NewGuid().ToString("N"));
            new DirectoryInfo(resources.directory).Create(security);
            resources.CertificatePath = Path.Combine(resources.directory, "device-ca.pem");
            File.WriteAllText(resources.CertificatePath, request.Identity.CertificatePem);
            resources.certificateLock = new FileStream(resources.CertificatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Validate the exact bytes held open, not just the in-memory source used for writing.
            using var reader = new StreamReader(resources.certificateLock, leaveOpen: true);
            if (reader.ReadToEnd() != request.Identity.CertificatePem) throw new MountException("identity-file-mismatch");
            return resources;
        }
        catch { resources.Dispose(); throw; }
    }

    public void WriteRcloneConfig(MountRequest request, string obscuredPassword)
    {
        if (directory is null || configLock is not null) throw new MountException("mount-resource-state-invalid");
        var contents = RcloneMountSession.ConfigText(request, obscuredPassword);
        ConfigPath = Path.Combine(directory, "rclone.conf");
        File.WriteAllText(ConfigPath, contents, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        configLock = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(configLock, leaveOpen: true);
        if (reader.ReadToEnd() != contents) throw new MountException("rclone-config-write-failed");
    }

    public void Dispose()
    {
        configLock?.Dispose(); configLock = null;
        certificateLock?.Dispose(); certificateLock = null;
        executableLock?.Dispose(); executableLock = null;
        try
        {
            // Only these exact files in our random session directory; never recursive deletion.
            if (ConfigPath.Length != 0) File.Delete(ConfigPath);
            if (CertificatePath.Length != 0) File.Delete(CertificatePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
        }
        finally
        {
            cacheLease?.Release(); cacheLease?.Dispose(); cacheLease = null;
            lease?.Release(); lease?.Dispose(); lease = null;
        }
    }
}
