using System.Net;
using System.Net.Sockets;
using industREAL.CAN.CANGW.Utils;

namespace industREAL.CAN.CANGW.Tests;

/// <summary>
/// In-process stand-in for the CANGW-01 firmware, implementing the transport the
/// dev_UNICAST branch uses: bind BROADCAST_PORT+1, remember the source address
/// and port of whatever last spoke, and send everything back to that peer.
/// </summary>
/// <remarks>
/// Deliberately self-contained - no external process - so these tests run
/// anywhere. It echoes received frames back as an uplink batch and emits a
/// periodic device-health frame with an incrementing TEC_A so tests can prove the
/// payload is actually decoded rather than defaulted.
/// </remarks>
internal sealed class FakeGateway : IDisposable
{
    private const byte SofUplink = (byte)'#';
    private const byte FuncSingle = (byte)'s';
    private const byte FuncMulti = (byte)'m';
    private const int HeaderLen = 14;
    private const uint FlagControl = 0x00CAFFEE;

    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _rxLoop;
    private readonly Task _beatLoop;

    private IPEndPoint? _peer;
    private byte _counter;

    /// <summary>Port the driver should be told to talk to (BROADCAST_PORT + 1).</summary>
    public int RxPort { get; }

    /// <summary>Value to pass as CanGateway's serverPort, i.e. RxPort - 1.</summary>
    public int BasePort => RxPort - 1;

    public int DatagramsReceived;
    public int FramesReceived;

    public FakeGateway(TimeSpan? heartbeatPeriod = null)
    {
        // Port 0 so parallel test runs cannot collide.
        _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        RxPort = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

        var period = heartbeatPeriod ?? TimeSpan.FromMilliseconds(200);
        _rxLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _beatLoop = Task.Run(() => HeartbeatLoopAsync(period, _cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _socket.ReceiveAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            byte[] payload;
            try { payload = COBS.Decode(result.Buffer); }
            catch (ArgumentException) { continue; }
            if (payload.Length < 2) continue;

            int offset = payload[0] switch { FuncSingle => 1, FuncMulti => 2, _ => -1 };

            // Only latch and count datagrams that are actually a host->gateway
            // command. Both this fake and the driver's client bind ephemeral ports,
            // so a heartbeat still in flight from an earlier test's fake can land
            // here when the OS recycles a port number. Counting that made the
            // "silent until the host speaks" assertions flaky.
            if (offset < 0) continue;

            Volatile.Write(ref _peer, result.RemoteEndPoint);
            Interlocked.Increment(ref DatagramsReceived);

            var echo = new List<byte> { SofUplink };
            while (offset + HeaderLen <= payload.Length)
            {
                int len = payload[offset + 12];
                if (offset + HeaderLen + len > payload.Length) break;
                echo.AddRange(payload.AsSpan(offset, HeaderLen + len).ToArray());
                offset += HeaderLen + len;
                Interlocked.Increment(ref FramesReceived);
            }

            if (echo.Count > 1) Send(echo.ToArray());
        }
    }

    private async Task HeartbeatLoopAsync(TimeSpan period, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(period, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            if (Volatile.Read(ref _peer) is null) continue;

            _counter = (byte)(_counter == 255 ? 1 : _counter + 1);

            // canId 0, flags = CONTROL, msgUid = '1' (DEVICE_HEALTH_PERIODIC),
            // 8 payload bytes: TEC_A REC_A TEC_B REC_B passiveA passiveB rsvd rsvd
            var record = new List<byte>();
            record.AddRange(BitConverter.GetBytes((uint)0));
            record.AddRange(BitConverter.GetBytes(FlagControl));
            record.AddRange(BitConverter.GetBytes((uint)'1'));
            // 12-byte health frame, matching firmware with bus supervision:
            // TEC_A REC_A TEC_B REC_B passiveA passiveB
            // busoffRecoveredA busoffRecoveredB txAbortsA txAbortsB busoffNow rsvd
            record.Add(12);
            record.Add(0);
            record.AddRange(new byte[] { _counter, 2, 3, 4, 0, 1,
                                         7, 8, 9, 10, 0x02, 0 });

            var datagram = new List<byte> { SofUplink };
            datagram.AddRange(record);
            Send(datagram.ToArray());
        }
    }

    private void Send(byte[] uplinkPayload)
    {
        var peer = Volatile.Read(ref _peer);
        if (peer is null) return;
        var frame = COBS.Encode(uplinkPayload);
        try { _socket.Send(frame, frame.Length, peer); }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Close();
        try { Task.WaitAll(new[] { _rxLoop, _beatLoop }, TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _cts.Dispose();
        _socket.Dispose();
    }
}
