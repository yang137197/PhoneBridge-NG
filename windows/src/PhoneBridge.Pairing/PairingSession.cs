using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PhoneBridge.Pairing;

public enum PairingState { Active, Confirmed, Consumed, Failed, Closed }

/// <summary>Serialized, in-memory protocol. The host owns transport deadlines and user authorization.</summary>
public sealed class PairingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly bool _windows;
    private readonly byte[] _window;
    private readonly char[] _code;
    private byte[] _client, _attempt, _ca;
    private ushort _port;
    private int _step;
    private readonly MemoryStream _transcript = new();
    private PakeExchange? _exchange;
    private PairingState _state;
    public PairingState State { get { lock (_gate) return _state; } }

    private PairingSession(bool windows, byte[] window, char[] code, ushort port, byte[] ca)
    {
        if (window.Length != 16 || code.Length != 8 || code.Any(c => c is < '0' or > '9') ||
            (!windows && (port == 0 || ca.Length is < 1 or > 4096))) throw new PairingProtocolException();
        _windows = windows; _window = (byte[])window.Clone(); _code = (char[])code.Clone();
        _client = windows ? RandomNumberGenerator.GetBytes(16) : []; _attempt = windows ? [] : RandomNumberGenerator.GetBytes(16);
        _ca = (byte[])ca.Clone(); _port = port;
    }
    public static PairingSession CreateWindows(byte[] window, char[] code) => new(true,window,code,0,[]);
    public static PairingSession CreateAndroid(byte[] window, char[] code, ushort httpsPort, byte[] caDer) => new(false,window,code,httpsPort,caDer);

    public byte[] CreateNextFrame(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            try
            {
                Guard(cancellationToken);
                if (((_step % 2) == 0) != _windows) throw new PairingProtocolException();
                byte[] payload;
                switch (_step)
                {
                    case 0: payload = [1,.._window,.._client]; break;
                    case 1:
                        payload = new byte[53+_ca.Length]; payload[0] = 1;
                        _window.CopyTo(payload,1); _client.CopyTo(payload,17); _attempt.CopyTo(payload,33);
                        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(49),_port);
                        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(51),(ushort)_ca.Length); _ca.CopyTo(payload,53); break;
                    case 2: case 3: payload = _exchange!.Round1(); break;
                    case 4: case 5: payload = _exchange!.Round2(); break;
                    case 6: case 7: payload = _exchange!.Round3(); break;
                    default: throw new PairingProtocolException();
                }
                cancellationToken.ThrowIfCancellationRequested();
                var frame = PairingFrames.Encode(PairingFrames.Order[_step],payload); Advance(frame); return frame;
            }
            catch { Fail(); throw new PairingProtocolException(); }
        }
    }

    public void AcceptFrame(ReadOnlySpan<byte> frame, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            try
            {
                Guard(cancellationToken);
                if (((_step % 2) == 0) == _windows) throw new PairingProtocolException();
                if (frame.Length > PairingFrames.HeaderSize+PairingFrames.MaxPayload) throw new PairingProtocolException();
                var snapshot = frame.ToArray();
                PairingFrames.Validate(snapshot,PairingFrames.Order[_step]);
                var body = snapshot[PairingFrames.HeaderSize..];
                switch (_step)
                {
                    case 0:
                        if (body[0] != 1 || !body.AsSpan(1,16).SequenceEqual(_window)) throw new PairingProtocolException();
                        _client = body[17..33]; break;
                    case 1:
                        if (body[0] != 1 || !body.AsSpan(1,16).SequenceEqual(_window) || !body.AsSpan(17,16).SequenceEqual(_client))
                            throw new PairingProtocolException();
                        _port = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(49));
                        int size = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(51));
                        if (_port == 0 || size is < 1 or > 4096 || body.Length != 53+size) throw new PairingProtocolException();
                        _attempt = body[33..49]; _ca = body[53..]; break;
                    case 2: case 3: _exchange!.AcceptRound1(body); break;
                    case 4: case 5: _exchange!.AcceptRound2(body); break;
                    case 6: case 7: _exchange!.AcceptRound3(body); break;
                    default: throw new PairingProtocolException();
                }
                cancellationToken.ThrowIfCancellationRequested(); Advance(snapshot);
            }
            catch { Fail(); throw new PairingProtocolException(); }
        }
    }

    private void Advance(ReadOnlySpan<byte> frame)
    {
        if (_transcript.Length + frame.Length > PairingFrames.MaxSessionBytes) throw new PairingProtocolException();
        _transcript.Write(frame); ++_step;
        if (_step == 2)
        {
            var context = Convert.ToHexStringLower(SHA256.HashData(_transcript.ToArray()));
            var own = "pbng-pair-v1:"+(_windows ? "W:" : "A:")+context;
            var peer = "pbng-pair-v1:"+(_windows ? "A:" : "W:")+context;
            _exchange = new PakeExchange(own,peer,_code); Array.Clear(_code);
        }
        if (_step == 8) _state = PairingState.Confirmed;
    }

    /// <summary>Call only after the final frame was sent/received and transport completion was checked.</summary>
    public PakeConfirmation TakeConfirmation(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            byte[]? key = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_state != PairingState.Confirmed) throw new PairingProtocolException();
                key = _exchange!.ExportKey();
                byte[] grant = HKDF.DeriveKey(HashAlgorithmName.SHA256,key,32,SHA256.HashData(_transcript.ToArray()),"PhoneBridge NG|pairing-grant|v1"u8.ToArray());
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = new PakeConfirmation(_ca,_client,_attempt,_port,grant);
                    _state = PairingState.Consumed; Release(); return result;
                }
                finally { CryptographicOperations.ZeroMemory(grant); }
            }
            catch { Fail(); throw new PairingProtocolException(); }
            finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
        }
    }
    private void Guard(CancellationToken token) { token.ThrowIfCancellationRequested(); if (_state != PairingState.Active) throw new PairingProtocolException(); }
    private void Fail() { _state = PairingState.Failed; Release(); }
    private void Release() { Array.Clear(_code); _exchange?.Dispose(); _exchange = null; _transcript.Dispose(); }
    public void Dispose() { lock (_gate) { Release(); _state = PairingState.Closed; } }
}

/// <summary>CA bytes are PAKE-bound but NOT yet parsed/validated as an X.509 trust anchor. No file authorization.</summary>
public sealed class PakeConfirmation : IDisposable
{
    private readonly byte[] _ca, _client, _attempt, _grant;
    private readonly object _gate = new();
    private bool _disposed;
    public ushort HttpsPort { get; }
    internal PakeConfirmation(byte[] ca,byte[] client,byte[] attempt,ushort port,byte[] grant)
    { _ca=(byte[])ca.Clone(); _client=(byte[])client.Clone(); _attempt=(byte[])attempt.Clone(); _grant=(byte[])grant.Clone(); HttpsPort=port; }
    public byte[] CandidateCaDer => (byte[])_ca.Clone();
    public byte[] ClientId => (byte[])_client.Clone();
    public byte[] AttemptId => (byte[])_attempt.Clone();
    public byte[] CopyGrant() { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed,this); return (byte[])_grant.Clone(); } }
    public override string ToString() => "PakeConfirmation(redacted)";
    public void Dispose() { lock (_gate) { CryptographicOperations.ZeroMemory(_grant); _disposed=true; } }
}
