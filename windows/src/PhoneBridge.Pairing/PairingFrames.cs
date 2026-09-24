using System.Buffers.Binary;

namespace PhoneBridge.Pairing;

public sealed class PairingProtocolException : Exception
{
    public PairingProtocolException() : base("pairing_protocol_error") { }
}

public static class PairingFrames
{
    public const int HeaderSize = 9;
    public const int MaxPayload = 8192;
    public const int MaxSessionBytes = 32768;
    internal static readonly byte[] Order = [1, 2, 0x11, 0x12, 0x21, 0x22, 0x31, 0x32];

    internal static int Length(ReadOnlySpan<byte> header, byte expectedType)
    {
        if (header.Length != HeaderSize || !header[..4].SequenceEqual("PBP1"u8) || header[4] != expectedType)
            throw new PairingProtocolException();
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header[5..]);
        bool valid = expectedType switch
        {
            1 => size == 33,
            2 => size is >= 54 and <= 4149,
            0x11 or 0x12 => size == 1600,
            0x21 or 0x22 => size == 800,
            0x31 or 0x32 => size == 32,
            _ => false
        };
        if (!valid || size > MaxPayload) throw new PairingProtocolException();
        return (int)size;
    }

    public static void Validate(ReadOnlySpan<byte> frame, byte expectedType)
    {
        if (frame.Length < HeaderSize || Length(frame[..HeaderSize], expectedType) != frame.Length - HeaderSize)
            throw new PairingProtocolException();
    }

    internal static byte[] Encode(byte type, byte[] payload)
    {
        var frame = new byte[HeaderSize + payload.Length];
        "PBP1"u8.CopyTo(frame); frame[4] = type;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)payload.Length);
        payload.CopyTo(frame, HeaderSize); Validate(frame, type); return frame;
    }
}

/// <summary>One expected frame only. A transport must call EndOfInput on EOF and reject surplus bytes.</summary>
public sealed class FrameAccumulator
{
    private readonly byte _expected;
    private readonly byte[] _header = new byte[PairingFrames.HeaderSize];
    private byte[]? _frame;
    private int _count;
    private bool _failed;
    public bool IsComplete => !_failed && _frame is not null && _count == _frame.Length;

    public FrameAccumulator(byte expectedType)
    {
        if (!PairingFrames.Order.Contains(expectedType)) throw new PairingProtocolException();
        _expected = expectedType;
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (_failed || IsComplete) throw new PairingProtocolException();
            if (_count < _header.Length)
            {
                int n = Math.Min(bytes.Length, _header.Length - _count);
                bytes[..n].CopyTo(_header.AsSpan(_count)); _count += n; bytes = bytes[n..];
                if (_count < _header.Length) return;
                _frame = new byte[_header.Length + PairingFrames.Length(_header, _expected)];
                _header.CopyTo(_frame, 0);
            }
            if (bytes.Length > _frame!.Length - _count) throw new PairingProtocolException();
            bytes.CopyTo(_frame.AsSpan(_count)); _count += bytes.Length;
        }
        catch { _failed = true; _frame = null; throw new PairingProtocolException(); }
    }

    public void EndOfInput()
    {
        if (!IsComplete) { _failed = true; _frame = null; throw new PairingProtocolException(); }
    }

    public byte[] GetFrame() { EndOfInput(); return (byte[])_frame!.Clone(); }
}
