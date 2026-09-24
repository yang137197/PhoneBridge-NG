// Offline synthetic library interop only; not a product pairing implementation.
using Org.BouncyCastle.Crypto.Agreement.JPake;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using System.Security.Cryptography;

try
{
    if (args.Length != 2 || args[0] is not ("00000000" or "00123456" or "00123457") || args[1].Length != 64) return 3;
    var own = "pbng-pair-v1:W:" + args[1];
    var peer = "pbng-pair-v1:A:" + args[1];
    var participant = new JPakeParticipant(own, args[0].ToCharArray(), JPakePrimeOrderGroups.NIST_3072, new Sha256Digest(), new Org.BouncyCastle.Security.SecureRandom());
    var first = participant.CreateRound1PayloadToSend();
    Send(Join(U(first.Gx1,384), U(first.Gx2,384), U(first.KnowledgeProofForX1[0],384), U(first.KnowledgeProofForX1[1],32), U(first.KnowledgeProofForX2[0],384), U(first.KnowledgeProofForX2[1],32)));
    var bytes = Read(1600);
    var remote = new JPakeRound1Payload(peer, B(bytes,0,384), B(bytes,384,384), [B(bytes,768,384),B(bytes,1152,32)], [B(bytes,1184,384),B(bytes,1568,32)]);
    participant.ValidateRound1PayloadReceived(remote);
    var second = participant.CreateRound2PayloadToSend();
    Send(Join(U(second.A,384),U(second.KnowledgeProofForX2s[0],384),U(second.KnowledgeProofForX2s[1],32)));
    bytes = Read(800);
    participant.ValidateRound2PayloadReceived(new JPakeRound2Payload(peer,B(bytes,0,384),[B(bytes,384,384),B(bytes,768,32)]));
    var key = participant.CalculateKeyingMaterial();
    var third = participant.CreateRound3PayloadToSend(key);
    Send(S(third.MacTag));
    bytes = Read(32);
    // Do not expose the C# library's BigInteger.Equals as the first MAC comparison.
    var expected = JPakeUtilities.CalculateMacTag(peer,own,remote.Gx1,remote.Gx2,first.Gx1,first.Gx2,key,new Sha256Digest());
    if (!CryptographicOperations.FixedTimeEquals(S(expected),bytes)) throw new InvalidDataException();
    participant.ValidateRound3PayloadReceived(new JPakeRound3Payload(peer,new BigInteger(bytes)),key);
    Console.WriteLine("OK:"+Convert.ToHexStringLower(SHA256.HashData(U(key,384))));
    return 0;
}
catch { Console.WriteLine("REJECT"); return 2; }

static BigInteger B(byte[] b,int offset,int size) => new(1,b.AsSpan(offset,size));
static byte[] U(BigInteger value,int length)
{
    var bytes=value.ToByteArrayUnsigned(); if(bytes.Length>length) throw new InvalidDataException();
    var result=new byte[length]; bytes.CopyTo(result,length-bytes.Length); return result;
}
static byte[] S(BigInteger value)
{
    var bytes=value.ToByteArray(); if(bytes.Length>32) throw new InvalidDataException();
    var result=Enumerable.Repeat(value.SignValue<0 ? (byte)255 : (byte)0,32).ToArray(); bytes.CopyTo(result,32-bytes.Length);return result;
}
static byte[] Join(params byte[][] chunks) => chunks.SelectMany(c=>c).ToArray();
static void Send(byte[] bytes) => Console.WriteLine(Convert.ToBase64String(bytes));
static byte[] Read(int length)
{
    var line=Console.ReadLine() ?? throw new EndOfStreamException(); if(line.Length>6000) throw new InvalidDataException();
    var bytes=Convert.FromBase64String(line); if(bytes.Length!=length) throw new InvalidDataException(); return bytes;
}
