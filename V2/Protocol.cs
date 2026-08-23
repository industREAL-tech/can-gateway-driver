// Protocol envelope encode/decode + constants.
//
// Wire layout (after COBS decode, before any handler):
//
//   offset  size  field
//   0       1     VER          (Constants.Version)
//   1       1     TYPE         (FrameType)
//   2       1     CHANNEL      (0=A, 1=B, 0xFE=device, 0xFF=any)
//   3       2     SEQ          LE
//   5       2     PAYLOAD_LEN  LE
//   7       N     PAYLOAD
//   7+N     2     CRC16-CCITT  LE, over bytes [0..7+N-1]
//
// All multi-byte fields are little-endian. The whole envelope is COBS-
// encoded and 0x00-terminated by the transport.

using System;
using industREAL.CAN.CANGW.Utils;

namespace industREAL.CAN.CANGW.V2
{
    public static class Constants
    {
        public const byte Version = 0x01;
        public const int  HeaderSize = 7;
        public const int  CrcSize = 2;
        public const int  Overhead = HeaderSize + CrcSize;
        public const int  MaxPayload = 1200;
        public const int  MaxFrame = MaxPayload + Overhead;

        public const byte ChannelA      = 0x00;
        public const byte ChannelB      = 0x01;
        public const byte ChannelDevice = 0xFE;
        public const byte ChannelAny    = 0xFF;
    }

    public enum FrameType : byte
    {
        DataFrame = 0x01,
        DataBatch = 0x02,
        CtrlReq   = 0x10,
        CtrlRsp   = 0x11,
        Event     = 0x20,
        Subscribe = 0x30,
        SubAck    = 0x31,
        Heartbeat = 0x40,
        TimeReq   = 0x50,
        TimeRsp   = 0x51,
    }

    public enum CtrlOp : byte    { Get = 0x01, Set = 0x02 }
    public enum CtrlStatus : byte { Ok = 0x00, Nack = 0x01, Busy = 0x02, Denied = 0x03 }

    public enum CtrlCmd : byte
    {
        Serial         = (byte)'s',
        HwVersion      = (byte)'h',
        FwVersion      = (byte)'f',
        Termination    = (byte)'t',
        Bootstrap      = (byte)'b',
        Baud           = (byte)'L',
        SystemDate     = (byte)'d',
        IpConfig       = (byte)'I',
        Gateway        = (byte)'g',
        WifiOnOff      = (byte)'w',
        Reboot         = (byte)'r',
        WifiStaMode    = (byte)'c',
        ApSsid         = (byte)'n',
        WifiPassword   = (byte)'p',
        Health         = (byte)'1',
        Led            = (byte)'D',
        Identify       = (byte)'X',
    }

    public enum SubscribeMode : byte { Release = 0x00, Owned = 0x01, ListenOnly = 0x02 }

    public readonly record struct Envelope(
        byte Version,
        FrameType Type,
        byte Channel,
        ushort Seq,
        ReadOnlyMemory<byte> Payload);

    public static class Protocol
    {
        public static int Encode(Span<byte> dest, FrameType type, byte channel,
                                 ushort seq, ReadOnlySpan<byte> payload)
        {
            int total = Constants.HeaderSize + payload.Length + Constants.CrcSize;
            if (dest.Length < total) throw new ArgumentException("buffer too small");
            if (payload.Length > Constants.MaxPayload) throw new ArgumentException("payload too large");

            dest[0] = Constants.Version;
            dest[1] = (byte)type;
            dest[2] = channel;
            dest[3] = (byte)seq;
            dest[4] = (byte)(seq >> 8);
            dest[5] = (byte)payload.Length;
            dest[6] = (byte)(payload.Length >> 8);
            payload.CopyTo(dest.Slice(Constants.HeaderSize));

            ushort crc = Crc16.CcittFalse(dest.Slice(0, Constants.HeaderSize + payload.Length));
            int crcPos = Constants.HeaderSize + payload.Length;
            dest[crcPos]     = (byte)crc;
            dest[crcPos + 1] = (byte)(crc >> 8);
            return total;
        }

        public static bool TryDecode(ReadOnlyMemory<byte> data, out Envelope env)
        {
            env = default;
            var span = data.Span;
            if (span.Length < Constants.Overhead) return false;
            if (span[0] != Constants.Version)     return false;

            int payloadLen = span[5] | (span[6] << 8);
            if (Constants.HeaderSize + payloadLen + Constants.CrcSize != span.Length) return false;

            int crcPos = Constants.HeaderSize + payloadLen;
            ushort crcWire = (ushort)(span[crcPos] | (span[crcPos + 1] << 8));
            ushort crcCalc = Crc16.CcittFalse(span.Slice(0, crcPos));
            if (crcWire != crcCalc) return false;

            env = new Envelope(
                span[0],
                (FrameType)span[1],
                span[2],
                (ushort)(span[3] | (span[4] << 8)),
                data.Slice(Constants.HeaderSize, payloadLen));
            return true;
        }

        /// COBS-encode the envelope and append the 0x00 delimiter.
        public static byte[] FrameForWire(ReadOnlySpan<byte> envelope)
        {
            var arr = envelope.ToArray();
            return COBS.Encode(arr);
        }
    }
}
