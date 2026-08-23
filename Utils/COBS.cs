using System;

namespace industREAL.CAN.CANGW.Utils
{
    /// <summary>
    /// COBS codec for the legacy v2 wire format.
    /// </summary>
    /// <remarks>
    /// The encoder here is a corrected version of the one that shipped in
    /// cangateway.dotnetdriver. The original only started a new block when it hit
    /// a zero byte, so a run of 254 or more non-zero bytes overflowed the block
    /// length into a single byte and produced a stream its own (correct) decoder
    /// could not read. With <c>Constants.MaxPayload = 1200</c> that was reachable
    /// by any large frame batch; it survived because v2 traffic in practice stayed
    /// short. Runs of 254 bytes now close the block with a 0xFF code, per the COBS
    /// specification and matching <see cref="Decode"/>.
    /// </remarks>
    public static class COBS
    {
        /// <summary>Encode a buffer and append the 0x00 frame delimiter.</summary>
        public static byte[] Encode(byte[] input)
        {
            if (input == null || input.Length == 0)
                return new byte[] { 0x01, 0x00 }; // Minimum valid COBS packet

            byte[] output = new byte[input.Length + input.Length / 254 + 2];
            int codeIndex = 0;      // position of the current block-length byte
            int writeIndex = 1;
            byte code = 1;

            foreach (byte b in input)
            {
                if (b == 0)
                {
                    output[codeIndex] = code;
                    codeIndex = writeIndex++;
                    code = 1;
                }
                else
                {
                    output[writeIndex++] = b;
                    // 0xFF closes a full block with no implicit zero after it.
                    if (++code == 0xFF)
                    {
                        output[codeIndex] = code;
                        codeIndex = writeIndex++;
                        code = 1;
                    }
                }
            }

            output[codeIndex] = code;
            output[writeIndex++] = 0x00; // Frame delimiter

            Array.Resize(ref output, writeIndex);
            return output;
        }

        public static byte[] Decode(byte[] input)
        {
            if (input == null || input.Length < 2 || input[^1] != 0)
                throw new ArgumentException("Invalid COBS encoded data.");

            byte[] output = new byte[input.Length - 2];
            int readIndex = 0, writeIndex = 0;

            while (readIndex < input.Length - 1)
            {
                int blockSize = input[readIndex++];
                if (blockSize == 0 || readIndex + blockSize - 1 > input.Length - 1)
                    throw new ArgumentException("Invalid COBS encoding structure.");

                for (int i = 1; i < blockSize; i++)
                    output[writeIndex++] = input[readIndex++];

                if (blockSize < 255 && readIndex < input.Length - 1)
                    output[writeIndex++] = 0x00;
            }

            // The buffer above is sized as input.Length - 2, which is exact only
            // when the frame contains no 0xFF block. Every full 254-byte block
            // adds a code byte that carries no data, so without this trim the
            // decode returns a buffer one byte too long per such block and the
            // envelope then fails its CRC check.
            Array.Resize(ref output, writeIndex);
            return output;
        }
    }
}
