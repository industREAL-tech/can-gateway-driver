using industREAL.CAN.CANGW.Utils;
using Xunit;

namespace industREAL.CAN.CANGW.Tests;

public class CobsTests
{
    /// <summary>
    /// Frames produced by the gateway firmware's own Core/Src/cobs.c, as
    /// "&lt;payloadLen&gt; &lt;payloadHex&gt; &lt;encodedHex&gt;".
    /// </summary>
    public static IEnumerable<object[]> FirmwareVectors()
    {
        foreach (var line in File.ReadAllLines("cobs-vectors.txt"))
        {
            var f = line.Split(' ');
            yield return new object[] { Convert.FromHexString(f[1]), Convert.FromHexString(f[2]) };
        }
    }

    [Theory]
    [MemberData(nameof(FirmwareVectors))]
    public void EncodeMatchesTheFirmwareByteForByte(byte[] payload, byte[] firmwareEncoded)
    {
        // Regression: Encode() never emitted the 0xFF continuation block required
        // every 254 non-zero bytes, and truncated the block-length byte with a
        // cast, so a long zero-free run produced an undecodable stream.
        Assert.Equal(firmwareEncoded, COBS.Encode(payload));
    }

    [Theory]
    [MemberData(nameof(FirmwareVectors))]
    public void DecodeRecoversTheFirmwarePayloadExactly(byte[] payload, byte[] firmwareEncoded)
    {
        // Regression: Decode() returned a byte[] sized input.Length - 2, an upper
        // bound rather than the length, so every continuation block left a trailing
        // 0x00 on the payload. FrameDeserializer then walked past the last record
        // and threw, and CanGateway discarded the whole datagram.
        Assert.Equal(payload, COBS.Decode(firmwareEncoded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(508)]
    [InlineData(1200)]
    public void RoundTripsZeroFreePayloadsAcrossTheBlockBoundaries(int n)
    {
        var payload = new byte[n];
        for (int i = 0; i < n; i++) payload[i] = (byte)((i * 7 + 1) | 1);

        var enc = COBS.Encode(payload);
        Assert.Equal(0, enc[^1]);
        Assert.DoesNotContain((byte)0, enc[..^1]);   // no interior zero in a COBS frame
        Assert.Equal(payload, n == 0 ? Array.Empty<byte>() : COBS.Decode(enc));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(300)]
    [InlineData(600)]
    public void RoundTripsPayloadsContainingZeros(int n)
    {
        foreach (var payload in new[]
        {
            new byte[n],                                                        // all zeros
            Enumerable.Range(0, n).Select(i => (byte)(i % 3 == 0 ? 0 : i)).ToArray(),
            Enumerable.Range(0, n).Select(i => (byte)(i % 2 == 0 ? 0 : 0xFF)).ToArray(),
        })
        {
            Assert.Equal(payload, COBS.Decode(COBS.Encode(payload)));
        }
    }

    [Fact]
    public void WorstCaseEncodedSizeIsTheDocumentedBound()
    {
        var payload = Enumerable.Repeat((byte)0x5A, 1200).ToArray();
        var enc = COBS.Encode(payload);

        Assert.Equal(1206, enc.Length);
        Assert.Equal(COBS.MaxEncodedLength(1200), enc.Length);
    }

    [Fact]
    public void EmptyInputProducesTheMinimalFrame()
    {
        Assert.Equal(new byte[] { 0x01, 0x00 }, COBS.Encode(Array.Empty<byte>()));
        Assert.Equal(new byte[] { 0x01, 0x00 }, COBS.Encode(null!));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0x01, 0x01 })]              // no delimiter
    [InlineData(new byte[] { 0x05, 0x01, 0x02, 0x00 })]  // block overruns the delimiter
    [InlineData(new byte[] { 0x00, 0x00 })]              // zero code byte
    public void DecodeRejectsMalformedFrames(byte[] bad)
    {
        Assert.Throws<ArgumentException>(() => COBS.Decode(bad));
    }

    [Fact]
    public void DecodeRejectsNull()
    {
        Assert.Throws<ArgumentException>(() => COBS.Decode(null!));
    }
}
