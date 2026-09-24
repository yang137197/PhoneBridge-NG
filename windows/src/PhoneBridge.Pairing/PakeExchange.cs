using Org.BouncyCastle.Crypto.Agreement.JPake;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using System.Security.Cryptography;

namespace PhoneBridge.Pairing;

internal sealed class PakeExchange : IDisposable
{
    private JPakeParticipant? _participant;
    private JPakeRound1Payload? _ownFirst, _peerFirst;
    private JPakeRound2Payload? _ownSecond;
    private JPakeRound3Payload? _ownThird;
    private BigInteger? _key;
    private readonly string _own, _peer;

    internal PakeExchange(string own, string peer, char[] code)
    {
        _own = own; _peer = peer;
        _participant = new JPakeParticipant(own, code, JPakePrimeOrderGroups.NIST_3072, new Sha256Digest(), new SecureRandom());
    }

    internal byte[] Round1()
    {
        _ownFirst ??= _participant!.CreateRound1PayloadToSend();
        var p = _ownFirst;
        return Join(U(p.Gx1,384), U(p.Gx2,384), U(p.KnowledgeProofForX1[0],384), U(p.KnowledgeProofForX1[1],32),
            U(p.KnowledgeProofForX2[0],384), U(p.KnowledgeProofForX2[1],32));
    }

    internal void AcceptRound1(byte[] b)
    {
        _ = Round1();
        var p = new JPakeRound1Payload(_peer, Element(b,0), Element(b,384,true),
            [Element(b,768), Scalar(b,1152)], [Element(b,1184), Scalar(b,1568)]);
        _participant!.ValidateRound1PayloadReceived(p); _peerFirst = p;
    }

    internal byte[] Round2()
    {
        _ownSecond ??= _participant!.CreateRound2PayloadToSend();
        return Join(U(_ownSecond.A,384), U(_ownSecond.KnowledgeProofForX2s[0],384), U(_ownSecond.KnowledgeProofForX2s[1],32));
    }

    internal void AcceptRound2(byte[] b)
    {
        _ = Round2();
        _participant!.ValidateRound2PayloadReceived(new JPakeRound2Payload(_peer, Element(b,0,true), [Element(b,384), Scalar(b,768)]));
    }

    internal byte[] Round3()
    {
        _key ??= _participant!.CalculateKeyingMaterial();
        _ownThird ??= _participant!.CreateRound3PayloadToSend(_key);
        return S(_ownThird.MacTag);
    }

    internal void AcceptRound3(byte[] b)
    {
        _ = Round3();
        var expected = JPakeUtilities.CalculateMacTag(_peer, _own, _peerFirst!.Gx1, _peerFirst.Gx2,
            _ownFirst!.Gx1, _ownFirst.Gx2, _key!, new Sha256Digest());
        var tag = S(expected);
        try { if (!CryptographicOperations.FixedTimeEquals(tag,b)) throw new PairingProtocolException(); }
        finally { CryptographicOperations.ZeroMemory(tag); }
        _participant!.ValidateRound3PayloadReceived(new JPakeRound3Payload(_peer,new BigInteger(b)),_key!);
    }

    internal byte[] ExportKey() => U(_key ?? throw new PairingProtocolException(),384);
    private static BigInteger Element(byte[] b, int offset, bool notOne = false)
    {
        var v = new BigInteger(1,b.AsSpan(offset,384));
        if (v.SignValue <= 0 || v.CompareTo(JPakePrimeOrderGroups.NIST_3072.P) >= 0 || (notOne && v.Equals(BigInteger.One)))
            throw new PairingProtocolException();
        return v;
    }
    private static BigInteger Scalar(byte[] b, int offset)
    {
        var v = new BigInteger(1,b.AsSpan(offset,32));
        if (v.CompareTo(JPakePrimeOrderGroups.NIST_3072.Q) >= 0) throw new PairingProtocolException();
        return v;
    }
    private static byte[] U(BigInteger v,int length)
    {
        byte[] bytes = v.ToByteArrayUnsigned();
        if (v.SignValue < 0 || bytes.Length > length) throw new PairingProtocolException();
        var output = new byte[length]; bytes.CopyTo(output,length-bytes.Length);
        CryptographicOperations.ZeroMemory(bytes); return output;
    }
    private static byte[] S(BigInteger v)
    {
        byte[] bytes = v.ToByteArray();
        if (bytes.Length > 32) throw new PairingProtocolException();
        var output = Enumerable.Repeat(v.SignValue < 0 ? (byte)255 : (byte)0,32).ToArray();
        bytes.CopyTo(output,32-bytes.Length); return output;
    }
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(p => p).ToArray();
    public void Dispose()
    {
        // BC exposes no abort/zeroize API; release references, never reuse a failed participant.
        _participant = null; _key = null; _ownFirst = _peerFirst = null; _ownSecond = null; _ownThird = null;
    }
}
