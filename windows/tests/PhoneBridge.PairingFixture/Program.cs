// Synthetic pipe-only interop host, not shipped with the product.
using PhoneBridge.Pairing;
using System.Security.Cryptography;

if (args.Length != 1 || args[0] is not ("00123456" or "00123457" or "00000000")) return 3;
using var session = PairingSession.CreateWindows(Enumerable.Range(0,16).Select(i=>(byte)i).ToArray(),args[0].ToCharArray());
try
{
    for (int step=0;step<4;step++)
    {
        Console.WriteLine(Convert.ToBase64String(session.CreateNextFrame()));
        var reader = new FrameAccumulator(new byte[]{2,0x12,0x22,0x32}[step]);
        var line = new List<char>();
        for (int c; (c=Console.Read()) != '\n'; )
        {
            if (c < 0 || line.Count >= 12000) throw new PairingProtocolException();
            if (c != '\r') line.Add((char)c);
        }
        foreach (byte b in Convert.FromBase64String(new string(line.ToArray()))) reader.Feed([b]);
        reader.EndOfInput(); session.AcceptFrame(reader.GetFrame());
    }
    using var result = session.TakeConfirmation();
    var grant=result.CopyGrant();
    Console.WriteLine("OK:"+Convert.ToHexStringLower(SHA256.HashData(grant))+":"+
        Convert.ToHexStringLower(SHA256.HashData(result.CandidateCaDer))+":"+Convert.ToHexStringLower(result.ClientId)+":"+
        Convert.ToHexStringLower(result.AttemptId)+":"+result.HttpsPort);
    CryptographicOperations.ZeroMemory(grant); return 0;
}
catch { Console.WriteLine("REJECT"); return 2; }
