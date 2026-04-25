using System;

namespace industREAL.CAN.CANGW.Utils
{
    public static class COBS
    {
        public static byte[] Encode(byte[] input)
        {
            if (input == null || input.Length == 0)
                return new byte[] { 0x01, 0x00 }; // Minimum valid COBS packet

            int inputLength = input.Length;
            byte[] output = new byte[inputLength + (inputLength / 254) + 2];
            int readIndex = 0, writeIndex = 1;
            int blockStart = 0;

            for (int i = 0; i < inputLength; i++)
            {
                if (input[i] == 0)
                {
                    output[blockStart] = (byte)(i - blockStart + 1);
                    blockStart = writeIndex++;
                }
                else
                {
                    output[writeIndex++] = input[i];
                }
            }

            output[blockStart] = (byte)(inputLength - blockStart + 1);
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

            return output;
        }
    }
}
