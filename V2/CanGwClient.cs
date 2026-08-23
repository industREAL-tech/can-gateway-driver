// High-level client. Composes:
//   - ITransport (UDP today, USB / WiFi later)
//   - Protocol envelope
//   - Channel ownership/lease (Subscribe + Heartbeat)
//   - NTP-style time sync background task
//   - Pending control-request map for await/response
//   - Outbound batching with deadline flush
//
// API: ConnectAsync, OpenChannelAsync, SetBaudAsync, SetTerminationAsync,
// SetIdentifyAsync, GetFirmwareVersionAsync, ReleaseChannelAsync, Send,
// Enqueue, Flush, FrameReceived, BusStatusUpdated, ConnectionChanged.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace industREAL.CAN.CANGW.V2
{
    public enum ChannelMode { Owned, ListenOnly }

    public sealed class BusStatus
    {
        public byte LoadAPercent  { get; init; }
        public byte LoadBPercent  { get; init; }
        public byte TecA          { get; init; }
        public byte RecA          { get; init; }
        public byte TecB          { get; init; }
        public byte RecB          { get; init; }
        public byte StateA        { get; init; }
        public byte StateB        { get; init; }
        public long DeviceUptimeUs{ get; init; }
        public Version Firmware   { get; init; } = new(0, 0, 0);
    }

    public sealed class CanGwClient : IAsyncDisposable
    {
        private readonly ITransport _transport;
        private readonly ClockSync  _clock = new();
        private readonly ConcurrentDictionary<ushort, TaskCompletionSource<Envelope>> _pending = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private CancellationTokenSource? _bgCts;
        private Task?  _hbTask;
        private Task?  _syncTask;
        private bool   _connected;

        public ClockSync Clock => _clock;
        public bool IsConnected => _connected;

        public event Action<CanGwFrame>?  FrameReceived;
        public event Action<BusStatus>?   BusStatusUpdated;
        public event Action<bool>?        ConnectionChanged;

        public CanGwClient(ITransport transport)
        {
            _transport = transport;
            _transport.FrameReceived += OnTransportFrame;
        }

        // ---- Connection lifecycle ---------------------------------------

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
            _bgCts = new CancellationTokenSource();
            _hbTask   = Task.Run(() => HeartbeatLoop(_bgCts.Token));
            _syncTask = Task.Run(() => SyncLoop(_bgCts.Token));
            SetConnected(true);
        }

        public async Task DisconnectAsync()
        {
            _bgCts?.Cancel();
            try { if (_hbTask   is not null) await _hbTask;   } catch { }
            try { if (_syncTask is not null) await _syncTask; } catch { }
            await _transport.DisconnectAsync().ConfigureAwait(false);
            SetConnected(false);
        }

        // ---- Channel lease ----------------------------------------------

        public async Task<bool> OpenChannelAsync(CanChannel ch, ChannelMode mode,
                                                 CancellationToken ct = default)
        {
            byte modeByte = mode == ChannelMode.Owned
                ? (byte)SubscribeMode.Owned : (byte)SubscribeMode.ListenOnly;
            byte[] payload = { (byte)ch, modeByte };

            var rsp = await SendAndAwaitAsync(FrameType.Subscribe, (byte)ch, payload, ct)
                          .ConfigureAwait(false);
            if (rsp.Type != FrameType.SubAck) return false;
            var p = rsp.Payload;
            return p.Length >= 2 && p.Span[1] == (byte)CtrlStatus.Ok;
        }

        public Task<bool> ReleaseChannelAsync(CanChannel ch, CancellationToken ct = default)
        {
            byte[] payload = { (byte)ch, (byte)SubscribeMode.Release };
            return SendAndAwaitAsync(FrameType.Subscribe, (byte)ch, payload, ct)
                  .ContinueWith(t => t.IsCompletedSuccessfully &&
                                     t.Result.Type == FrameType.SubAck, ct);
        }

        // ---- Control commands -------------------------------------------

        public Task<CtrlStatus> SetTerminationAsync(CanChannel ch, bool on,
                                                    CancellationToken ct = default)
            => SendCtrl(CtrlCmd.Termination, CtrlOp.Set, (byte)ch,
                        new[] { (byte)ch, (byte)(on ? 1 : 0) }, ct);

        public Task<(CtrlStatus, bool)> GetTerminationAsync(CanChannel ch,
                                                            CancellationToken ct = default)
            => SendCtrlForBytes(CtrlCmd.Termination, CtrlOp.Get, (byte)ch, new[] { (byte)ch }, ct,
                                buf => buf.Length >= 1 && buf.Span[0] != 0);

        public Task<CtrlStatus> SetBaudAsync(CanChannel ch, uint nominalBps, uint dataBps,
                                              CanMode mode, CancellationToken ct = default)
        {
            byte[] body = new byte[9];
            BitConverter.TryWriteBytes(body.AsSpan(0, 4), nominalBps);
            BitConverter.TryWriteBytes(body.AsSpan(4, 4), dataBps);
            body[8] = (byte)mode;
            return SendCtrl(CtrlCmd.Baud, CtrlOp.Set, (byte)ch, body, ct);
        }

        public Task<CtrlStatus> SetIdentifyAsync(CanChannel ch, bool on,
                                                  CancellationToken ct = default)
            => SendCtrl(CtrlCmd.Identify, CtrlOp.Set, (byte)ch,
                        new[] { (byte)ch, (byte)(on ? 1 : 0) }, ct);

        public async Task<Version?> GetFirmwareVersionAsync(CancellationToken ct = default)
        {
            var rsp = await SendCtrlRawAsync(CtrlCmd.FwVersion, CtrlOp.Get,
                                             Constants.ChannelDevice, ReadOnlyMemory<byte>.Empty, ct)
                          .ConfigureAwait(false);
            byte[] b = rsp.Payload.ToArray();
            if (b.Length < 5 || b[1] != (byte)CtrlStatus.Ok) return null;
            return new Version(b[2], b[3], b[4]);
        }

        public Task<CtrlStatus> RebootAsync(CancellationToken ct = default)
            => SendCtrl(CtrlCmd.Reboot, CtrlOp.Set, Constants.ChannelDevice,
                        new byte[] { 0xCD, 0xAB }, ct);

        // ---- Data send --------------------------------------------------

        public async Task SendFrameAsync(CanChannel ch, uint canId, CanFlagsV2 flags,
                                          ReadOnlyMemory<byte> data,
                                          uint uid = 0, CancellationToken ct = default)
        {
            byte[] body = new byte[CanFrameCodec.HeaderSize + data.Length];
            CanFrameCodec.Encode(body, canId, flags, uid, 0L, data.Span);
            await SendEnvelopeAsync(FrameType.DataFrame, (byte)ch, body, ct)
                .ConfigureAwait(false);
        }

        // ---- Internal request/response helpers --------------------------

        private async Task<CtrlStatus> SendCtrl(CtrlCmd cmd, CtrlOp op, byte channel,
                                                ReadOnlyMemory<byte> body,
                                                CancellationToken ct)
        {
            var rsp = await SendCtrlRawAsync(cmd, op, channel, body, ct).ConfigureAwait(false);
            var p = rsp.Payload;
            return p.Length >= 2 ? (CtrlStatus)p.Span[1] : CtrlStatus.Nack;
        }

        private async Task<(CtrlStatus, T)> SendCtrlForBytes<T>(CtrlCmd cmd, CtrlOp op,
                byte channel, ReadOnlyMemory<byte> body,
                CancellationToken ct, Func<ReadOnlyMemory<byte>, T> parser)
        {
            var rsp = await SendCtrlRawAsync(cmd, op, channel, body, ct).ConfigureAwait(false);
            var b = rsp.Payload;
            if (b.Length < 2) return (CtrlStatus.Nack, default!);
            CtrlStatus st = (CtrlStatus)b.Span[1];
            T parsed = b.Length > 2 ? parser(b.Slice(2)) : default!;
            return (st, parsed);
        }

        private Task<Envelope> SendCtrlRawAsync(CtrlCmd cmd, CtrlOp op, byte channel,
                                                 ReadOnlyMemory<byte> body, CancellationToken ct)
        {
            byte[] payload = new byte[2 + body.Length];
            payload[0] = (byte)cmd;
            payload[1] = (byte)op;
            body.Span.CopyTo(payload.AsSpan(2));
            return SendAndAwaitAsync(FrameType.CtrlReq, channel, payload, ct);
        }

        private async Task<Envelope> SendAndAwaitAsync(FrameType type, byte channel,
                                                        ReadOnlyMemory<byte> payload,
                                                        CancellationToken ct,
                                                        int timeoutMs = 1500)
        {
            ushort seq = NextSeq();
            var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[seq] = tcs;

            try
            {
                await SendEnvelopeAsync(type, channel, payload, ct, seq).ConfigureAwait(false);
                using var to = new CancellationTokenSource(timeoutMs);
                using var lk = CancellationTokenSource.CreateLinkedTokenSource(ct, to.Token);
                using (lk.Token.Register(() => tcs.TrySetCanceled()))
                    return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(seq, out _);
            }
        }

        private async Task SendEnvelopeAsync(FrameType type, byte channel,
                                              ReadOnlyMemory<byte> payload,
                                              CancellationToken ct, ushort? seq = null)
        {
            byte[] env = new byte[Constants.HeaderSize + payload.Length + Constants.CrcSize];
            ushort s = seq ?? NextSeq();
            int len = Protocol.Encode(env, type, channel, s, payload.Span);
            byte[] wire = Protocol.FrameForWire(env.AsSpan(0, len));

            await _sendGate.WaitAsync(ct).ConfigureAwait(false);
            try { await _transport.SendAsync(wire, ct).ConfigureAwait(false); }
            finally { _sendGate.Release(); }
        }

        private ushort NextSeq() => unchecked((ushort)Interlocked.Increment(ref _seqCounter));
        private int _seqCounter;

        // ---- Inbound handler --------------------------------------------

        private void OnTransportFrame(TransportFrame frame)
        {
            if (!Protocol.TryDecode(frame.Bytes, out var env)) return;

            switch (env.Type)
            {
                case FrameType.DataFrame: HandleDataFrame(env); break;
                case FrameType.Event:     HandleEvent(env);     break;
                case FrameType.TimeRsp:   HandleTimeRsp(env, frame.LocalUsRx); break;
                case FrameType.SubAck:
                case FrameType.CtrlRsp:
                    if (_pending.TryRemove(env.Seq, out var tcs)) tcs.TrySetResult(env);
                    break;
            }
        }

        private void HandleDataFrame(Envelope env)
        {
            if (!CanFrameCodec.TryDecode(env.Payload.Span, (CanChannel)env.Channel,
                    out uint canId, out var flags, out uint uid, out long ts, out var data))
                return;

            FrameReceived?.Invoke(new CanGwFrame {
                CanId      = canId,
                Flags      = flags,
                MessageUid = uid,
                Channel    = (CanChannel)env.Channel,
                Data       = data,
                DeviceUs   = ts,
                HostUtc    = _clock.DeviceToHost(ts),
            });
        }

        private void HandleEvent(Envelope env)
        {
            // Periodic device health: see HealthEvent.h for layout.
            var p = env.Payload.Span;
            if (p.Length < 19) return;
            var status = new BusStatus {
                LoadAPercent   = p[0],
                LoadBPercent   = p[1],
                TecA           = p[2],
                RecA           = p[3],
                TecB           = p[4],
                RecB           = p[5],
                StateA         = p[6],
                StateB         = p[7],
                DeviceUptimeUs = BitConverter.ToInt64(p.Slice(8, 8)),
                Firmware       = new Version(p[16], p[17], p[18]),
            };
            BusStatusUpdated?.Invoke(status);
        }

        private void HandleTimeRsp(Envelope env, long t4Us)
        {
            var b = env.Payload.Span;
            if (b.Length < 24) return;
            long t1 = BitConverter.ToInt64(b.Slice(0,  8));
            long t2 = BitConverter.ToInt64(b.Slice(8,  8));
            long t3 = BitConverter.ToInt64(b.Slice(16, 8));
            _clock.Submit(t1, t2, t3, t4Us);
        }

        // ---- Background loops -------------------------------------------

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            byte[] empty = Array.Empty<byte>();
            while (!ct.IsCancellationRequested)
            {
                try { await SendEnvelopeAsync(FrameType.Heartbeat, Constants.ChannelAny, empty, ct); }
                catch { }
                try { await Task.Delay(500, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task SyncLoop(CancellationToken ct)
        {
            int interval = 1000;
            int warmup   = 8;
            while (!ct.IsCancellationRequested)
            {
                long t1 = Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;
                byte[] payload = new byte[8];
                BitConverter.TryWriteBytes(payload, t1);
                try { await SendEnvelopeAsync(FrameType.TimeReq, Constants.ChannelAny, payload, ct); }
                catch { }

                if (warmup > 0) { warmup--; interval = 1000; }
                else            { interval = 10_000; }

                try { await Task.Delay(interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        // ---- Misc -------------------------------------------------------

        private void SetConnected(bool v)
        {
            if (_connected != v) { _connected = v; ConnectionChanged?.Invoke(v); }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync().ConfigureAwait(false);
            _transport.Dispose();
        }
    }
}
