using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using industREAL.CAN.CANGW;
using industREAL.CAN.CORE;

namespace industREAL.CAN.CANGW
{
    public class FrameDeserializer
    {
        // Method to deserialize the byte array into a list of tCanGwFrame objects
        public static List<CanFrame> DeserializeFrameBuffer(byte[] buffer)
        {
            List<CanFrame> frames = new List<CanFrame>();

            // Skip the first byte which is always '#'
            int currentOffset = 1;

            // Loop through the buffer and extract frames
            while (currentOffset < buffer.Length)
            {
                // Check if there are enough bytes left for the header (10 bytes)
                if (currentOffset + 14 > buffer.Length)
                {
                    throw new InvalidDataException("Not enough data for the frame header.");
                }

                // Extract the 14-byte header (canId, flags, dataLength, canChannel)
                CanFrame frame = new CanFrame((CanDLC)buffer[currentOffset + 12])
                {
                    _canID      = BitConverter.ToUInt32(buffer, currentOffset),
                    _flags      = (CanFlags)BitConverter.ToUInt32(buffer, currentOffset + 4),
                    _frameID    = (uint)BitConverter.ToUInt32(buffer, currentOffset + 8),
                    _dataLength = (CanDLC)buffer[currentOffset + 12],
                    _channel    = (CanInterface)buffer[currentOffset + 13]
                };

                // Update the current offset after reading the header
                currentOffset += 14;

                // Check if there’s enough space for the data[] based on dataLength
                if (currentOffset + (int)frame._dataLength > buffer.Length)
                {
                    throw new InvalidDataException("Not enough data for the frame data array.");
                }

                // Extract the data[] array based on the DataLength
                Array.Copy(buffer, currentOffset, frame._data, 0, (int)frame._dataLength);

                // Update the current offset after reading the data
                currentOffset += (int)frame._dataLength;

                // Add the frame to the list
                frames.Add(frame);

                /*if (frame.DataLength > 8)
                {
                    throw new InvalidDataException("Arulas van!");
                }*/
            }

            return frames;
        }



        public static byte[] SerializeWithSOF(CanFrame frame)
        {
            // Use a MemoryStream and BinaryWriter for serialization
            using (MemoryStream ms = new MemoryStream())
            {
                // Create a binary writer to write the object's data to the memory stream
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    // Write the fields of tCanGwFrame to the stream
                    writer.Write('s');
                    writer.Write((uint)frame._canID);
                    writer.Write((uint)frame._flags);
                    writer.Write((uint)frame._frameID);
                    writer.Write((byte)frame._dataLength);
                    writer.Write((byte)frame._channel);

                    // Write the Data array, ensure to write its length first
                    //writer.Write((ushort)frame.Data.Length);  // Write length of the Data array
                    writer.Write(frame._data);  // Write the actual byte array

                    return ms.ToArray();  // Return the byte array representation of the object
                }
            }
        }

        public static byte[] SerializeMultipleWithSOF(CanFrame[] frames)
        {
            // Use a MemoryStream and BinaryWriter for serialization
            using (MemoryStream ms = new MemoryStream())
            {
                // Create a binary writer to write the object's data to the memory stream
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    // Write the fields of tCanGwFrame to the stream
                    writer.Write('m');
                    writer.Write((byte)frames.Length);  /* Number of CAN Frames in the UDP packet */
                    foreach (var frame in frames)
                    {
                        writer.Write((uint)frame._canID);
                        writer.Write((uint)frame._flags);
                        writer.Write((uint)frame._frameID);
                        writer.Write((byte)frame._dataLength);
                        writer.Write((byte)frame._channel);
                        writer.Write(frame._data);       // Write the actual byte array
                    }
                    // Write the Data array, ensure to write its length first
                    //writer.Write((ushort)frame.Data.Length);  // Write length of the Data array

                    return ms.ToArray();  // Return the byte array representation of the object
                }
            }
        }

    }
}
