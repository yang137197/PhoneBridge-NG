using PhoneBridge.Credentials;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

try
{
    if(args is ["anonymous"])
    {
        const string id="pbng-0000000000000000000000000000000000000000000000000000000000000000";
        byte[] plain="synthetic-dpapi-context-proof"u8.ToArray();var blob=CurrentUserProtection.Protect(plain,id);
        // Warm the managed path before the isolated process thread impersonates anonymous.
        var warm=CurrentUserProtection.Unprotect(blob,id);CryptographicOperations.ZeroMemory(warm);
        using var thread=Native.OpenThread(0x100,false,Native.GetCurrentThreadId());
        if(thread.IsInvalid || !Native.ImpersonateAnonymousToken(thread))return 6;
        bool refused=false;
        try { var unexpected=CurrentUserProtection.Unprotect(blob,id);CryptographicOperations.ZeroMemory(unexpected); }
        catch(CredentialStoreException) { refused=true; }
        finally { if(!Native.RevertToSelf())Environment.Exit(7); }
        CryptographicOperations.ZeroMemory(plain);Console.WriteLine(refused?"ANONYMOUS_REFUSED":"UNEXPECTED_SUCCESS");return refused?0:5;
    }
    if(args.Length!=4 || args[0] is not ("read" or "pause"))return 3;
    var root=Path.GetFullPath(args[1]);var publicCa=Path.GetFullPath(args[2]);
    var project=new DirectoryInfo(AppContext.BaseDirectory);
    while(project is not null && !File.Exists(Path.Combine(project.FullName,"AGENTS.md")))project=project.Parent;
    if(project is null)return 3;
    var allowed=Path.Combine(project.FullName,".audit","p1-005")+Path.DirectorySeparatorChar;
    if(!root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!publicCa.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))return 3;
    byte[] der=File.ReadAllBytes(publicCa);var identity=ValidatedDeviceIdentity.Validate(der,Convert.ToHexStringLower(SHA256.HashData(der)));
    Action<CommitStage>? hook=null;
    if(args[0]=="pause")
    {
        if(!Enum.TryParse<CommitStage>(args[3],out var stage)||!Enum.IsDefined(stage))return 3;
        hook=current=>{ if(current==stage) { Console.WriteLine("READY");_=Console.ReadLine();throw new IOException(); } };
    }
    var store=PairingStore.OpenAt(root,hook);var metadata=store.Load(identity.DeviceId);
    if(args[0]=="pause") { _=store.BeginRevocation(metadata);return 4; }
    using var credential=store.OpenCredential(metadata.DeviceId,metadata.ClientId,
        metadata.State==PairingRecordState.RevocationPending?CredentialPurpose.SelfRevocation:CredentialPurpose.SessionValidation);
    var token=credential.CopyToken();Console.WriteLine("OK|"+metadata.State+"|"+metadata.Revision+"|"+Convert.ToHexStringLower(SHA256.HashData(token)));
    CryptographicOperations.ZeroMemory(token);return 0;
}
catch(CredentialStoreException e) { Console.WriteLine("ERROR|"+e.Error);return 2; }
catch { Console.WriteLine("ERROR|fixture");return 2; }

internal static class Native
{
    [DllImport("kernel32.dll",SetLastError=true)]internal static extern SafeFileHandle OpenThread(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint id);
    [DllImport("kernel32.dll")]internal static extern uint GetCurrentThreadId();
    [DllImport("advapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool ImpersonateAnonymousToken(SafeFileHandle thread);
    [DllImport("advapi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool RevertToSelf();
}
