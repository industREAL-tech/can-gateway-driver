// CRC-16/CCITT-FALSE — poly 0x1021, init 0xFFFF, no reflection, xor 0x0000.
// Mirrors the firmware Crc16_CcittFalse so envelope CRC verifies in both
// directions on every transport.

using System;

namespace industREAL.CAN.CANGW.Utils
{
    public static class Crc16
    {
        private static readonly ushort[] Table = BuildTable();

        private static ushort[] BuildTable()
        {
            var t = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                ushort crc = (ushort)(i << 8);
                for (int k = 0; k < 8; k++)
                {
                    crc = ((crc & 0x8000) != 0)
                        ? (ushort)((crc << 1) ^ 0x1021)
                        : (ushort)(crc << 1);
                }
                t[i] = crc;
            }
            return t;
        }

        public static ushort CcittFalse(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
                crc = (ushort)((crc << 8) ^ Table[((crc >> 8) ^ b) & 0xFF]);
            return crc;
        }
    }
}
