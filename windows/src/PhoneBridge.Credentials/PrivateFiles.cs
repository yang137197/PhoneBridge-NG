using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace PhoneBridge.Credentials;

internal enum CommitStage { Protected, TempCreated, Flushed, BeforeCommit, Committed }

internal sealed class PrivateFiles : IDisposable
{
    private readonly List<SafeFileHandle> _directories=[];
    private SafeFileHandle? _lease;
    private readonly string _root;
    private readonly SecurityIdentifier _sid;
    private static readonly SecurityIdentifier SystemSid=new(WellKnownSidType.LocalSystemSid,null);
    internal string PathFor(string deviceId) => Path.Combine(_root,RecordRules.HashFromDeviceId(deviceId)+".pairing");
    private PrivateFiles(string root)
    {
        _root=root;using var identity=WindowsIdentity.GetCurrent();
        _sid=identity.User??throw new CredentialStoreException(StoreError.UnsafeLocation);
        if (_sid==SystemSid) throw new CredentialStoreException(StoreError.UnsafeLocation);
    }

    internal static PrivateFiles Acquire(string root,string firstPrivate,bool create)
    {
        var session=new PrivateFiles(root);
        try
        {
            var chain=new Stack<string>();for(var d=new DirectoryInfo(root);d is not null;d=d.Parent)chain.Push(d.FullName);
            bool requirePrivate=false;
            foreach(var path in chain)
            {
                if (string.Equals(path,firstPrivate,StringComparison.OrdinalIgnoreCase)) requirePrivate=true;
                if (!Directory.Exists(path))
                {
                    if (!create || !requirePrivate) throw new CredentialStoreException(StoreError.Missing);
                    new DirectoryInfo(path).Create(session.DirectoryAcl());
                }
                var handle=Open(path,0x20080,3,3,0x02200000,null);
                try
                {
                    var info=Information(handle);
                    if ((info.Attributes&0x400)!=0 || (info.Attributes&0x10)==0) throw new CredentialStoreException(StoreError.UnsafeLocation);
                    if (requirePrivate) session.CheckAcl(handle,true);
                    session._directories.Add(handle);
                }
                catch { handle.Dispose();throw; }
            }
            if (!requirePrivate) throw new CredentialStoreException(StoreError.UnsafeLocation);
            session._lease=session.OpenRegular(Path.Combine(root,".store.lock"),0xC0020000,0,4);
            return session;
        }
        catch { session.Dispose();throw; }
    }

    internal byte[] Read(string deviceId)
    {
        using var handle=OpenRegular(PathFor(deviceId),0x80020000,1,3);
        using var stream=new FileStream(handle,FileAccess.Read);
        if (stream.Length is < 1 or > RecordCodec.MaxFileBytes) throw new CredentialStoreException(StoreError.NeedsRepair);
        var result=new byte[(int)stream.Length];stream.ReadExactly(result);return result;
    }

    internal void Write(string deviceId,byte[] ciphertext,bool replace,Action<CommitStage>? checkpoint)
    {
        if (ciphertext.Length is < 1 or > RecordCodec.MaxFileBytes) throw new CredentialStoreException(StoreError.InvalidInput);
        string destination=PathFor(deviceId);string temporary=Path.Combine(_root,".pending-"+Guid.NewGuid().ToString("N")+".tmp");
        bool created=false;
        try
        {
            checkpoint?.Invoke(CommitStage.Protected);
            using(var handle=OpenRegular(temporary,0xC0020000,0,1,true))
            using(var stream=new FileStream(handle,FileAccess.ReadWrite))
            {
                created=true;checkpoint?.Invoke(CommitStage.TempCreated);
                stream.Write(ciphertext);stream.Flush(flushToDisk:true);checkpoint?.Invoke(CommitStage.Flushed);
            }
            checkpoint?.Invoke(CommitStage.BeforeCommit);
            if (replace)
            {
                for(int attempt=0;;attempt++)
                {
                    // Revalidate both entries on every retry; the transaction lease and parent pins remain held.
                    using (var existing=OpenRegular(destination,0x80020000,1,3)) { }
                    using (var source=OpenRegular(temporary,0x80020000,1,3)) { }
                    try { File.Replace(temporary,destination,null,ignoreMetadataErrors:false);break; }
                    // These contention errors retain both names. Never retry 1176/1177 or uncertain partial commits.
                    catch(IOException e) when(IsReplaceContention(e.HResult) && attempt<10) { Thread.Sleep(25); }
                }
            }
            else File.Move(temporary,destination,overwrite:false);
            checkpoint?.Invoke(CommitStage.Committed);
        }
        finally
        {
            // Only the exact newly-created ciphertext temporary file; never enumerate/delete data files.
            if (created)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal bool Exists(string deviceId) => File.Exists(PathFor(deviceId)) || Directory.Exists(PathFor(deviceId));
    internal string[] DeviceIds()
    {
        var paths=Directory.EnumerateFileSystemEntries(_root,"*.pairing").Take(257).ToArray();
        if(paths.Length>256) throw new CredentialStoreException(StoreError.NeedsRepair);
        return paths.Select(path=>
        {
            var name=Path.GetFileNameWithoutExtension(path);
            if(!RecordRules.Hex(name,64)) throw new CredentialStoreException(StoreError.NeedsRepair);
            return "pbng-"+name;
        }).Order(StringComparer.Ordinal).ToArray();
    }
    internal void Delete(string deviceId)
    {
        // Delete the validated object, not a pathname that could be replaced between check and deletion.
        using var handle=OpenRegular(PathFor(deviceId),0x00030080,0,3);
        int delete=1;
        if(!SetFileInformationByHandle(handle,4,ref delete,4)) throw new CredentialStoreException(StoreError.IoFailure);
    }
    private static bool IsReplaceContention(int error) => error is
        unchecked((int)0x80070020) or unchecked((int)0x80070021) or unchecked((int)0x80070497);

    private SafeFileHandle OpenRegular(string path,uint access,uint share,uint disposition,bool writeThrough=false)
    {
        var handle=Open(path,access,share,disposition,0x00200080U|(writeThrough?0x80000000U:0),FileAcl().GetSecurityDescriptorBinaryForm());
        try
        {
            var info=Information(handle);
            if ((info.Attributes&(0x400|0x10))!=0 || info.Links!=1) throw new CredentialStoreException(StoreError.UnsafeLocation);
            CheckAcl(handle,false);return handle;
        }
        catch { handle.Dispose();throw; }
    }

    private DirectorySecurity DirectoryAcl()
    {
        var acl=new DirectorySecurity();acl.SetOwner(_sid);acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{_sid,SystemSid}) acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        return acl;
    }
    private FileSecurity FileAcl()
    {
        var acl=new FileSecurity();acl.SetOwner(_sid);acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{_sid,SystemSid})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
        return acl;
    }

    private void CheckAcl(SafeFileHandle handle,bool directory)
    {
        nint descriptor=0;
        try
        {
            if(GetSecurityInfo(handle,1,5,out _,out _,out _,out _,out descriptor)!=0) throw new CredentialStoreException(StoreError.UnsafeLocation);
            uint size=GetSecurityDescriptorLength(descriptor);
            if (size is 0 or > 65536) throw new CredentialStoreException(StoreError.UnsafeLocation);
            var bytes=new byte[(int)size];Marshal.Copy(descriptor,bytes,0,bytes.Length);var sd=new RawSecurityDescriptor(bytes,0);
            var acl=sd.DiscretionaryAcl;
            if (sd.Owner!=_sid || !sd.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) || acl is null || acl.Count!=2)
                throw new CredentialStoreException(StoreError.UnsafeLocation);
            var seen=new HashSet<string>();var expectedFlags=directory?AceFlags.ContainerInherit|AceFlags.ObjectInherit:AceFlags.None;
            foreach(GenericAce ace in acl)
            {
                if (ace is not CommonAce common || common.AceQualifier!=AceQualifier.AccessAllowed || common.IsCallback ||
                    common.AceFlags!=expectedFlags || common.AccessMask!=(int)FileSystemRights.FullControl ||
                    (common.SecurityIdentifier!=_sid && common.SecurityIdentifier!=SystemSid) || !seen.Add(common.SecurityIdentifier.Value))
                    throw new CredentialStoreException(StoreError.UnsafeLocation);
            }
        }
        finally { if(descriptor!=0)_=LocalFree(descriptor); }
    }

    private static SafeFileHandle Open(string path,uint access,uint share,uint disposition,uint flags,byte[]? acl)
    {
        GCHandle pin=default;
        try
        {
            if(acl is not null)pin=GCHandle.Alloc(acl,GCHandleType.Pinned);
            var attributes=new SecurityAttributes { Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=pin.IsAllocated?pin.AddrOfPinnedObject():0,Inherit=0 };
            for(int attempt=0;;attempt++)
            {
                var handle=CreateFileW(path,access,share,ref attributes,disposition,flags,0);
                if(!handle.IsInvalid)return handle;
                int error=Marshal.GetLastWin32Error();handle.Dispose();
                if(error is 32 or 33 && disposition is 3 or 4 && attempt<10) { Thread.Sleep(25);continue; }
                throw new CredentialStoreException(error is 2 or 3?StoreError.Missing:error is 32 or 33?StoreError.Busy:StoreError.UnsafeLocation);
            }
        }
        finally { if(pin.IsAllocated)pin.Free(); }
    }

    private static FileInformation Information(SafeFileHandle handle)
    { if(!GetFileInformationByHandle(handle,out var info))throw new CredentialStoreException(StoreError.UnsafeLocation);return info; }
    public void Dispose() { _lease?.Dispose();_lease=null;for(int i=_directories.Count-1;i>=0;i--)_directories[i].Dispose();_directories.Clear(); }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length;internal nint Descriptor;internal int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        internal uint Attributes;internal System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        internal uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,ref SecurityAttributes security,uint disposition,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation information);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int informationClass,ref int information,uint size);
    [DllImport("advapi32.dll",SetLastError=true)]
    private static extern uint GetSecurityInfo(SafeFileHandle handle,uint type,uint flags,out nint owner,out nint group,out nint dacl,out nint sacl,out nint descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(nint descriptor);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
