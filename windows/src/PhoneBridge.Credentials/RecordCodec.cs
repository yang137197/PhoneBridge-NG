using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PhoneBridge.Credentials;

internal static class RecordCodec
{
    internal const int MaxFileBytes=65536;
    internal static byte[] Encode(StoredRecord record)
    {
        var m=record.Metadata;
        if (record.Token.Length!=32) throw new CredentialStoreException(StoreError.InvalidInput);
        var ca=m.Identity.CertificateDer;var device=RecordRules.Utf8.GetBytes(m.DeviceName);var client=RecordRules.Utf8.GetBytes(m.ClientName);
        var result=new byte[4+1+8+1+1+2+ca.Length+32+69+16+32+2+device.Length+2+client.Length];
        int p=0;
        void Put(byte[] value) { value.CopyTo(result,p);p+=value.Length; }
        void U16(int value) { BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(p),(ushort)value);p+=2; }
        Put("PBC1"u8.ToArray());result[p++]=1;BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(p),m.Revision);p+=8;
        result[p++]=(byte)m.State;result[p++]=(byte)m.Mode;U16(ca.Length);Put(ca);Put(Convert.FromHexString(m.Identity.Sha256));
        Put(Encoding.ASCII.GetBytes(m.DeviceId));Put(Convert.FromHexString(m.ClientId));Put(record.Token);
        U16(device.Length);Put(device);U16(client.Length);Put(client);return result;
    }

    internal static StoredRecord Decode(byte[] plaintext,string expectedDeviceId)
    {
        try
        {
            if (plaintext.Length is < 1 or > MaxFileBytes) throw new InvalidDataException();
            var reader=new Reader(plaintext);
            if (!reader.Take(4).SequenceEqual("PBC1"u8) || reader.Take(1)[0]!=1) throw new InvalidDataException();
            ulong revision=BinaryPrimitives.ReadUInt64BigEndian(reader.Take(8));
            var state=(PairingRecordState)reader.Take(1)[0];var mode=(AccessMode)reader.Take(1)[0];
            int caLength=reader.U16();if (caLength is < 1 or > 4096) throw new InvalidDataException();
            var ca=reader.Take(caLength);var sha=Convert.ToHexStringLower(reader.Take(32));
            var device=Encoding.ASCII.GetString(reader.Take(69));var client=Convert.ToHexStringLower(reader.Take(16));
            var token=reader.Take(32);
            string deviceName=RecordRules.Utf8.GetString(reader.Take(reader.U16()));
            string clientName=RecordRules.Utf8.GetString(reader.Take(reader.U16()));
            if (!reader.AtEnd || device!=expectedDeviceId || device!="pbng-"+sha) throw new InvalidDataException();
            var identity=ValidatedDeviceIdentity.Validate(ca,sha);
            return new StoredRecord(new PairingRecord(identity,client,deviceName,clientName,state,mode,revision),token.ToArray());
        }
        catch { throw new CredentialStoreException(StoreError.NeedsRepair); }
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _remaining=data;
        internal bool AtEnd => _remaining.IsEmpty;
        internal ReadOnlySpan<byte> Take(int count)
        { if (count<0 || count>_remaining.Length) throw new InvalidDataException();var value=_remaining[..count];_remaining=_remaining[count..];return value; }
        internal int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    }
}
