// CAN frame DTO carrying the timestamp delivered by the firmware. The
// timestamp is the device monotonic microsecond counter at the moment
// the FDCAN RX ISR fired; ClockSync converts it to host time.

using System;

namespace industREAL.CAN.CANGW.V2
{
    public enum CanChannel : byte { A = 0, B = 1 }

    public enum CanMode : byte
    {
        Classic    = 0,
        FdNoBrs    = 1,
        FdBrs      = 2,
    }

    [Flags]
    public enum CanFlagsV2 : uint
    {
        None        = 0,
        Extended    = 0x40000000,
        RemoteFrame = 0x20000000,
        FdFormat    = 0x00200000,
        BrsOn       = 0x00100000,
    }

    public sealed class CanGwFrame
    {
        public uint        CanId          { get; init; }
        public CanFlagsV2  Flags          { get; init; }
        public uint        MessageUid     { get; init; }
        public CanChannel  Channel        { get; init; }
        public byte[]      Data           { get; init; } = Array.Empty<byte>();
        public long        DeviceUs       { get; init; }
        public DateTime    HostUtc        { get; init; }

        public bool IsExtended    => (Flags & CanFlagsV2.Extended)    != 0;
        public bool IsRemote      => (Flags & CanFlagsV2.RemoteFrame) != 0;
        public bool IsFd          => (Flags & CanFlagsV2.FdFormat)    != 0;
        public bool IsBrs         => (Flags & CanFlagsV2.BrsOn)       != 0;

        public override string ToString() =>
            $"[{Channel}] 0x{CanId:X} dlc={Data.Length} ts={DeviceUs}µs";
    }

    public static class CanFrameCodec
    {
        // Wire body for PROTO_TYPE_DATA_FRAME:
        //   [canId:4][flags:4][uid:4][ts_us:8][dlc:1][data:dlc]   = 21 + dlc bytes
        public const int HeaderSize = 21;

        public static int Encode(Span<byte> dst, uint canId, CanFlagsV2 flags,
                                 uint uid, long timestampUs,
                                 ReadOnlySpan<byte> data)
        {
            int total = HeaderSize + data.Length;
            if (dst.Length < total) throw new ArgumentException("buffer too small");

            BitConverter.TryWriteBytes(dst.Slice(0,  4), canId);
            BitConverter.TryWriteBytes(dst.Slice(4,  4), (uint)flags);
            BitConverter.TryWriteBytes(dst.Slice(8,  4), uid);
            BitConverter.TryWriteBytes(dst.Slice(12, 8), timestampUs);
            dst[20] = (byte)data.Length;
            data.CopyTo(dst.Slice(21));
            return total;
        }

        public static bool TryDecode(ReadOnlySpan<byte> src, CanChannel channel,
                                     out uint canId, out CanFlagsV2 flags,
                                     out uint uid, out long ts,
                                     out byte[] data)
        {
            canId = 0; flags = 0; uid = 0; ts = 0; data = Array.Empty<byte>();
            if (src.Length < HeaderSize) return false;

            canId = BitConverter.ToUInt32(src.Slice(0,  4));
            flags = (CanFlagsV2)BitConverter.ToUInt32(src.Slice(4, 4));
            uid   = BitConverter.ToUInt32(src.Slice(8,  4));
            ts    = BitConverter.ToInt64 (src.Slice(12, 8));
            int dlc = src[20];
            if (src.Length < HeaderSize + dlc) return false;
            data = src.Slice(HeaderSize, dlc).ToArray();
            return true;
        }
    }
}
