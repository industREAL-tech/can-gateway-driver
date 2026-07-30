using industREAL.CAN;
using industREAL.CAN.CORE;
using Xunit;

namespace industREAL.CAN.CANGW.Tests;

/// <summary>
/// These tests bind real loopback sockets. The OS reuses ephemeral ports, so a
/// fake gateway from a concurrently running test could latch onto a client here
/// and deliver a stray datagram. Serialising the class keeps that out.
/// </summary>
[CollectionDefinition("loopback", DisableParallelization = true)]
public sealed class LoopbackCollection { }

[Collection("loopback")]
public class CanGatewayTests
{
    private const string Host = "127.0.0.1";

    private static CanFrame Frame(uint id, int len = 8)
    {
        var f = new CanFrame((CanDLC)len) { _canID = id, _flags = CanFlags.ExtendedId };
        for (int i = 0; i < len; i++) f._data[i] = (byte)(i + 1);
        return f;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    [Fact]
    public void DisposeDoesNotThrowOnAnUnconnectedGateway()
    {
        // Regression: Dispose() threw NotImplementedException, so any `using` or
        // container-managed lifetime crashed at scope exit.
        using var gw = new CanGateway(Host, 12144);
    }

    [Fact]
    public void DisposeDoesNotThrowAfterConnect()
    {
        using var fake = new FakeGateway();
        using (var gw = new CanGateway(Host, fake.BasePort))
        {
            gw.Connect();
            Thread.Sleep(100);
        }
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var gw = new CanGateway(Host, 12144);
        gw.Dispose();
        gw.Dispose();
    }

    [Fact]
    public void ConnectAfterDisconnectWorks()
    {
        // Regression: Stop() closed the only socket the instance would ever own,
        // because it was created in the constructor - so a disconnect was permanent.
        using var fake = new FakeGateway();
        using var gw = new CanGateway(Host, fake.BasePort);

        gw.Connect();
        gw.sendSingleCanFrame(Frame(0x100));
        Assert.True(WaitFor(() => gw.isConnected), "did not connect the first time");

        gw.Disconnect();
        Assert.False(gw.isConnected);

        gw.Connect();
        gw.sendSingleCanFrame(Frame(0x101));
        Assert.True(WaitFor(() => gw.isConnected), "did not reconnect");
    }

    [Fact]
    public void DisconnectReportsTheStateChange()
    {
        using var fake = new FakeGateway();
        using var gw = new CanGateway(Host, fake.BasePort);

        var transitions = new List<bool>();
        gw.OnConnectionChanged += c => { lock (transitions) transitions.Add(c); };

        gw.Connect();
        gw.sendSingleCanFrame(Frame(0x100));
        Assert.True(WaitFor(() => gw.isConnected));

        gw.Disconnect();

        Assert.False(gw.isConnected);
        lock (transitions) Assert.Equal(new[] { true, false }, transitions);
    }

    [Fact]
    public void IsConnectedGoesTrueOnTheFirstUplinkDatagram()
    {
        using var fake = new FakeGateway();
        using var gw = new CanGateway(Host, fake.BasePort);

        // Written on the UDP receive thread, read here - so it needs a barrier.
        // A plain bool read once after WaitFor() can still see a stale value even
        // though the polling loop has already observed isConnected.
        int sawConnect = 0;
        gw.OnConnectionChanged += c => { if (c) Interlocked.Exchange(ref sawConnect, 1); };

        gw.Connect();

        // The gateway has no peer until the host speaks, so it must not have sent
        // anything yet. Asserted against this fake's own counter rather than
        // gw.isConnected, which any stray loopback datagram could flip.
        Thread.Sleep(150);
        Assert.Equal(0, fake.DatagramsReceived);

        gw.sendSingleCanFrame(Frame(0x123));

        Assert.True(WaitFor(() => gw.isConnected), "never became connected");
        Assert.True(WaitFor(() => Volatile.Read(ref sawConnect) == 1),
                    "OnConnectionChanged(true) was not raised");
    }

    [Fact]
    public void ReceivedFramesAreDeliveredToSubscribers()
    {
        using var fake = new FakeGateway();
        using var gw = new CanGateway(Host, fake.BasePort);

        var received = new List<CanFrame>();
        gw.FrameReceived += f => { lock (received) received.Add(f); };
        gw.Connect();

        gw.sendSingleCanFrame(Frame(0x18FF50E5));

        Assert.True(WaitFor(() => { lock (received) return received.Count >= 1; }),
                    "no frame was delivered");
        lock (received)
        {
            Assert.Equal(0x18FF50E5u, received[0]._canID);
            Assert.Equal(CanFlags.ExtendedId, received[0]._flags);
            Assert.Equal(8, (byte)received[0]._dataLength);
        }
    }

    [Fact]
    public void HeartbeatPayloadIsDecodedIntoTheErrorCounters()
    {
        // Regression: the payload was discarded and OnGatewayStatusUpdated raised
        // with (0,0,0,0), which made the bus-state features unimplementable.
        using var fake = new FakeGateway(TimeSpan.FromMilliseconds(80));
        using var gw = new CanGateway(Host, fake.BasePort);

        var sync = new object();
        byte tec = 0, recA = 0, tecB = 0, recB = 0;
        int updates = 0;
        gw.OnGatewayStatusUpdated += (a, b, c, d) =>
        {
            // Written on the UDP receive thread; the lock is the barrier.
            lock (sync) { tec = a; recA = b; tecB = c; recB = d; }
            Interlocked.Increment(ref updates);
        };

        gw.Connect();
        gw.sendSingleCanFrame(Frame(0x1));   // latch the peer so heartbeats start

        Assert.True(WaitFor(() => Volatile.Read(ref updates) >= 2), "no heartbeats decoded");

        lock (sync)
        {
            Assert.True(tec >= 1, $"TEC_A should carry the emulator's counter, got {tec}");
            Assert.Equal(2, recA);
            Assert.Equal(3, tecB);
            Assert.Equal(4, recB);
        }
        Assert.Equal(tec, gw.TEC_A);
        Assert.Equal(2, gw.REC_A);
        Assert.False(gw.ErrorPassiveA);
    }

    [Fact]
    public void FlushSendsEveryQueuedFrameEvenBeyondTheSingleByteCount()
    {
        // Regression: the whole queue went into one datagram with the frame count
        // cast to a byte, so 256 frames wrapped the count to 0 and the gateway
        // transmitted nothing while the caller believed the batch had gone out.
        // The gateway also reads only the first pbuf, so an over-MTU batch was
        // truncated - hence the byte budget as well as the count limit.
        const int n = 300;

        using var fake = new FakeGateway(TimeSpan.FromMilliseconds(500));
        using var gw = new CanGateway(Host, fake.BasePort);

        int echoed = 0;
        gw.FrameReceived += _ => Interlocked.Increment(ref echoed);
        gw.Connect();

        gw.sendSingleCanFrame(Frame(0x1, 0));            // latch the peer
        Assert.True(WaitFor(() => gw.isConnected));
        Interlocked.Exchange(ref echoed, 0);

        for (int i = 0; i < n; i++) gw.addToMultipleFrame(Frame((uint)(0x200 + i)));
        gw.FlushMultipleFrames();

        Assert.True(WaitFor(() => Volatile.Read(ref echoed) >= n, 8000),
                    $"only {Volatile.Read(ref echoed)} of {n} frames came back");
        Assert.True(fake.DatagramsReceived > 2,
                    "a 300-frame batch must be split across several datagrams");
    }

    [Fact]
    public void FlushOnAnEmptyQueueSendsNothing()
    {
        using var fake = new FakeGateway(TimeSpan.FromMilliseconds(500));
        using var gw = new CanGateway(Host, fake.BasePort);
        gw.Connect();

        gw.FlushMultipleFrames();
        Thread.Sleep(150);

        Assert.Equal(0, fake.DatagramsReceived);
    }

    [Fact]
    public void SerialPortConstructorIsExplicitlyUnimplemented()
    {
        Assert.Throws<NotImplementedException>(() => new CanGateway("COM3", 500000u));
    }
}
