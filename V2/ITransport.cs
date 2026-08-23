// Medium-agnostic transport for the v2 protocol. Implementations exist
// for UDP today, with USB-CDC and WiFi to follow. Each transport is
// responsible for delivering one COBS-framed envelope at a time to its
// listener; framing concerns do not bleed up into the protocol layer.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.V2
{
    public enum TransportKind { Udp, UsbSerial, Uart, Wifi }

    /// One COBS-decoded envelope-bytes ready to hand to Protocol.TryDecode.
    public sealed class TransportFrame
    {
        public byte[]   Bytes      { get; }
        public DateTime HostUtcRx  { get; }
        public long     LocalUsRx  { get; }      // Stopwatch ticks converted to µs

        public TransportFrame(byte[] bytes, DateTime hostUtcRx, long localUsRx)
        {
            Bytes = bytes; HostUtcRx = hostUtcRx; LocalUsRx = localUsRx;
        }
    }

    public interface ITransport : IDisposable
    {
        TransportKind Kind { get; }
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken ct);
        Task DisconnectAsync();

        /// Send a COBS-encoded, 0x00-terminated envelope.
        Task SendAsync(ReadOnlyMemory<byte> wire, CancellationToken ct);

        /// Fires once per inbound envelope. The handler runs on the
        /// transport reader thread; do not block.
        event Action<TransportFrame>? FrameReceived;
    }
}
