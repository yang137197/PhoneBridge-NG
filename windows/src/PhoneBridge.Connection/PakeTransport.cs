using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PhoneBridge.Discovery;
using PhoneBridge.Pairing;

namespace PhoneBridge.Connection;

internal static class PakeTransport
{
    internal static async Task<PakeConfirmation> ConfirmAsync(DeviceEndpoint endpoint, PairingAdvertisement pairing,
        char[] code, CancellationToken cancellationToken)
    {
        EndpointRules.Check(endpoint);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var tcp = new TcpClient();
        using var session = PairingSession.CreateWindows(Convert.FromHexString(pairing.Window), code);
        Array.Clear(code);
        await tcp.ConnectAsync(IPAddress.Parse(endpoint.Address), pairing.Port, deadline.Token).ConfigureAwait(false);
        using var stream = tcp.GetStream();
        foreach (byte expected in new byte[] { 2, 0x12, 0x22, 0x32 })
        {
            var frame = session.CreateNextFrame(deadline.Token);
            using var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            frameDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            await stream.WriteAsync(frame, frameDeadline.Token).ConfigureAwait(false);
            if (expected == 0x32) tcp.Client.Shutdown(SocketShutdown.Send);
            byte[] header = new byte[9];
            await stream.ReadExactlyAsync(header, frameDeadline.Token).ConfigureAwait(false);
            var accumulator = new FrameAccumulator(expected);
            accumulator.Feed(header); // Validates length/type before allocation.
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5)));
            if (length > 8192) throw new ConnectionException("invalid-frame");
            byte[] payload = new byte[length];
            await stream.ReadExactlyAsync(payload, frameDeadline.Token).ConfigureAwait(false);
            accumulator.Feed(payload);
            session.AcceptFrame(accumulator.GetFrame(), deadline.Token);
        }
        if (await stream.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false) != 0)
            throw new ConnectionException("trailing-frame");
        return session.TakeConfirmation(deadline.Token);
    }
}
