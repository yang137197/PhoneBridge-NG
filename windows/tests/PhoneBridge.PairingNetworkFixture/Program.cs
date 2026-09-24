using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using PhoneBridge.Pairing;
using PhoneBridge.Credentials;

// Isolated emulator bridge only. Secrets arrive through stdin, never command arguments or logs.
string stage="input";
try
{
    using var input=JsonDocument.Parse(Console.ReadLine()??throw new InvalidDataException());
    var setup=input.RootElement;
    var code=setup.GetProperty("code").GetString()!.ToCharArray();
    var window=Convert.FromHexString(setup.GetProperty("window").GetString()!);
    int pairPort=setup.GetProperty("pair_port").GetInt32(),forwardedHttps=setup.GetProperty("https_port").GetInt32();
    int deviceHttps=setup.GetProperty("device_https_port").GetInt32();
    string runRoot=Path.GetFullPath(setup.GetProperty("store_root").GetString()!);
    string allowed=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,".audit","p1-007"))+Path.DirectorySeparatorChar;
    if(!runRoot.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||window.Length!=16||pairPort is <1 or >65535||forwardedHttps is <1 or >65535)throw new InvalidDataException();
    stage="pake";
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var tcp=new TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback,pairPort,timeout.Token);
    using var session=PairingSession.CreateWindows(window,code);Array.Clear(code);
    var stream=tcp.GetStream();
    foreach(byte expected in new byte[]{2,0x12,0x22,0x32})
    {
        stage=$"pake_frame_{expected:x2}";
        using var frameTimeout=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);frameTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] sent=session.CreateNextFrame(timeout.Token);
        await stream.WriteAsync(sent,frameTimeout.Token);
        if(expected==0x32)tcp.Client.Shutdown(SocketShutdown.Send);
        byte[] header=new byte[9];await stream.ReadExactlyAsync(header,frameTimeout.Token);
        var accumulator=new FrameAccumulator(expected);accumulator.Feed(header);
        int length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5)));
        if(length>8192)throw new InvalidDataException();
        byte[] payload=new byte[length];await stream.ReadExactlyAsync(payload,frameTimeout.Token);accumulator.Feed(payload);
        session.AcceptFrame(accumulator.GetFrame(),timeout.Token);
    }
    if(await stream.ReadAsync(new byte[1],timeout.Token)!=0)throw new InvalidDataException();
    using var confirmed=session.TakeConfirmation(timeout.Token);
    if(confirmed.HttpsPort!=deviceHttps)throw new InvalidDataException();
    string clientId=Convert.ToHexStringLower(confirmed.ClientId),attempt=Convert.ToHexStringLower(confirmed.AttemptId);
    byte[] ca=confirmed.CandidateCaDer;
    var identity=ValidatedDeviceIdentity.Validate(ca,Convert.ToHexStringLower(SHA256.HashData(ca)));
    using var anchor=X509CertificateLoader.LoadCertificate(ca);
    var policy=new X509ChainPolicy{TrustMode=X509ChainTrustMode.CustomRootTrust,RevocationMode=X509RevocationMode.NoCheck,DisableCertificateDownloads=true};
    policy.CustomTrustStore.Add(anchor);policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
    using var handler=new SocketsHttpHandler{
        UseProxy=false,UseCookies=false,AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(5),MaxResponseHeadersLength=16,
        SslOptions=new SslClientAuthenticationOptions{CertificateChainPolicy=policy}
    };
    using var http=new HttpClient(handler){BaseAddress=new Uri($"https://127.0.0.1:{forwardedHttps}"),Timeout=TimeSpan.FromSeconds(10)};
    stage="pending_storage";
    var store=PairingStore.OpenAt(runRoot);
    var record=store.CreatePending(identity,clientId,"Synthetic Android","Synthetic Windows");
    using var credential=store.OpenCredential(record.DeviceId,clientId,CredentialPurpose.PairingSubmission);
    byte[] token=credential.CopyToken(),grant=confirmed.CopyGrant();
    try
    {
        string bearer=Encode(grant),password=Encode(token);
        string basic=Convert.ToBase64String(Encoding.ASCII.GetBytes($"pbng-{clientId}:{password}"));
        byte[] submission=JsonSerializer.SerializeToUtf8Bytes(new {client_id=clientId,client_name="Synthetic Windows",credential=password});
        try
        {
            stage="https_submission";
            var submitted=await Request(http,HttpMethod.Post,$"/phonebridge/v1/pairing/{attempt}","Bearer",bearer,submission);
            if(submitted.Status!=202)throw new InvalidDataException();
            using(var pending=JsonDocument.Parse(submitted.Body))
            {
                Exact(pending.RootElement,"attempt_id","client_id","state");
                if(pending.RootElement.GetProperty("client_id").GetString()!=clientId||pending.RootElement.GetProperty("attempt_id").GetString()!=attempt||pending.RootElement.GetProperty("state").GetString()!="PendingApproval")throw new InvalidDataException();
            }
            Emit(new {event_name="pending",client_id=clientId,attempt_id=attempt,ca_sha256=identity.Sha256,windows_state=record.State.ToString()});
            while(Console.ReadLine() is { } line)
            {
                using var command=JsonDocument.Parse(line);string action=command.RootElement.GetProperty("action").GetString()!;stage=action;
                if(action=="exit")break;
                switch(action)
                {
                    case "session":
                        var result=await Request(http,HttpMethod.Get,"/phonebridge/v1/session","Basic",basic);
                        if(result.Status==200)
                        {
                            using var body=JsonDocument.Parse(result.Body);var value=body.RootElement;Exact(value,"device_id","client_id","mode","share_ready");
                            if(!value.GetProperty("share_ready").GetBoolean())throw new InvalidDataException();
                            AccessMode mode=value.GetProperty("mode").GetString() switch {"readOnly"=>AccessMode.ReadOnly,"safe"=>AccessMode.Safe,"readWrite"=>AccessMode.ReadWrite,_=>throw new InvalidDataException()};
                            record=store.ApplyVerifiedSession(record,value.GetProperty("device_id").GetString()!,value.GetProperty("client_id").GetString()!,mode);
                        }
                        Emit(new {event_name="session",status=result.Status,windows_state=store.Load(record.DeviceId).State.ToString()});break;
                    case "read":
                        var read=await Request(http,HttpMethod.Get,"/phonebridge-p1-007-origin.txt","Basic",basic);
                        if(read.Status!=200||Encoding.UTF8.GetString(read.Body)!="phonebridge-p1-007-synthetic")throw new InvalidDataException();
                        Emit(new {event_name="read",status=read.Status,sha256=Convert.ToHexStringLower(SHA256.HashData(read.Body))});break;
                    case "revoke":
                        record=store.BeginRevocation(record);
                        var revoked=await Request(http,HttpMethod.Delete,"/phonebridge/v1/pairings/self","Basic",basic);
                        if(revoked.Status!=204||revoked.Body.Length!=0)throw new InvalidDataException();
                        Emit(new {event_name="revoke",status=revoked.Status,windows_can_mount=store.Load(record.DeviceId).CanMount});break;
                    case "grant":
                        var grantState=await Request(http,HttpMethod.Get,$"/phonebridge/v1/pairing/{attempt}","Bearer",bearer);
                        Emit(new {event_name="grant",status=grantState.Status});break;
                    case "failure":
                        try {
                            var failure=await Request(http,HttpMethod.Get,"/phonebridge/v1/session","Basic",basic);
                            if(failure.Status!=503)throw new InvalidDataException();
                            Emit(new {event_name="failure",status=failure.Status,closed=false});
                        } catch(HttpRequestException) { Emit(new {event_name="failure",status=0,closed=true}); }
                        catch(IOException) { Emit(new {event_name="failure",status=0,closed=true}); }
                        break;
                    default:throw new InvalidDataException();
                }
            }
        }
        finally{CryptographicOperations.ZeroMemory(submission);}
    }
    finally{CryptographicOperations.ZeroMemory(token);CryptographicOperations.ZeroMemory(grant);}
    return 0;
}
catch(Exception error)
{
    Emit(new {event_name="failed",stage,error_type=error.GetType().Name});return 1;
}

static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
static void Emit(object value)=>Console.WriteLine(JsonSerializer.Serialize(value));
static void Exact(JsonElement value,params string[] names)
{
    if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException();
    var fields=value.EnumerateObject().Select(x=>x.Name).ToArray();
    if(fields.Length!=names.Length||fields.Distinct(StringComparer.Ordinal).Count()!=names.Length||fields.Except(names,StringComparer.Ordinal).Any())throw new InvalidDataException();
}
static async Task<(int Status,byte[] Body)> Request(HttpClient http,HttpMethod method,string path,string scheme,string auth,byte[]? body=null)
{
    using var request=new HttpRequestMessage(method,path){Version=HttpVersion.Version11,VersionPolicy=HttpVersionPolicy.RequestVersionExact};
    request.Headers.Authorization=new AuthenticationHeaderValue(scheme,auth);
    if(body!=null){request.Content=new ByteArrayContent(body);request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");}
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
    if(response.Headers.CacheControl?.NoStore!=true)throw new InvalidDataException();
    await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);
    byte[] buffer=new byte[4097];int count=0;
    while(count<buffer.Length){int read=await input.ReadAsync(buffer.AsMemory(count),deadline.Token);if(read==0)break;count+=read;}
    if(count>4096)throw new InvalidDataException();
    return ((int)response.StatusCode,buffer[..count]);
}
