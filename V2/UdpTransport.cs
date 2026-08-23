// UDP transport. Binds an ephemeral local port; sends to the device
// port. Inbound datagrams are unwrapped from COBS and forwarded as
// TransportFrame events.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using industREAL.CAN.CANGW.Utils;

namespace industREAL.CAN.CANGW.V2
{
    public sealed class UdpTransport : ITransport
    {
        private readonly IPEndPoint  _remote;
        private          UdpClient?  _client;
        private          CancellationTokenSource? _cts;
        private          Task?       _rxTask;
        private static readonly long s_tickFreq = Stopwatch.Frequency;

        public TransportKind Kind => TransportKind.Udp;
        public bool IsConnected   => _client is not null;

        public event Action<TransportFrame>? FrameReceived;

        public UdpTransport(string ip, int port)
        {
            _remote = new IPEndPoint(IPAddress.Parse(ip), port);
        }

        public Task ConnectAsync(CancellationToken ct)
        {
            if (_client is not null) return Task.CompletedTask;

            _client = new UdpClient(0);
            _client.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReceiveTimeout, 0);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _rxTask = Task.Run(() => RxLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            try { _client?.Close(); } catch { }
            if (_rxTask is not null) { try { await _rxTask; } catch { } }
            _client = null;
            _cts = null;
            _rxTask = null;
        }

        public async Task SendAsync(ReadOnlyMemory<byte> wire, CancellationToken ct)
        {
            var c = _client ?? throw new InvalidOperationException("transport not connected");
            await c.SendAsync(wire.ToArray(), wire.Length, _remote).ConfigureAwait(false);
        }

        private async Task RxLoopAsync(CancellationToken ct)
        {
            var c = _client!;
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult res;
                try { res = await c.ReceiveAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException)    { return; }

                long localUs = Stopwatch.GetTimestamp() * 1_000_000L / s_tickFreq;
                DateTime utc = DateTime.UtcNow;

                byte[] decoded;
                try { decoded = COBS.Decode(res.Buffer); }
                catch { continue; }

                FrameReceived?.Invoke(new TransportFrame(decoded, utc, localUs));
            }
        }

        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }
}
