using PhoneBridge.Credentials;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

[assembly: DoNotParallelize]
namespace PhoneBridge.Credentials.Tests;

[TestClass]
public sealed class StoreTests
{
    private const string Client="101112131415161718191a1b1c1d1e1f";
    private static string _project="",_run="",_publicCa="";
    private static ValidatedDeviceIdentity _identity=null!;
    private string _base="",_root="";
    public TestContext TestContext { get; set; }=null!;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _ioFailures=new();
    private void RecordIoFailure(object? _,System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
    {
        if(args.Exception is IOException error)
            _ioFailures.Enqueue(error.GetType().Name+"|"+error.HResult.ToString("X8")+"|"+error.TargetSite?.Name);
        if(args.Exception is CredentialStoreException storeError)
            _ioFailures.Enqueue(storeError.Error+"|"+storeError.TargetSite?.Name);
    }
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"AGENTS.md")))d=d.Parent;
        _project=d!.FullName;_run=Path.Combine(_project,".audit","p1-005","run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_run);
        var der=Certificate();_identity=ValidatedDeviceIdentity.Validate(der,Hash(der));_publicCa=Path.Combine(_run,"synthetic-ca.der");File.WriteAllBytes(_publicCa,der);
    }
    [TestInitialize] public void NewRoot()
    {
        _base=Path.Combine(_run,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_base);_root=Path.Combine(_base,"store");
        AppDomain.CurrentDomain.FirstChanceException+=RecordIoFailure;
    }
    [TestCleanup] public void ReportIoFailure()
    {
        AppDomain.CurrentDomain.FirstChanceException-=RecordIoFailure;
        if(TestContext.CurrentTestOutcome==UnitTestOutcome.Failed)
            foreach(var item in _ioFailures.Distinct())TestContext.WriteLine(item);
    }
    private static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
    private PairingRecord Pending(PairingStore? store=null)=>(store??PairingStore.OpenAt(_root)).CreatePending(_identity,Client,"合成测试手机","Synthetic PC");
    private PairingRecord Active(PairingStore store) { var pending=Pending(store);return store.ApplyVerifiedSession(pending,pending.DeviceId,pending.ClientId,AccessMode.Safe); }
    private string RecordPath=>Path.Combine(_root,_identity.Sha256+".pairing");
    private static void Error(StoreError expected,Action action)
    { var error=Assert.Throws<CredentialStoreException>(action);Assert.AreEqual(expected,error.Error);Assert.IsNull(error.InnerException); }
    private static byte[] Certificate(int bits=2048,bool ca=true,bool signing=true,int validity=0,bool critical=false,bool differentAlgorithm=false)
    {
        using var key=RSA.Create(bits);var request=new CertificateRequest("CN=PhoneBridge synthetic storage CA",key,
            differentAlgorithm?HashAlgorithmName.SHA384:HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(signing?X509KeyUsageFlags.KeyCertSign:X509KeyUsageFlags.DigitalSignature,true));
        if(critical)request.CertificateExtensions.Add(new X509Extension("1.2.3.4.5",[5,0],true));
        var now=DateTimeOffset.UtcNow;using var cert=request.CreateSelfSigned(now.AddDays(validity==1?1:-2),now.AddDays(validity==-1?-1:2));
        return cert.Export(X509ContentType.Cert);
    }

    [TestMethod]
    public void ListAndVerifiedRemovalRespectStateAndRevision()
    {
        var store=PairingStore.OpenAt(_root); Assert.IsEmpty(store.List());
        var pending=Pending(store); Assert.HasCount(1,store.List());
        Error(StoreError.StateConflict,()=>store.RemoveAfterVerifiedRevocation(pending));
        var disabled=store.BeginRevocation(pending);
        Error(StoreError.RevisionConflict,()=>store.RemoveAfterVerifiedRevocation(pending));
        store.RemoveAfterVerifiedRevocation(disabled); Assert.IsEmpty(store.List());
        Error(StoreError.Missing,()=>store.Load(disabled.DeviceId));
        var next=store.CreatePending(_identity,"202122232425262728292a2b2c2d2e2f","Phone","PC");
        Assert.AreNotEqual(pending.ClientId,next.ClientId);
    }
    [TestMethod]
    public void CorruptRecordCannotBeHiddenByEnumeration()
    {
        var store=PairingStore.OpenAt(_root); Pending(store);
        File.WriteAllBytes(RecordPath,[1,2,3]);
        Assert.Throws<CredentialStoreException>(()=>store.List());
    }
    [TestMethod]
    public void ExplicitLocalForgetRequiresDisabledState()
    {
        var store=PairingStore.OpenAt(_root); var active=Active(store);
        Error(StoreError.StateConflict,()=>store.RemoveLocally(active));
        store.RemoveLocally(store.BeginRevocation(active)); Assert.IsEmpty(store.List());
    }
    [TestMethod]
    public void PendingPersistsWithoutPlaintextAndCannotMount()
    {
        var store=PairingStore.OpenAt(_root);var p=Pending(store);Assert.AreEqual(PairingRecordState.Pending,p.State);Assert.IsFalse(p.CanMount);
        Error(StoreError.StateConflict,()=>store.OpenCredential(p.DeviceId,Client,CredentialPurpose.Mount));
        using var lease=store.OpenCredential(p.DeviceId,Client,CredentialPurpose.PairingSubmission);var token=lease.CopyToken();
        try
        {
            Assert.HasCount(32,token);Assert.AreEqual("pbng-"+Client,lease.Username);
            var bytes=File.ReadAllBytes(RecordPath);Assert.IsLessThanOrEqualTo(65536,bytes.Length);
            Assert.AreEqual(-1,bytes.AsSpan().IndexOf(token));Assert.AreEqual(-1,bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(p.DeviceName)));
            Assert.IsFalse(JsonSerializer.Serialize(p).Contains(Convert.ToBase64String(token),StringComparison.Ordinal));
            Assert.IsFalse(JsonSerializer.Serialize(lease).Contains(Convert.ToBase64String(token),StringComparison.Ordinal));
            using var restored=PairingStore.OpenAt(_root).OpenCredential(p.DeviceId,Client,CredentialPurpose.SessionValidation);
            var copy=restored.CopyToken();Assert.IsTrue(CryptographicOperations.FixedTimeEquals(token,copy));CryptographicOperations.ZeroMemory(copy);
        }
        finally { CryptographicOperations.ZeroMemory(token); }
        lease.Dispose();Assert.Throws<ObjectDisposedException>(()=>lease.CopyToken());Assert.AreEqual("CredentialLease(redacted)",lease.ToString());
    }

    [TestMethod]
    public void AuthorizedStateAndRevocationAreCompareAndSwap()
    {
        var store=PairingStore.OpenAt(_root);var p=Pending(store);
        Error(StoreError.IdentityMismatch,()=>store.ApplyVerifiedSession(p,"pbng-"+new string('0',64),Client,AccessMode.Safe));
        var a=store.ApplyVerifiedSession(p,p.DeviceId,Client,AccessMode.ReadOnly);Assert.IsTrue(a.CanMount);Assert.AreEqual(2UL,a.Revision);
        using(var lease=store.OpenCredential(a.DeviceId,Client,CredentialPurpose.Mount)) { }
        Error(StoreError.RevisionConflict,()=>store.BeginRevocation(p));
        var revoked=store.BeginRevocation(a);Assert.AreEqual(PairingRecordState.RevocationPending,revoked.State);
        Error(StoreError.StateConflict,()=>store.OpenCredential(a.DeviceId,Client,CredentialPurpose.Mount));
        Error(StoreError.StateConflict,()=>store.OpenCredential(a.DeviceId,Client,CredentialPurpose.SessionValidation));
        using(var revokeLease=store.OpenCredential(a.DeviceId,Client,CredentialPurpose.SelfRevocation)) { }
        Error(StoreError.RevisionConflict,()=>store.ApplyVerifiedSession(p,p.DeviceId,Client,AccessMode.Safe));
        Error(StoreError.StateConflict,()=>store.ApplyVerifiedSession(revoked,p.DeviceId,Client,AccessMode.Safe));
        Assert.AreEqual(revoked.Revision,store.BeginRevocation(revoked).Revision);
        var repair=store.MarkNeedsRepair(revoked);Assert.AreEqual(PairingRecordState.NeedsRepair,repair.State);
        Error(StoreError.StateConflict,()=>store.OpenCredential(p.DeviceId,Client,CredentialPurpose.SelfRevocation));
    }

    [TestMethod]
    public void VerifiedModeRefreshPreservesTokenAndRejectsStaleRevision()
    {
        var store=PairingStore.OpenAt(_root);var active=Active(store);
        using var before=store.OpenCredential(active.DeviceId,Client,CredentialPurpose.Mount);
        var first=before.CopyToken();
        try
        {
            var updated=store.ApplyVerifiedSession(active,active.DeviceId,Client,AccessMode.ReadOnly);
            Assert.AreEqual(AccessMode.ReadOnly,updated.Mode);Assert.AreEqual(active.Revision+1,updated.Revision);
            Assert.AreEqual(updated.Revision,store.ApplyVerifiedSession(updated,updated.DeviceId,Client,updated.Mode).Revision);
            Error(StoreError.RevisionConflict,()=>store.ApplyVerifiedSession(active,active.DeviceId,Client,AccessMode.ReadWrite));
            using var after=PairingStore.OpenAt(_root).OpenCredential(active.DeviceId,Client,CredentialPurpose.Mount);
            var second=after.CopyToken();
            try { Assert.IsTrue(CryptographicOperations.FixedTimeEquals(first,second)); }
            finally { CryptographicOperations.ZeroMemory(second); }
        }
        finally { CryptographicOperations.ZeroMemory(first); }
    }

    [TestMethod]
    public void InvalidCredentialRequestDoesNotTouchStore()
    {
        var store=PairingStore.OpenAt(_root);
        Error(StoreError.InvalidInput,()=>store.OpenCredential("../../other",Client,CredentialPurpose.Mount));
        Error(StoreError.InvalidInput,()=>store.OpenCredential(_identity.DeviceId,"INVALID",CredentialPurpose.Mount));
        Error(StoreError.InvalidInput,()=>store.OpenCredential(_identity.DeviceId,Client,(CredentialPurpose)255));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public void SequentialCommitsAndReopensPreserveRevision()
    {
        var store=PairingStore.OpenAt(_root);var current=Active(store);
        for(int i=0;i<100;i++)
        {
            store=PairingStore.OpenAt(_root);
            var nextMode=current.Mode==AccessMode.Safe?AccessMode.ReadOnly:AccessMode.Safe;
            current=store.ApplyVerifiedSession(current,current.DeviceId,Client,nextMode);
            Assert.AreEqual((ulong)i+3,PairingStore.OpenAt(_root).Load(current.DeviceId).Revision);
        }
    }

    [TestMethod]
    public void TemporaryReaderBlockingReplaceIsRetriedWithoutDroppingOldRecord()
    {
        var active=Active(PairingStore.OpenAt(_root));FileStream? reader=null;int observed=0;
        void ReleaseReader(object? _,System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if(args.Exception is IOException io && io.HResult is unchecked((int)0x80070497) or unchecked((int)0x80070020))
            { Interlocked.Increment(ref observed);reader?.Dispose();reader=null; }
        }
        var store=PairingStore.OpenAt(_root,s=>{ if(s==CommitStage.BeforeCommit)reader=File.Open(RecordPath,FileMode.Open,FileAccess.Read,FileShare.Read); });
        AppDomain.CurrentDomain.FirstChanceException+=ReleaseReader;
        try
        {
            var revoked=store.BeginRevocation(active);
            Assert.IsGreaterThan(0,observed);Assert.AreEqual(PairingRecordState.RevocationPending,revoked.State);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException-=ReleaseReader;reader?.Dispose(); }
    }

    [TestMethod]
    public void PersistentReaderBlockingReplaceFailsClosedWithOldRecordIntact()
    {
        var active=Active(PairingStore.OpenAt(_root));var before=Hash(File.ReadAllBytes(RecordPath));FileStream? reader=null;
        var store=PairingStore.OpenAt(_root,s=>{ if(s==CommitStage.BeforeCommit)reader=File.Open(RecordPath,FileMode.Open,FileAccess.Read,FileShare.Read); });
        try { Error(StoreError.IoFailure,()=>store.BeginRevocation(active)); }
        finally { reader?.Dispose(); }
        Error(StoreError.NeedsRepair,()=>store.OpenCredential(active.DeviceId,Client,CredentialPurpose.Mount));
        Assert.AreEqual(before,Hash(File.ReadAllBytes(RecordPath)));
        Assert.AreEqual(PairingRecordState.Active,PairingStore.OpenAt(_root).Load(active.DeviceId).State);
    }

    [TestMethod]
    public void TemporaryLeaseConflictCanRecoverWithinBoundedWait()
    {
        Pending();var held=PrivateFiles.Acquire(_root,_root,false);
        var releaser=new Thread(()=>{ Thread.Sleep(75);held.Dispose(); });releaser.Start();
        try { Assert.AreEqual(PairingRecordState.Pending,PairingStore.OpenAt(_root).Load(_identity.DeviceId).State); }
        finally { Assert.IsTrue(releaser.Join(5000)); }
    }

    [TestMethod]
    public void DuplicateMissingAndDifferentClientNeverCreateTrust()
    {
        var store=PairingStore.OpenAt(_root);Error(StoreError.Missing,()=>store.Load(_identity.DeviceId));Assert.IsFalse(Directory.Exists(_root));
        var p=Pending(store);var before=Hash(File.ReadAllBytes(RecordPath));Error(StoreError.AlreadyExists,()=>Pending(store));
        Assert.AreEqual(before,Hash(File.ReadAllBytes(RecordPath)));
        Error(StoreError.IdentityMismatch,()=>store.OpenCredential(p.DeviceId,new string('0',32),CredentialPurpose.SessionValidation));
        File.Delete(RecordPath);Error(StoreError.Missing,()=>store.Load(p.DeviceId));Assert.IsFalse(File.Exists(RecordPath));
    }

    [TestMethod]
    [DataRow("wrong-magic")] [DataRow("version")] [DataRow("revision")] [DataRow("state")] [DataRow("mode")]
    [DataRow("ca-hash")] [DataRow("device")] [DataRow("utf8")] [DataRow("trailing")] [DataRow("short")]
    public void ProtectedMalformedRecordFailsClosed(string mutation)
    {
        var store=PairingStore.OpenAt(_root);Pending(store);var plain=CurrentUserProtection.Unprotect(File.ReadAllBytes(RecordPath),_identity.DeviceId);
        try
        {
            int n=BinaryPrimitives.ReadUInt16BigEndian(plain.AsSpan(15));
            switch(mutation)
            {
                case "wrong-magic":plain[0]^=1;break;case "version":plain[4]=2;break;case "revision":Array.Clear(plain,5,8);break;
                case "state":plain[13]=255;break;case "mode":plain[14]=255;break;case "ca-hash":plain[17+n]^=1;break;
                case "device":plain[49+n]^=1;break;case "utf8":plain[^1]=255;break;
                case "trailing":var longer=new byte[plain.Length+1];plain.CopyTo(longer,0);CryptographicOperations.ZeroMemory(plain);plain=longer;break;
                case "short":CryptographicOperations.ZeroMemory(plain);plain=[1];break;
            }
            File.WriteAllBytes(RecordPath,CurrentUserProtection.Protect(plain,_identity.DeviceId));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var before=Hash(File.ReadAllBytes(RecordPath));Error(StoreError.NeedsRepair,()=>store.Load(_identity.DeviceId));
        Error(StoreError.NeedsRepair,()=>store.OpenCredential(_identity.DeviceId,Client,CredentialPurpose.Mount));
        Assert.AreEqual(before,Hash(File.ReadAllBytes(RecordPath)));
    }

    [TestMethod]
    [DataRow("mutation")] [DataRow("empty")] [DataRow("oversized")] [DataRow("foreign-binding")]
    public void CiphertextFailureNeverFallsBack(string kind)
    {
        var store=PairingStore.OpenAt(_root);Pending(store);var bytes=File.ReadAllBytes(RecordPath);
        if(kind=="mutation") { bytes[^1]^=1;File.WriteAllBytes(RecordPath,bytes); }
        if(kind=="empty")File.WriteAllBytes(RecordPath,[]);
        if(kind=="oversized")File.WriteAllBytes(RecordPath,new byte[65537]);
        if(kind=="foreign-binding")File.WriteAllBytes(RecordPath,CurrentUserProtection.Protect("synthetic"u8.ToArray(),"pbng-"+new string('0',64)));
        Error(StoreError.NeedsRepair,()=>store.Load(_identity.DeviceId));
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void FailedCreateDoesNotExposeToken(int stageValue)
    {
        var stage=(CommitStage)stageValue;
        var store=PairingStore.OpenAt(_root,s=>{ if(s==stage)throw new IOException("synthetic failure must not escape"); });
        Error(StoreError.IoFailure,()=>Pending(store));Error(StoreError.NeedsRepair,()=>store.OpenCredential(_identity.DeviceId,Client,CredentialPurpose.PairingSubmission));
        var fresh=PairingStore.OpenAt(_root);
        if(stage==CommitStage.Committed)Assert.AreEqual(PairingRecordState.Pending,fresh.Load(_identity.DeviceId).State);
        else Error(StoreError.Missing,()=>fresh.Load(_identity.DeviceId));
        Assert.IsEmpty(Directory.GetFiles(_root,".pending-*.tmp"));
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void FailedReplacementHasObservableDiskState(int stageValue)
    {
        var stage=(CommitStage)stageValue;
        var first=PairingStore.OpenAt(_root);var active=Active(first);
        var store=PairingStore.OpenAt(_root,s=>{ if(s==stage)throw new IOException(); });Error(StoreError.IoFailure,()=>store.BeginRevocation(active));
        Error(StoreError.NeedsRepair,()=>store.OpenCredential(active.DeviceId,Client,CredentialPurpose.Mount));
        var observed=PairingStore.OpenAt(_root).Load(active.DeviceId);
        Assert.AreEqual(stage==CommitStage.Committed?PairingRecordState.RevocationPending:PairingRecordState.Active,observed.State);
    }

    [TestMethod]
    [DataRow("directory")] [DataRow("record")] [DataRow("lock")]
    public void BroadAclRejectedWithoutSilentRepair(string kind)
    {
        Pending();var everyone=new SecurityIdentifier(WellKnownSidType.WorldSid,null);
        if(kind=="directory")
        {
            var dir=new DirectoryInfo(_root);var acl=dir.GetAccessControl();acl.AddAccessRule(new FileSystemAccessRule(everyone,FileSystemRights.Read,AccessControlType.Allow));dir.SetAccessControl(acl);
        }
        else
        {
            var file=new FileInfo(kind=="record"?RecordPath:Path.Combine(_root,".store.lock"));var acl=file.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(everyone,FileSystemRights.Read,AccessControlType.Allow));file.SetAccessControl(acl);
        }
        Error(StoreError.UnsafeLocation,()=>PairingStore.OpenAt(_root).Load(_identity.DeviceId));
    }

    [TestMethod]
    public void AclHasOnlyUserAndSystemAndParentsStayPinned()
    {
        Pending();var acl=new DirectoryInfo(_root).GetAccessControl();Assert.IsTrue(acl.AreAccessRulesProtected);
        Assert.HasCount(2,acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray());
        using var pin=PrivateFiles.Acquire(_root,_root,false);
        Assert.Throws<IOException>(()=>Directory.Move(_base,_base+"-renamed"));
        Error(StoreError.Busy,()=>PairingStore.OpenAt(_root).Load(_identity.DeviceId));
    }

    [TestMethod]
    public void HardLinkAndDirectoryAtRecordAreRejected()
    {
        Pending();var link=Path.Combine(_base,"record-link");Assert.IsTrue(Native.CreateHardLinkW(link,RecordPath,0));
        try { Error(StoreError.UnsafeLocation,()=>PairingStore.OpenAt(_root).Load(_identity.DeviceId)); }
        finally { File.Delete(link); }
        File.Delete(RecordPath);Directory.CreateDirectory(RecordPath);
        Error(StoreError.UnsafeLocation,()=>PairingStore.OpenAt(_root).Load(_identity.DeviceId));
    }

    [TestMethod]
    public void JunctionRootAndAncestorAreRejected()
    {
        Pending();var link=Path.Combine(_base,"junction");var start=new ProcessStartInfo("cmd.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("/d");start.ArgumentList.Add("/c");start.ArgumentList.Add("mklink");start.ArgumentList.Add("/J");start.ArgumentList.Add(link);start.ArgumentList.Add(_root);
        using(var process=Process.Start(start)!)
        {
            try { Assert.IsTrue(process.WaitForExit(10000));Assert.AreEqual(0,process.ExitCode); }
            finally { if(!process.HasExited) { process.Kill();Assert.IsTrue(process.WaitForExit(5000)); } }
        }
        try
        {
            Error(StoreError.UnsafeLocation,()=>PairingStore.OpenAt(link).Load(_identity.DeviceId));
            Error(StoreError.UnsafeLocation,()=>PairingStore.OpenAt(Path.Combine(link,"child")).CreatePending(_identity,Client,"phone","pc"));
            Assert.IsFalse(Directory.Exists(Path.Combine(_root,"child")));
        }
        finally { Assert.IsTrue(Path.GetFullPath(link).StartsWith(_run+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));Directory.Delete(link,false); }
    }

    [TestMethod]
    [DataRow("short-id")] [DataRow("bad-name")] [DataRow("format-name")] [DataRow("long-name")] [DataRow("bad-utf16")]
    public void InvalidInputNeverWrites(string kind)
    {
        string client=kind=="short-id"?"ab":Client;
        string name=kind switch { "bad-name"=>"a\nb","format-name"=>"a\u200bb","long-name"=>new string('字',129),"bad-utf16"=>"\ud800",_=>"phone" };
        Error(StoreError.InvalidInput,()=>PairingStore.OpenAt(_root).CreatePending(_identity,client,name,"pc"));Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    [DataRow("weak")] [DataRow("leaf")] [DataRow("usage")] [DataRow("expired")] [DataRow("future")] [DataRow("critical")] [DataRow("algorithm")]
    [DataRow("signature")] [DataRow("trailing")] [DataRow("malformed")]
    public void InvalidCaRejected(string kind)
    {
        var bytes=kind switch { "weak"=>Certificate(bits:1024),"leaf"=>Certificate(ca:false),"usage"=>Certificate(signing:false),
            "expired"=>Certificate(validity:-1),"future"=>Certificate(validity:1),"critical"=>Certificate(critical:true),
            "algorithm"=>Certificate(differentAlgorithm:true),_=>_identity.CertificateDer };
        if(kind=="signature")bytes[^1]^=1;if(kind=="trailing")bytes=[..bytes,0];if(kind=="malformed")bytes=[0x30,0];
        Error(StoreError.InvalidIdentity,()=>ValidatedDeviceIdentity.Validate(bytes,Hash(bytes)));
    }

    [TestMethod]
    public void FingerprintMismatchAndArrayOwnership()
    {
        var der=_identity.CertificateDer;Error(StoreError.IdentityMismatch,()=>ValidatedDeviceIdentity.Validate(der,new string('0',64)));
        der[0]^=1;Assert.AreEqual(_identity.Sha256,Hash(_identity.CertificateDer));
        Error(StoreError.InvalidInput,()=>PairingStore.OpenAt(_root).Load("../../other"));
    }

    private static Process Fixture(params string[] args)
    {
        var output=new DirectoryInfo(AppContext.BaseDirectory);
        var dll=Path.Combine(output.Parent!.Parent!.Parent!.Parent!.FullName,"PhoneBridge.CredentialsFixture","bin",output.Parent.Name,output.Name,"PhoneBridge.CredentialsFixture.dll");
        var dotnet=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,"../../..","dotnet.exe"));
        var start=new ProcessStartInfo(dotnet)
        { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(dll);foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)!;
    }

    private static async Task<(string Text,int ExitCode)> CaptureFixture(params string[] args)
    {
        using var process=Fixture(args);
        try
        {
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual("",await error.WaitAsync(TimeSpan.FromSeconds(5)));
            return ((await output.WaitAsync(TimeSpan.FromSeconds(5))).Trim(),process.ExitCode);
        }
        finally
        {
            if(!process.HasExited) { process.Kill();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }

    [TestMethod]
    public async Task NewProcessReadsSameCredentialAndAnonymousCannotDecrypt()
    {
        var p=Pending();using var lease=PairingStore.OpenAt(_root).OpenCredential(p.DeviceId,Client,CredentialPurpose.SessionValidation);
        var token=lease.CopyToken();var expected=Hash(token);CryptographicOperations.ZeroMemory(token);
        var read=await CaptureFixture("read",_root,_publicCa,"none");
        Assert.AreEqual("OK|Pending|1|"+expected,read.Text);Assert.AreEqual(0,read.ExitCode);
        var anonymous=await CaptureFixture("anonymous");
        Assert.AreEqual("ANONYMOUS_REFUSED",anonymous.Text);Assert.AreEqual(0,anonymous.ExitCode);
    }

    [TestMethod]
    public async Task CrossProcessLeaseRefusesConcurrentAccess()
    {
        Pending();using var lease=PrivateFiles.Acquire(_root,_root,false);
        var result=await CaptureFixture("read",_root,_publicCa,"none");
        Assert.AreEqual("ERROR|Busy",result.Text);Assert.AreEqual(2,result.ExitCode);
    }

    [TestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(4)]
    public async Task ProcessDeathDuringCommitKeepsAValidRecord(int stageValue)
    {
        var stage=(CommitStage)stageValue;
        var metadata=Active(PairingStore.OpenAt(_root));using var process=Fixture("pause",_root,_publicCa,stage.ToString());
        try
        {
            Assert.AreEqual("READY",await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            process.Kill();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if(!process.HasExited) { process.Kill();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
        var recovered=PairingStore.OpenAt(_root).Load(metadata.DeviceId);
        Assert.AreEqual(stage==CommitStage.Committed?PairingRecordState.RevocationPending:PairingRecordState.Active,recovered.State);
        foreach(var path in Directory.GetFiles(_root,".pending-*.tmp")) Assert.IsLessThanOrEqualTo(65536L,new FileInfo(path).Length);
    }

    private static class Native
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateHardLinkW(string link,string existing,nint security);
    }
}
