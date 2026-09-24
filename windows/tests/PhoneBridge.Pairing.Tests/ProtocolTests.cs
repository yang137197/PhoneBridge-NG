using System.Buffers.Binary;
using PhoneBridge.Pairing;

namespace PhoneBridge.Pairing.Tests;

[TestClass]
public sealed class ProtocolTests
{
    private static readonly byte[] Window = Enumerable.Range(0,16).Select(i=>(byte)i).ToArray();
    private static PairingSession W(string code="00123456") => PairingSession.CreateWindows(Window,code.ToCharArray());
    private static PairingSession A(string code="00123456") => PairingSession.CreateAndroid(Window,code.ToCharArray(),8273,[0x30,0]);
    private static void Exchange(PairingSession w,PairingSession a,int count=8)
    { for (int i=0;i<count;i++) { var sender=i%2==0?w:a; var receiver=i%2==0?a:w; receiver.AcceptFrame(sender.CreateNextFrame()); } }
    private static byte[] Frame(byte type,int size)
    {
        var b=new byte[9+size]; "PBP1"u8.CopyTo(b);b[4]=type; BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(5),(uint)size); return b;
    }

    [TestMethod]
    [DataRow("00123456")]
    [DataRow("00000000")]
    public void FullHandshakeBindsIdentityAndGrant(string code)
    {
        using var w=W(code);using var a=A(code);Exchange(w,a);
        using var x=w.TakeConfirmation();using var y=a.TakeConfirmation();
        CollectionAssert.AreEqual(x.CopyGrant(),y.CopyGrant());CollectionAssert.AreEqual(x.ClientId,y.ClientId);
        CollectionAssert.AreEqual(x.AttemptId,y.AttemptId);CollectionAssert.AreEqual(new byte[]{0x30,0},x.CandidateCaDer);
        Assert.AreEqual((ushort)8273,x.HttpsPort);Assert.AreEqual(PairingState.Consumed,w.State);
        var copy=x.CopyGrant();copy[0]^=255;CollectionAssert.AreNotEqual(copy,x.CopyGrant());
        x.Dispose();Assert.Throws<ObjectDisposedException>(()=>x.CopyGrant());
        Assert.Throws<PairingProtocolException>(()=>w.TakeConfirmation());
    }

    [TestMethod]
    public void WrongCodeNeverYieldsConfirmation()
    { using var w=W();using var a=A("00123457"); Assert.Throws<PairingProtocolException>(()=>Exchange(w,a)); Assert.Throws<PairingProtocolException>(()=>a.TakeConfirmation()); }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(3)] [DataRow(6)] [DataRow(7)]
    public void EarlyResultTerminatesSession(int steps)
    { using var w=W();using var a=A();Exchange(w,a,steps);Assert.Throws<PairingProtocolException>(()=>w.TakeConfirmation());Assert.AreEqual(PairingState.Failed,w.State); }

    [TestMethod]
    public void WrongDirectionAndReuseFail()
    { using var a=A();Assert.Throws<PairingProtocolException>(()=>a.CreateNextFrame());Assert.Throws<PairingProtocolException>(()=>a.AcceptFrame(Frame(1,33))); }

    [TestMethod]
    public void CancellationTerminatesAtBoundary()
    { using var w=W();Assert.Throws<PairingProtocolException>(()=>w.CreateNextFrame(new CancellationToken(true)));Assert.AreEqual(PairingState.Failed,w.State); }

    [TestMethod]
    public void ExtraFrameAfterConfirmationInvalidatesUntakenResult()
    { using var w=W();using var a=A();Exchange(w,a);Assert.Throws<PairingProtocolException>(()=>w.AcceptFrame(Frame(0x32,32)));Assert.Throws<PairingProtocolException>(()=>w.TakeConfirmation()); }

    [TestMethod]
    public void ZeroServerPortRejected()
    { using var w=W();using var a=A();Exchange(w,a,1);var frame=a.CreateNextFrame();frame[58]=frame[59]=0;Assert.Throws<PairingProtocolException>(()=>w.AcceptFrame(frame)); }

    [TestMethod]
    [DataRow(1,33)] [DataRow(2,54)] [DataRow(2,4149)] [DataRow(17,1600)] [DataRow(18,1600)]
    [DataRow(33,800)] [DataRow(34,800)] [DataRow(49,32)] [DataRow(50,32)]
    public void EveryFrameSupportsByteFragments(int type,int size)
    {
        var frame=Frame((byte)type,size);var reader=new FrameAccumulator((byte)type);
        foreach(var b in frame) reader.Feed([b]);Assert.IsTrue(reader.IsComplete);CollectionAssert.AreEqual(frame,reader.GetFrame());
        Assert.Throws<PairingProtocolException>(()=>reader.Feed([0]));Assert.IsFalse(reader.IsComplete);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(4)] [DataRow(8)] [DataRow(9)] [DataRow(20)]
    public void TruncatedFrameFailsPermanently(int length)
    { var reader=new FrameAccumulator(1);reader.Feed(Frame(1,33).AsSpan(0,length));Assert.Throws<PairingProtocolException>(()=>reader.EndOfInput());Assert.Throws<PairingProtocolException>(()=>reader.Feed([0])); }

    [TestMethod]
    [DataRow(0)] [DataRow(32)] [DataRow(34)] [DataRow(8193)] [DataRow(-1)]
    public void InvalidLengthRejectedFromHeaderBeforePayload(int size)
    { var header=Frame(1,0);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5),unchecked((uint)size));var reader=new FrameAccumulator(1);Assert.Throws<PairingProtocolException>(()=>reader.Feed(header)); }

    [TestMethod]
    public void MagicTypeTrailingAndDuplicateRejected()
    {
        var frame=Frame(1,33);var bad=(byte[])frame.Clone();bad[0]^=1;
        Assert.Throws<PairingProtocolException>(()=>new FrameAccumulator(1).Feed(bad));
        Assert.Throws<PairingProtocolException>(()=>new FrameAccumulator(2).Feed(frame));
        Assert.Throws<PairingProtocolException>(()=>new FrameAccumulator(1).Feed([..frame,0]));
        using var w=W();using var a=A();var hello=w.CreateNextFrame();a.AcceptFrame(hello);
        Assert.Throws<PairingProtocolException>(()=>a.AcceptFrame(hello));
    }

    [TestMethod]
    [DataRow(9)] [DataRow(10)] [DataRow(26)] [DataRow(60)] [DataRow(61)]
    public void InvalidServerHelloFails(int offset)
    { using var w=W();using var a=A();Exchange(w,a,1);var hello=a.CreateNextFrame();hello[offset]^=1;Assert.Throws<PairingProtocolException>(()=>w.AcceptFrame(hello)); }

    [TestMethod]
    [DataRow(42)] [DataRow(58)] [DataRow(62)]
    public void BoundServerHelloChangeFailsAtProof(int offset)
    { using var w=W();using var a=A();Exchange(w,a,1);var hello=a.CreateNextFrame();hello[offset]^=1;w.AcceptFrame(hello);Assert.Throws<PairingProtocolException>(()=>a.AcceptFrame(w.CreateNextFrame())); }

    [TestMethod]
    public void CrossSessionReplayAndReflectionFail()
    {
        using var w=W();using var a=A();Exchange(w,a,2);var r1=w.CreateNextFrame();a.AcceptFrame(r1);var peer=a.CreateNextFrame();
        var reflection=(byte[])r1.Clone();reflection[4]=0x12;Assert.Throws<PairingProtocolException>(()=>w.AcceptFrame(reflection));
        using var w2=W();using var a2=A();Exchange(w2,a2,2);_=w2.CreateNextFrame();Assert.Throws<PairingProtocolException>(()=>w2.AcceptFrame(peer));
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public void InvalidGroupOrScalarRejected(int kind)
    {
        using var w=W();using var a=A();Exchange(w,a,2);var r1=w.CreateNextFrame();
        if(kind<3) { Array.Clear(r1,393,384);if(kind==1)r1[776]=1;if(kind==2)Array.Fill(r1,(byte)255,393,384); }
        else Array.Fill(r1,(byte)255,1161,32);
        Assert.Throws<PairingProtocolException>(()=>a.AcceptFrame(r1));Assert.AreEqual(PairingState.Failed,a.State);
    }

    [TestMethod]
    [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)]
    public void ModifiedProofOrMacRejected(int step)
    {
        using var w=W();using var a=A();Exchange(w,a,step);var sender=step%2==0?w:a;var receiver=step%2==0?a:w;
        var frame=sender.CreateNextFrame();frame[^1]^=1;Assert.Throws<PairingProtocolException>(()=>receiver.AcceptFrame(frame));
    }

    [TestMethod]
    public void InvalidInputsAndClosedSessionsRejected()
    {
        Assert.Throws<PairingProtocolException>(()=>W("１２３４５６７８"));
        Assert.Throws<PairingProtocolException>(()=>PairingSession.CreateAndroid(Window,"00123456".ToCharArray(),0,[1]));
        Assert.Throws<PairingProtocolException>(()=>PairingSession.CreateAndroid(Window,"00123456".ToCharArray(),1,new byte[4097]));
        using var w=W();w.Dispose();Assert.Throws<PairingProtocolException>(()=>w.CreateNextFrame());
    }
}
