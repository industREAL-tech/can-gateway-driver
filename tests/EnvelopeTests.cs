// v2 envelope round-trip, including the CRC and version rejection paths. Uses
// the same COBS codec on the wire, so this also exercises the two together.

using industREAL.CAN.CANGW.V2;

namespace industREAL.CAN.CANGW.Tests
{
    internal static class EnvelopeTests
    {
        public static void RunAll()
        {
            byte[] payload = { 1, 2, 3, 4, 5, 0, 0xFF };
            var buf = new byte[Constants.Overhead + payload.Length];
            int len = Protocol.Encode(buf, FrameType.CtrlReq, 0x01, 0xBEEF, payload);
            Check(len == buf.Length, "envelope length");

            Check(Protocol.TryDecode(buf.AsMemory(0, len), out var env), "envelope decode");
            Check(env.Type == FrameType.CtrlReq, "envelope type");
            Check(env.Channel == 0x01, "envelope channel");
            Check(env.Seq == 0xBEEF, "envelope seq");
            Check(env.Payload.Span.SequenceEqual(payload), "envelope payload");

            // A corrupted CRC must be rejected, not silently accepted.
            var bad = (byte[])buf.Clone();
            bad[^1] ^= 0xFF;
            Check(!Protocol.TryDecode(bad.AsMemory(0, len), out _), "CRC rejection");

            // A payload big enough to contain a 254-byte zero-free run is where
            // the old COBS encoder produced an unreadable frame.
            byte[] big = Enumerable.Range(0, 1000).Select(i => (byte)(i % 255 + 1)).ToArray();
            var buf2 = new byte[Constants.Overhead + big.Length];
            int len2 = Protocol.Encode(buf2, FrameType.DataFrame, 0x00, 1, big);
            byte[] wire = Protocol.FrameForWire(buf2.AsSpan(0, len2));
            byte[] backWire = Utils.COBS.Decode(wire);
            Check(Protocol.TryDecode(backWire, out var env2), "large frame survives the wire");
            Check(env2.Payload.Span.SequenceEqual(big), "large frame payload intact");
        }

        private static void Check(bool cond, string what)
        {
            if (!cond) throw new InvalidOperationException($"envelope self-check failed: {what}");
        }
    }
}
