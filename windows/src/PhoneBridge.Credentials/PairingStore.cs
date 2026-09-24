using System.Security.Cryptography;

namespace PhoneBridge.Credentials;

public sealed class PairingStore
{
    private readonly object _gate=new();
    private readonly string _root,_firstPrivate;
    private readonly Action<CommitStage>? _checkpoint;
    private bool _uncertainWrite;
    private PairingStore(string root,string firstPrivate,Action<CommitStage>? checkpoint)
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith("\\\\",StringComparison.Ordinal) ||
            root.Split(Path.DirectorySeparatorChar).Any(p=>p.EndsWith(' ')||p.EndsWith('.'))) throw new CredentialStoreException(StoreError.UnsafeLocation);
        _root=Path.GetFullPath(root);_firstPrivate=Path.GetFullPath(firstPrivate);_checkpoint=checkpoint;
        if (new DriveInfo(Path.GetPathRoot(_root)!).DriveType!=DriveType.Fixed) throw new CredentialStoreException(StoreError.UnsafeLocation);
    }
    public static PairingStore Open()
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string app=Path.Combine(local,"PhoneBridge-NG");return new(Path.Combine(app,"Pairings-v1"),app,null);
    }
    internal static PairingStore OpenAt(string root,Action<CommitStage>? checkpoint=null) => new(root,root,checkpoint);

    public PairingRecord CreatePending(ValidatedDeviceIdentity identity,string clientId,string deviceName,string clientName)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var checkedIdentity=ValidatedDeviceIdentity.Validate(identity.CertificateDer,identity.Sha256);
        var metadata=new PairingRecord(checkedIdentity,clientId,deviceName,clientName,PairingRecordState.Pending,AccessMode.Safe,1);
        return Run(true,files=>
        {
            if(files.Exists(metadata.DeviceId))throw new CredentialStoreException(StoreError.AlreadyExists);
            using var record=new StoredRecord(metadata,RandomNumberGenerator.GetBytes(32));
            Commit(files,record,false);return record.Metadata;
        });
    }

    public PairingRecord Load(string deviceId)
    {
        _=RecordRules.HashFromDeviceId(deviceId);
        return Run(false,files=>{ using var record=Read(files,deviceId);return record.Metadata; });
    }

    public IReadOnlyList<PairingRecord> List()
    {
        try { return Run(false, files => files.DeviceIds().Select(id => { using var r=Read(files,id); return r.Metadata; }).ToArray()); }
        catch (CredentialStoreException e) when(e.Error==StoreError.Missing && !Directory.Exists(_root)) { return []; }
    }

    /// <summary>Caller must first prove cancellation/revocation and token rejection over the saved identity's strict TLS.</summary>
    public void RemoveAfterVerifiedRevocation(PairingRecord expected) => Run(false,files=>
    {
        using var stored=Read(files,expected.DeviceId);
        if(stored.Metadata.ClientId!=expected.ClientId) throw new CredentialStoreException(StoreError.IdentityMismatch);
        if(stored.Metadata.Revision!=expected.Revision) throw new CredentialStoreException(StoreError.RevisionConflict);
        if(stored.Metadata.State!=PairingRecordState.RevocationPending) throw new CredentialStoreException(StoreError.StateConflict);
        files.Delete(expected.DeviceId);return true;
    });

    /// <summary>Explicit user choice only. Remote authorization may remain; never label this as remote revocation.</summary>
    public void ForgetLocally(PairingRecord expected) => RemoveAfterVerifiedRevocation(expected);

    public CredentialLease OpenCredential(string deviceId,string expectedClientId,CredentialPurpose purpose)
    {
        _=RecordRules.HashFromDeviceId(deviceId);
        if(!RecordRules.Hex(expectedClientId,32)||!Enum.IsDefined(purpose))throw new CredentialStoreException(StoreError.InvalidInput);
        return Run(false,files=>
    {
        using var record=Read(files,deviceId);
        if(record.Metadata.ClientId!=expectedClientId)throw new CredentialStoreException(StoreError.IdentityMismatch);
        bool allowed=record.Metadata.State switch
        {
            PairingRecordState.Pending => purpose is CredentialPurpose.PairingSubmission or CredentialPurpose.SessionValidation,
            PairingRecordState.Active => purpose is CredentialPurpose.SessionValidation or CredentialPurpose.Mount or CredentialPurpose.SelfRevocation,
            PairingRecordState.RevocationPending => purpose==CredentialPurpose.SelfRevocation,
            _ => false
        };
        if(!allowed)throw new CredentialStoreException(StoreError.StateConflict);
        return new CredentialLease(record.Metadata.ClientId,record.Token);
    });
    }

    /// <summary>Only a caller that verified the saved token over the bound CA and endpoint may apply the response.</summary>
    public PairingRecord ApplyVerifiedSession(PairingRecord expected,string responseDeviceId,string responseClientId,AccessMode mode)
    {
        if(expected.DeviceId!=responseDeviceId || expected.ClientId!=responseClientId || !Enum.IsDefined(mode))
            throw new CredentialStoreException(StoreError.IdentityMismatch);
        return Change(expected,PairingRecordState.Active,mode);
    }
    public PairingRecord BeginRevocation(PairingRecord expected) => Change(expected,PairingRecordState.RevocationPending,expected.Mode);
    public PairingRecord MarkNeedsRepair(PairingRecord expected) => Change(expected,PairingRecordState.NeedsRepair,expected.Mode);

    private PairingRecord Change(PairingRecord expected,PairingRecordState state,AccessMode mode) => Run(false,files=>
    {
        using var record=Read(files,expected.DeviceId);var current=record.Metadata;
        if(current.ClientId!=expected.ClientId)throw new CredentialStoreException(StoreError.IdentityMismatch);
        if(current.Revision!=expected.Revision)throw new CredentialStoreException(StoreError.RevisionConflict);
        if(current.State==state && current.Mode==mode)return current;
        bool allowed=state switch
        {
            PairingRecordState.Active => current.State is PairingRecordState.Pending or PairingRecordState.Active,
            PairingRecordState.RevocationPending => current.State is PairingRecordState.Pending or PairingRecordState.Active,
            PairingRecordState.NeedsRepair => current.State!=PairingRecordState.NeedsRepair,
            _ => false
        };
        if(!allowed)throw new CredentialStoreException(StoreError.StateConflict);
        record.Metadata=current.WithState(state,mode);Commit(files,record,true);return record.Metadata;
    });

    private static StoredRecord Read(PrivateFiles files,string deviceId)
    {
        byte[] plaintext=CurrentUserProtection.Unprotect(files.Read(deviceId),deviceId);
        try { return RecordCodec.Decode(plaintext,deviceId); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private void Commit(PrivateFiles files,StoredRecord record,bool replace)
    {
        byte[]? plaintext=null;
        try
        {
            plaintext=RecordCodec.Encode(record);
            byte[] encrypted=CurrentUserProtection.Protect(plaintext,record.Metadata.DeviceId);
            CryptographicOperations.ZeroMemory(plaintext);
            files.Write(record.Metadata.DeviceId,encrypted,replace,_checkpoint);
            using var committed=Read(files,record.Metadata.DeviceId);
            if(committed.Metadata.ClientId!=record.Metadata.ClientId || committed.Metadata.Revision!=record.Metadata.Revision ||
                committed.Metadata.State!=record.Metadata.State || committed.Metadata.Mode!=record.Metadata.Mode ||
                !CryptographicOperations.FixedTimeEquals(committed.Token,record.Token)) throw new IOException();
        }
        catch { _uncertainWrite=true;throw new CredentialStoreException(StoreError.IoFailure); }
        finally { if(plaintext is not null)CryptographicOperations.ZeroMemory(plaintext); }
    }

    private T Run<T>(bool create,Func<PrivateFiles,T> action)
    {
        lock(_gate)
        {
            if(_uncertainWrite)throw new CredentialStoreException(StoreError.NeedsRepair);
            try { using var files=PrivateFiles.Acquire(_root,_firstPrivate,create);return action(files); }
            catch(CredentialStoreException) { throw; }
            catch { throw new CredentialStoreException(StoreError.IoFailure); }
        }
    }
}

