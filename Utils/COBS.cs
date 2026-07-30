using System;

namespace industREAL.CAN.CANGW.Utils
{
    /// <summary>
    /// Consistent Overhead Byte Stuffing, matching the gateway firmware's
    /// Core/Src/cobs.c byte for byte.
    /// </summary>
    public static class COBS
    {
        /// <summary>Worst-case encoded size: one overhead byte per 254-byte
        /// block, plus the leading code byte and the trailing delimiter.</summary>
        public static int MaxEncodedLength(int payloadLength)
            => payloadLength + (payloadLength / 254) + 2;

        public static byte[] Encode(byte[] input)
        {
            if (input == null || input.Length == 0)
                return new byte[] { 0x01, 0x00 }; // Minimum valid COBS packet

            int inputLength = input.Length;
            byte[] output = new byte[MaxEncodedLength(inputLength)];
            int writeIndex = 1;     // output[0] is the first block's code byte
            int blockStart = 0;
            int code = 1;           // bytes in the current block, + 1

            for (int i = 0; i < inputLength; i++)
            {
                if (input[i] == 0)
                {
                    // A zero ends the block; its code byte stands in for it.
                    output[blockStart] = (byte)code;
                    blockStart = writeIndex++;
                    code = 1;
                }
                else
                {
                    output[writeIndex++] = input[i];
                    code++;

                    // A block carries at most 254 data bytes. At that point emit
                    // the 0xFF continuation code and open a new block without
                    // consuming an input byte.
                    //
                    // This rule was missing: the code byte was computed once at
                    // the end as (inputLength - blockStart + 1) and truncated by
                    // the cast, so any run of more than 254 non-zero bytes
                    // produced a stream no decoder could read. Reachable from a
                    // multi-frame batch of CAN-FD frames with non-zero payloads.
                    if (code == 0xFF)
                    {
                        output[blockStart] = (byte)code;
                        blockStart = writeIndex++;
                        code = 1;
                    }
                }
            }

            output[blockStart] = (byte)code;
            output[writeIndex++] = 0x00; // Frame delimiter

            Array.Resize(ref output, writeIndex);
            return output;
        }

        public static byte[] Decode(byte[] input)
        {
            if (input == null || input.Length < 2 || input[^1] != 0)
                throw new ArgumentException("Invalid COBS encoded data.");

            // input.Length - 2 is an upper bound on the decoded size, not the
            // exact size: every 0xFF continuation block costs one extra encoded
            // byte that yields no decoded byte. Returning the whole buffer left
            // one trailing 0x00 per continuation block on the payload, which
            // made FrameDeserializer walk past the last record and throw, so the
            // entire datagram was discarded.
            byte[] output = new byte[input.Length - 2];
            int readIndex = 0, writeIndex = 0;
            int end = input.Length - 1;   // index of the delimiter

            while (readIndex < end)
            {
                int blockSize = input[readIndex++];
                if (blockSize == 0)
                    throw new ArgumentException("Invalid COBS encoding structure.");

                int dataBytes = blockSize - 1;
                if (readIndex + dataBytes > end)
                    throw new ArgumentException("Invalid COBS encoding structure.");
                if (writeIndex + dataBytes > output.Length)
                    throw new ArgumentException("Invalid COBS encoding structure.");

                Buffer.BlockCopy(input, readIndex, output, writeIndex, dataBytes);
                readIndex += dataBytes;
                writeIndex += dataBytes;

                // A code below 0xFF means the block was terminated by a zero in
                // the original data; 0xFF means it was a full continuation block
                // and no zero is restored.
                if (blockSize < 0xFF && readIndex < end)
                {
                    if (writeIndex >= output.Length)
                        throw new ArgumentException("Invalid COBS encoding structure.");
                    output[writeIndex++] = 0x00;
                }
            }

            if (writeIndex != output.Length)
                Array.Resize(ref output, writeIndex);

            return output;
        }
    }
}
