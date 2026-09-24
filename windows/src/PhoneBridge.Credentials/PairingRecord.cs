using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.X509;

namespace PhoneBridge.Credentials;

public enum StoreError { InvalidInput, InvalidIdentity, IdentityMismatch, Missing, NeedsRepair, AlreadyExists, Busy, UnsafeLocation, StateConflict, RevisionConflict, IoFailure }
public sealed class CredentialStoreException(StoreError error) : Exception("credential_store_"+error.ToString().ToLowerInvariant())
{ public StoreError Error { get; } = error; }
public enum PairingRecordState : byte { Pending=1, Active=2, RevocationPending=3, NeedsRepair=4 }
public enum AccessMode : byte { ReadOnly=1, Safe=2, ReadWrite=3 }
public enum CredentialPurpose { PairingSubmission, SessionValidation, Mount, SelfRevocation }

/// <summary>Certificate validation only: the caller must obtain the fingerprint from the confirmed PAKE exchange.</summary>
public sealed class ValidatedDeviceIdentity
{
    private readonly byte[] _der;
    public string Sha256 { get; }
    public string DeviceId => "pbng-"+Sha256;
    public byte[] CertificateDer => (byte[])_der.Clone();
    private ValidatedDeviceIdentity(byte[] der,string sha) { _der=der;Sha256=sha; }
    public static ValidatedDeviceIdentity Validate(ReadOnlySpan<byte> der,string confirmedSha256)
    {
        try
        {
            if (der.Length is < 1 or > 4096 || !RecordRules.Hex(confirmedSha256,64)) throw new CredentialStoreException(StoreError.InvalidIdentity);
            var bytes=der.ToArray();var sha=Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (sha!=confirmedSha256) throw new CredentialStoreException(StoreError.IdentityMismatch);
            // Exactly one canonical DER object. No PEM, PKCS#12, trailing certificate or BER alternate encoding.
            if (!Asn1Object.FromByteArray(bytes).GetEncoded(Asn1Encodable.Der).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException();
            var certificate=new X509CertificateParser().ReadCertificate(bytes);
            if (!certificate.GetEncoded().AsSpan().SequenceEqual(bytes) || certificate.Version!=3 || certificate.GetBasicConstraints()<0 ||
                !certificate.IssuerDN.Equivalent(certificate.SubjectDN,true) || certificate.SigAlgOid!="1.2.840.113549.1.1.11") throw new InvalidDataException();
            var usage=certificate.GetKeyUsage();
            if (usage is null || usage.Length<=5 || !usage[5]) throw new InvalidDataException();
            var critical=certificate.GetCriticalExtensionOids();
            if (critical is not null && critical.Any(oid=>oid is not ("2.5.29.19" or "2.5.29.15"))) throw new InvalidDataException();
            if (certificate.GetPublicKey() is not RsaKeyParameters key || key.IsPrivate || key.Modulus.BitLength<2048) throw new InvalidDataException();
            certificate.CheckValidity(DateTime.UtcNow);certificate.Verify(key);
            return new ValidatedDeviceIdentity(bytes,sha);
        }
        catch (CredentialStoreException) { throw; }
        catch { throw new CredentialStoreException(StoreError.InvalidIdentity); }
    }
}

public sealed class PairingRecord
{
    public ValidatedDeviceIdentity Identity { get; }
    public string DeviceId => Identity.DeviceId;
    public string ClientId { get; }
    public string DeviceName { get; }
    public string ClientName { get; }
    public PairingRecordState State { get; }
    public AccessMode Mode { get; }
    public ulong Revision { get; }
    public bool CanMount => State==PairingRecordState.Active;
    internal PairingRecord(ValidatedDeviceIdentity identity,string client,string deviceName,string clientName,PairingRecordState state,AccessMode mode,ulong revision)
    {
        if (!RecordRules.Hex(client,32) || !Enum.IsDefined(state) || !Enum.IsDefined(mode) || revision==0) throw new CredentialStoreException(StoreError.InvalidInput);
        RecordRules.Name(deviceName);RecordRules.Name(clientName);
        Identity=identity;ClientId=client;DeviceName=deviceName;ClientName=clientName;State=state;Mode=mode;Revision=revision;
    }
    internal PairingRecord WithState(PairingRecordState state,AccessMode mode) => new(Identity,ClientId,DeviceName,ClientName,state,mode,checked(Revision+1));
    public override string ToString() => "PairingRecord(redacted)";
}

public sealed class CredentialLease : IDisposable
{
    private readonly object _gate=new();
    private readonly byte[] _token;
    private bool _disposed;
    public string Username { get; }
    internal CredentialLease(string clientId,byte[] token) { Username="pbng-"+clientId;_token=(byte[])token.Clone(); }
    public byte[] CopyToken() { lock(_gate) { ObjectDisposedException.ThrowIf(_disposed,this);return (byte[])_token.Clone(); } }
    public void Dispose() { lock(_gate) { CryptographicOperations.ZeroMemory(_token);_disposed=true; } }
    public override string ToString() => "CredentialLease(redacted)";
}

internal static class RecordRules
{
    internal static readonly UTF8Encoding Utf8=new(false,true);
    internal static bool Hex(string? text,int length) => text is not null && text.Length==length && text.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static string HashFromDeviceId(string id)
    {
        if (id is null || id.Length!=69 || !id.StartsWith("pbng-",StringComparison.Ordinal) || !Hex(id[5..],64)) throw new CredentialStoreException(StoreError.InvalidInput);
        return id[5..];
    }
    internal static void Name(string name)
    {
        try
        {
            if (name is null || name.Length==0 || name.Length>256 || Utf8.GetByteCount(name)>256) throw new InvalidDataException();
            int count=0;
            foreach (var rune in name.EnumerateRunes())
                if (++count>128 || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format) throw new InvalidDataException();
        }
        catch { throw new CredentialStoreException(StoreError.InvalidInput); }
    }
}

internal sealed class StoredRecord(PairingRecord metadata,byte[] ownedToken) : IDisposable
{
    internal PairingRecord Metadata { get; set; }=metadata;
    internal byte[] Token { get; }=ownedToken;
    public void Dispose() => CryptographicOperations.ZeroMemory(Token);
}
