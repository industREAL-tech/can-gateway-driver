// COBS regression tests.
//
// Both halves of this codec shipped broken and only misbehaved on runs of 254
// or more consecutive non-zero bytes:
//
//   * Encode never closed a full block with a 0xFF code, so the block length
//     overflowed a single byte and produced a stream that this file's own
//     (correct) decoder could not read.
//   * Decode sized its output as input.Length - 2, which is exact only when the
//     frame contains no full block. Every 0xFF block left one extra byte on the
//     end, and the envelope then failed its CRC.
//
// Constants.MaxPayload is 1200, so both were reachable by any large frame batch.
// They survived because real v2 traffic stayed short and zero-sprinkled. The
// boundary is pinned at 253/254 explicitly so neither can come back.

using industREAL.CAN.CANGW.Utils;

namespace industREAL.CAN.CANGW.Tests
{
    internal static class CobsTests
    {
        public static void RunAll()
        {
            var cases = new List<byte[]>
            {
                new byte[] { 0x00 },
                new byte[] { 0x11, 0x22, 0x00, 0x33 },
                new byte[] { 0x11, 0x00, 0x00, 0x00 },
                Enumerable.Range(1, 253).Select(i => (byte)i).ToArray(),
                Enumerable.Range(1, 254).Select(i => (byte)(i % 255 + 1)).ToArray(),
                Enumerable.Range(0, 300).Select(i => (byte)(i % 255 + 1)).ToArray(),
                Enumerable.Range(0, 1207).Select(i => (byte)(i % 255 + 1)).ToArray(),
                Enumerable.Range(0, 1207).Select(i => (byte)(i % 256)).ToArray(),
            };

            // Fixed seed: a failure must be reproducible.
            var rnd = new Random(1234);
            for (int n = 0; n < 400; n++)
            {
                int len = rnd.Next(1, 1208);
                var b = new byte[len];
                for (int i = 0; i < len; i++)
                    b[i] = (byte)(rnd.Next(100) < 8 ? 0 : rnd.Next(1, 256));
                cases.Add(b);
            }

            foreach (var input in cases)
            {
                byte[] wire = COBS.Encode(input);
                Check(wire[^1] == 0x00, $"delimiter (len {input.Length})");
                Check(Array.IndexOf(wire, (byte)0x00) == wire.Length - 1,
                      $"body is zero-free (len {input.Length})");
                byte[] back = COBS.Decode(wire);
                Check(back.AsSpan().SequenceEqual(input), $"round-trip (len {input.Length})");
            }

            // 254 zero-free bytes must give one full block plus the empty
            // trailing block: [FF][254 bytes][01][00].
            byte[] full = Enumerable.Repeat((byte)0xAB, 254).ToArray();
            byte[] enc = COBS.Encode(full);
            Check(enc.Length == 257, "254 zero-free bytes encode to 257");
            Check(enc[0] == 0xFF, "full block closed with a 0xFF code");
            Check(enc[255] == 0x01 && enc[256] == 0x00, "trailing empty block then delimiter");

            // 253 stays a single ordinary block: [FE][253 bytes][00].
            byte[] just = Enumerable.Repeat((byte)0xAB, 253).ToArray();
            byte[] encJust = COBS.Encode(just);
            Check(encJust.Length == 255, "253 zero-free bytes encode to 255");
            Check(encJust[0] == 0xFE, "253 bytes is one ordinary block");
        }

        private static void Check(bool cond, string what)
        {
            if (!cond) throw new InvalidOperationException($"COBS self-check failed: {what}");
        }
    }
}
