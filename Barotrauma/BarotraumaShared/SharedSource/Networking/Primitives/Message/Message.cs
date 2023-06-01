﻿using Lidgren.Network;
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    public static class MsgConstants
    {
        public const int MTU = 1200; //TODO: determine dynamically
        public const int CompressionThreshold = 1000;
        public const int InitialBufferSize = 256;
        public const int BufferOverAllocateAmount = 4;
    }

    /// <summary>
    /// Utility struct for writing Singles
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct SingleUIntUnion
    {
        /// <summary>
        /// Value as a 32 bit float
        /// </summary>
        [FieldOffset(0)]
        public float SingleValue;

        /// <summary>
        /// Value as an unsigned 32 bit integer
        /// </summary>
        [FieldOffset(0)]
        public uint UIntValue;
    }

    internal static class MsgWriter
    {
        internal static void UpdateBitLength(ref int bitLength, int bitPos)
        {
            bitLength = Math.Max(bitLength, bitPos);
        }
        
        internal static void WriteBoolean(ref byte[] buf, ref int bitPos, ref int bitLength, bool val)
        {
#if DEBUG
            int resetPos = bitPos;
#endif

            EnsureBufferSize(ref buf, bitPos + 1);

            int bytePos = bitPos / 8;
            int bitOffset = bitPos % 8;
            byte bitFlag = (byte)(1 << bitOffset);
            byte bitMask = (byte)((~bitFlag) & 0xff);
            buf[bytePos] &= bitMask;
            if (val) buf[bytePos] |= bitFlag;
            bitPos++;
            UpdateBitLength(ref bitLength, bitPos);
#if DEBUG
            bool testVal = MsgReader.ReadBoolean(buf, ref resetPos);
            if (testVal != val || resetPos != bitPos)
            {
                DebugConsole.ThrowError($"Boolean written incorrectly! {testVal}, {val}; {resetPos}, {bitPos}");
            }
#endif
        }

        internal static void WritePadBits(ref byte[] buf, ref int bitPos, ref int bitLength)
        {
            int bitOffset = bitPos % 8;
            bitPos += ((8 - bitOffset) % 8);
            UpdateBitLength(ref bitLength, bitPos);
            EnsureBufferSize(ref buf, bitPos);
        }

        internal static void WriteByte(ref byte[] buf, ref int bitPos, ref int bitLength, byte val)
        {
            EnsureBufferSize(ref buf, bitPos + 8);
            NetBitWriter.WriteByte(val, 8, buf, bitPos);
            bitPos += 8;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteUInt16(ref byte[] buf, ref int bitPos, ref int bitLength, UInt16 val)
        {
            EnsureBufferSize(ref buf, bitPos + 16);
            NetBitWriter.WriteUInt16(val, 16, buf, bitPos);
            bitPos += 16;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteInt16(ref byte[] buf, ref int bitPos, ref int bitLength, Int16 val)
        {
            EnsureBufferSize(ref buf, bitPos + 16);
            NetBitWriter.WriteUInt16((UInt16)val, 16, buf, bitPos);
            bitPos += 16;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteUInt32(ref byte[] buf, ref int bitPos, ref int bitLength, UInt32 val)
        {
            EnsureBufferSize(ref buf, bitPos + 32);
            NetBitWriter.WriteUInt32(val, 32, buf, bitPos);
            bitPos += 32;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteInt32(ref byte[] buf, ref int bitPos, ref int bitLength, Int32 val)
        {
            EnsureBufferSize(ref buf, bitPos + 32);
            NetBitWriter.WriteUInt32((UInt32)val, 32, buf, bitPos);
            bitPos += 32;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteUInt64(ref byte[] buf, ref int bitPos, ref int bitLength, UInt64 val)
        {
            EnsureBufferSize(ref buf, bitPos + 64);
            NetBitWriter.WriteUInt64(val, 64, buf, bitPos);
            bitPos += 64;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteInt64(ref byte[] buf, ref int bitPos, ref int bitLength, Int64 val)
        {
            EnsureBufferSize(ref buf, bitPos + 64);
            NetBitWriter.WriteUInt64((UInt64)val, 64, buf, bitPos);
            bitPos += 64;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteSingle(ref byte[] buf, ref int bitPos, ref int bitLength, Single val)
        {
            // Use union to avoid BitConverter.GetBytes() which allocates memory on the heap
            SingleUIntUnion su;
            su.UIntValue = 0; // must initialize every member of the union to avoid warning
            su.SingleValue = val;

            EnsureBufferSize(ref buf, bitPos + 32);

            NetBitWriter.WriteUInt32(su.UIntValue, 32, buf, bitPos);
            bitPos += 32;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteDouble(ref byte[] buf, ref int bitPos, ref int bitLength, Double val)
        {
            EnsureBufferSize(ref buf, bitPos + 64);

            byte[] bytes = BitConverter.GetBytes(val);
            WriteBytes(ref buf, ref bitPos, ref bitLength, bytes, 0, 8);
        }

        internal static void WriteColorR8G8B8(ref byte[] buf, ref int bitPos, ref int bitLength, Color val)
        {
            EnsureBufferSize(ref buf, bitPos + 24);

            WriteByte(ref buf, ref bitPos, ref bitLength, val.R);
            WriteByte(ref buf, ref bitPos, ref bitLength, val.G);
            WriteByte(ref buf, ref bitPos, ref bitLength, val.B);
        }

        internal static void WriteColorR8G8B8A8(ref byte[] buf, ref int bitPos, ref int bitLength, Color val)
        {
            EnsureBufferSize(ref buf, bitPos + 32);

            WriteByte(ref buf, ref bitPos, ref bitLength, val.R);
            WriteByte(ref buf, ref bitPos, ref bitLength, val.G);
            WriteByte(ref buf, ref bitPos, ref bitLength, val.B);
            WriteByte(ref buf, ref bitPos, ref bitLength, val.A);
        }

        internal static void WriteString(ref byte[] buf, ref int bitPos, ref int bitLength, string val)
        {
            if (string.IsNullOrEmpty(val))
            {
                WriteVariableUInt32(ref buf, ref bitPos, ref bitLength, 0u);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(val);
            WriteVariableUInt32(ref buf, ref bitPos, ref bitLength, (uint)bytes.Length);
            WriteBytes(ref buf, ref bitPos, ref bitLength, bytes, 0, bytes.Length);
        }

        internal static void WriteVariableUInt32(ref byte[] buf, ref int bitPos, ref int bitLength, uint value)
        {
            uint remainingValue = value;
            while (remainingValue >= 0x80)
            {
                WriteByte(ref buf, ref bitPos, ref bitLength, (byte)(remainingValue | 0x80));
                remainingValue >>= 7;
            }

            WriteByte(ref buf, ref bitPos, ref bitLength, (byte)remainingValue);
        }

        internal static void WriteRangedInteger(ref byte[] buf, ref int bitPos, ref int bitLength, int val, int min, int max)
        {
            uint range = (uint)(max - min);
            int numberOfBits = NetUtility.BitsToHoldUInt(range);

            EnsureBufferSize(ref buf, bitPos + numberOfBits);

            uint rvalue = (uint)(val - min);
            NetBitWriter.WriteUInt32(rvalue, numberOfBits, buf, bitPos);
            bitPos += numberOfBits;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteRangedSingle(ref byte[] buf, ref int bitPos, ref int bitLength, Single val, Single min, Single max, int numberOfBits)
        {
            float range = max - min;
            float unit = ((val - min) / range);
            int maxVal = (1 << numberOfBits) - 1;

            EnsureBufferSize(ref buf, bitPos + numberOfBits);

            NetBitWriter.WriteUInt32((UInt32)(maxVal * unit), numberOfBits, buf, bitPos);
            bitPos += numberOfBits;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void WriteBytes(ref byte[] buf, ref int bitPos, ref int bitLength, byte[] val, int pos, int length)
        {
            EnsureBufferSize(ref buf, bitPos + length * 8);
            NetBitWriter.WriteBytes(val, pos, length, buf, bitPos);
            bitPos += length * 8;
            UpdateBitLength(ref bitLength, bitPos);
        }

        internal static void EnsureBufferSize(ref byte[] buf, int numberOfBits)
        {
            int byteLen = (numberOfBits + 7) / 8;
            if (buf == null)
            {
                buf = new byte[byteLen + MsgConstants.BufferOverAllocateAmount];
                return;
            }

            if (buf.Length < byteLen)
            {
                Array.Resize(ref buf, byteLen + MsgConstants.BufferOverAllocateAmount);
            }
        }
    }

    internal static class MsgReader
    {
        internal static bool ReadBoolean(byte[] buf, ref int bitPos)
        {
            byte retval = NetBitWriter.ReadByte(buf, 1, bitPos);
            bitPos++;
            return retval > 0;
        }

        internal static void ReadPadBits(ref int bitPos)
        {
            int bitOffset = bitPos % 8;
            bitPos += (8 - bitOffset) % 8;
        }

        internal static byte ReadByte(byte[] buf, ref int bitPos)
        {
            byte retval = NetBitWriter.ReadByte(buf, 8, bitPos);
            bitPos += 8;
            return retval;
        }

        internal static byte PeekByte(byte[] buf, ref int bitPos)
        {
            byte retval = NetBitWriter.ReadByte(buf, 8, bitPos);
            return retval;
        }

        internal static UInt16 ReadUInt16(byte[] buf, ref int bitPos)
        {
            uint retval = NetBitWriter.ReadUInt16(buf, 16, bitPos);
            bitPos += 16;
            return (ushort)retval;
        }

        internal static Int16 ReadInt16(byte[] buf, ref int bitPos)
        {
            return (Int16)ReadUInt16(buf, ref bitPos);
        }

        internal static UInt32 ReadUInt32(byte[] buf, ref int bitPos)
        {
            uint retval = NetBitWriter.ReadUInt32(buf, 32, bitPos);
            bitPos += 32;
            return retval;
        }

        internal static Int32 ReadInt32(byte[] buf, ref int bitPos)
        {
            return (Int32)ReadUInt32(buf, ref bitPos);
        }

        internal static UInt64 ReadUInt64(byte[] buf, ref int bitPos)
        {
            ulong low = NetBitWriter.ReadUInt32(buf, 32, bitPos);
            bitPos += 32;
            ulong high = NetBitWriter.ReadUInt32(buf, 32, bitPos);
            ulong retval = low + (high << 32);
            bitPos += 32;
            return retval;
        }

        internal static Int64 ReadInt64(byte[] buf, ref int bitPos)
        {
            return (Int64)ReadUInt64(buf, ref bitPos);
        }

        internal static Single ReadSingle(byte[] buf, ref int bitPos)
        {
            if ((bitPos & 7) == 0) // read directly
            {
                float retval = BitConverter.ToSingle(buf, bitPos >> 3);
                bitPos += 32;
                return retval;
            }

            byte[] bytes = ReadBytes(buf, ref bitPos, 4);
            return BitConverter.ToSingle(bytes, 0);
        }

        internal static Double ReadDouble(byte[] buf, ref int bitPos)
        {
            if ((bitPos & 7) == 0) // read directly
            {
                // read directly
                double retval = BitConverter.ToDouble(buf, bitPos >> 3);
                bitPos += 64;
                return retval;
            }

            byte[] bytes = ReadBytes(buf, ref bitPos, 8);
            return BitConverter.ToDouble(bytes, 0);
        }

        internal static Color ReadColorR8G8B8(byte[] buf, ref int bitPos)
        {
            byte r = ReadByte(buf, ref bitPos);
            byte g = ReadByte(buf, ref bitPos);
            byte b = ReadByte(buf, ref bitPos);
            return new Color(r, g, b, (byte)255);
        }

        internal static Color ReadColorR8G8B8A8(byte[] buf, ref int bitPos)
        {
            byte r = ReadByte(buf, ref bitPos);
            byte g = ReadByte(buf, ref bitPos);
            byte b = ReadByte(buf, ref bitPos);
            byte a = ReadByte(buf, ref bitPos);
            return new Color(r, g, b, a);
        }

        internal static UInt32 ReadVariableUInt32(byte[] buf, ref int bitPos)
        {
            int bitLength = buf.Length * 8;

            int result = 0;
            int shift = 0;
            while (bitLength - bitPos >= 8)
            {
                byte chunk = ReadByte(buf, ref bitPos);
                result |= (chunk & 0x7f) << shift;
                shift += 7;
                if ((chunk & 0x80) == 0) { return (uint)result; }
            }

            // ouch; failed to find enough bytes; malformed variable length number?
            return (uint)result;
        }

        internal static String ReadString(byte[] buf, ref int bitPos)
        {
            int bitLength = buf.Length * 8;
            int byteLen = (int)ReadVariableUInt32(buf, ref bitPos);

            if (byteLen <= 0) { return String.Empty; }

            if ((ulong)(bitLength - bitPos) < ((ulong)byteLen * 8))
            {
                // not enough data
                return null;
            }

            if ((bitPos & 7) == 0)
            {
                // read directly
                string retval = Encoding.UTF8.GetString(buf, bitPos >> 3, byteLen);
                bitPos += (8 * byteLen);
                return retval;
            }

            byte[] bytes = ReadBytes(buf, ref bitPos, byteLen);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        internal static int ReadRangedInteger(byte[] buf, ref int bitPos, int min, int max)
        {
            uint range = (uint)(max - min);
            int numBits = NetUtility.BitsToHoldUInt(range);

            uint rvalue = NetBitWriter.ReadUInt32(buf, numBits, bitPos);
            bitPos += numBits;

            return (int)(min + rvalue);
        }

        internal static Single ReadRangedSingle(byte[] buf, ref int bitPos, Single min, Single max, int bitCount)
        {
            int maxInt = (1 << bitCount) - 1;
            int intVal = ReadRangedInteger(buf, ref bitPos, 0, maxInt);
            Single range = max - min;
            return min + range * intVal / maxInt;
        }

        internal static byte[] ReadBytes(byte[] buf, ref int bitPos, int numberOfBytes)
        {
            byte[] retval = new byte[numberOfBytes];
            NetBitWriter.ReadBytes(buf, numberOfBytes, bitPos, retval, 0);
            bitPos += 8 * numberOfBytes;
            return retval;
        }
    }

    internal sealed class WriteOnlyMessage : IWriteMessage
    {
        private byte[] buf = new byte[MsgConstants.InitialBufferSize];
        private int seekPos;
        private int lengthBits;

        public int BitPosition
        {
            get => seekPos;
            set => seekPos = value;
        }

        public int BytePosition => seekPos / 8;

        public byte[] Buffer => buf;

        public int LengthBits
        {
            get
            {
                lengthBits = seekPos > lengthBits ? seekPos : lengthBits;
                return lengthBits;
            }
            set
            {
                lengthBits = value;
                seekPos = seekPos > lengthBits ? lengthBits : seekPos;
                MsgWriter.EnsureBufferSize(ref buf, lengthBits);
            }
        }

        public int LengthBytes => (LengthBits + 7) / 8;

        public void WriteBoolean(bool val)
        {
            MsgWriter.WriteBoolean(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WritePadBits()
        {
            MsgWriter.WritePadBits(ref buf, ref seekPos, ref lengthBits);
        }

        public void WriteByte(byte val)
        {
            MsgWriter.WriteByte(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt16(UInt16 val)
        {
            MsgWriter.WriteUInt16(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt16(Int16 val)
        {
            MsgWriter.WriteInt16(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt32(UInt32 val)
        {
            MsgWriter.WriteUInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt32(Int32 val)
        {
            MsgWriter.WriteInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt64(UInt64 val)
        {
            MsgWriter.WriteUInt64(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt64(Int64 val)
        {
            MsgWriter.WriteInt64(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteSingle(Single val)
        {
            MsgWriter.WriteSingle(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteDouble(Double val)
        {
            MsgWriter.WriteDouble(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteColorR8G8B8(Color val)
        {
            MsgWriter.WriteColorR8G8B8(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteColorR8G8B8A8(Color val)
        {
            MsgWriter.WriteColorR8G8B8A8(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteVariableUInt32(UInt32 val)
        {
            MsgWriter.WriteVariableUInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteString(String val)
        {
            MsgWriter.WriteString(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteIdentifier(Identifier val)
        {
            WriteString(val.Value);
        }

        public void WriteRangedInteger(int val, int min, int max)
        {
            MsgWriter.WriteRangedInteger(ref buf, ref seekPos, ref lengthBits, val, min, max);
        }

        public void WriteRangedSingle(Single val, Single min, Single max, int bitCount)
        {
            MsgWriter.WriteRangedSingle(ref buf, ref seekPos, ref lengthBits, val, min, max, bitCount);
        }

        public void WriteBytes(byte[] val, int startPos, int length)
        {
            MsgWriter.WriteBytes(ref buf, ref seekPos, ref lengthBits, val, startPos, length);
        }

        public byte[] PrepareForSending(bool compressPastThreshold, out bool isCompressed, out int length)
        {
            byte[] outBuf;
            if (LengthBytes <= MsgConstants.CompressionThreshold || !compressPastThreshold)
            {
                isCompressed = false;
                outBuf = new byte[LengthBytes];
                Array.Copy(buf, outBuf, LengthBytes);
                length = LengthBytes;
            }
            else
            {
                using MemoryStream output = new MemoryStream();

                using (DeflateStream dstream = new DeflateStream(output, CompressionLevel.Fastest))
                {
                    dstream.Write(buf, 0, LengthBytes);
                }

                byte[] compressedBuf = output.ToArray();
                //don't send the data as compressed if the data takes up more space after compression
                //(which may happen when sending a sub/save file that's already been compressed with a better compression ratio)
                if (compressedBuf.Length >= LengthBytes)
                {
                    isCompressed = false;
                    outBuf = new byte[LengthBytes];
                    Array.Copy(buf, outBuf, LengthBytes);
                    length = LengthBytes;
                }
                else
                {
                    isCompressed = true;
                    outBuf = compressedBuf;
                    length = outBuf.Length;
                    DebugConsole.Log($"Compressed message: {LengthBytes} to {length}");
                }
            }

            return outBuf;
        }
    }

    internal sealed class ReadOnlyMessage : IReadMessage
    {
        private int seekPos;
        private int lengthBits;

        public int BitPosition
        {
            get => seekPos;
            set => seekPos = value;
        }

        public int BytePosition => seekPos / 8;

        public byte[] Buffer { get; }

        public int LengthBits
        {
            get
            {
                lengthBits = seekPos > lengthBits ? seekPos : lengthBits;
                return lengthBits;
            }
            set
            {
                lengthBits = value;
                seekPos = seekPos > lengthBits ? lengthBits : seekPos;
            }
        }

        public int LengthBytes => (LengthBits + 7) / 8;

        public NetworkConnection Sender { get; }

        public ReadOnlyMessage(byte[] inBuf, bool isCompressed, int startPos, int byteLength, NetworkConnection sender)
        {
            Sender = sender;
            if (isCompressed)
            {
                byte[] decompressedData;
                using (MemoryStream input = new MemoryStream(inBuf, startPos, byteLength))
                {
                    using (MemoryStream output = new MemoryStream())
                    {
                        using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
                        {
                            dstream.CopyTo(output);
                        }

                        decompressedData = output.ToArray();
                    }
                }

                Buffer = new byte[decompressedData.Length];
                try
                {
                    Array.Copy(decompressedData, 0, Buffer, 0, decompressedData.Length);
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException(
                        $"Failed to copy the incoming compressed buffer. Source buffer length: {decompressedData.Length}, start position: {0}, length: {decompressedData.Length}, destination buffer length: {Buffer.Length}.", e);
                }

                lengthBits = decompressedData.Length * 8;
                DebugConsole.Log("Decompressing message: " + byteLength + " to " + LengthBytes);
            }
            else
            {
                Buffer = new byte[inBuf.Length];
                try
                {
                    Array.Copy(inBuf, startPos, Buffer, 0, byteLength);
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"Failed to copy the incoming uncompressed buffer. Source buffer length: {inBuf.Length}, start position: {startPos}, length: {byteLength}, destination buffer length: {Buffer.Length}.", e);
                }

                lengthBits = byteLength * 8;
            }

            seekPos = 0;
        }

        public bool ReadBoolean()
        {
            return MsgReader.ReadBoolean(Buffer, ref seekPos);
        }

        public void ReadPadBits() { MsgReader.ReadPadBits(ref seekPos); }

        public byte ReadByte()
        {
            return MsgReader.ReadByte(Buffer, ref seekPos);
        }

        public byte PeekByte()
        {
            return MsgReader.PeekByte(Buffer, ref seekPos);
        }

        public UInt16 ReadUInt16()
        {
            return MsgReader.ReadUInt16(Buffer, ref seekPos);
        }

        public Int16 ReadInt16()
        {
            return MsgReader.ReadInt16(Buffer, ref seekPos);
        }

        public UInt32 ReadUInt32()
        {
            return MsgReader.ReadUInt32(Buffer, ref seekPos);
        }

        public Int32 ReadInt32()
        {
            return MsgReader.ReadInt32(Buffer, ref seekPos);
        }

        public UInt64 ReadUInt64()
        {
            return MsgReader.ReadUInt64(Buffer, ref seekPos);
        }

        public Int64 ReadInt64()
        {
            return MsgReader.ReadInt64(Buffer, ref seekPos);
        }

        public Single ReadSingle()
        {
            return MsgReader.ReadSingle(Buffer, ref seekPos);
        }

        public Double ReadDouble()
        {
            return MsgReader.ReadDouble(Buffer, ref seekPos);
        }

        public UInt32 ReadVariableUInt32()
        {
            return MsgReader.ReadVariableUInt32(Buffer, ref seekPos);
        }

        public String ReadString()
        {
            return MsgReader.ReadString(Buffer, ref seekPos);
        }

        public Identifier ReadIdentifier()
        {
            return ReadString().ToIdentifier();
        }

        public Color ReadColorR8G8B8()
        {
            return MsgReader.ReadColorR8G8B8(Buffer, ref seekPos);
        }

        public Color ReadColorR8G8B8A8()
        {
            return MsgReader.ReadColorR8G8B8A8(Buffer, ref seekPos);
        }

        public int ReadRangedInteger(int min, int max)
        {
            return MsgReader.ReadRangedInteger(Buffer, ref seekPos, min, max);
        }

        public Single ReadRangedSingle(Single min, Single max, int bitCount)
        {
            return MsgReader.ReadRangedSingle(Buffer, ref seekPos, min, max, bitCount);
        }

        public byte[] ReadBytes(int numberOfBytes)
        {
            return MsgReader.ReadBytes(Buffer, ref seekPos, numberOfBytes);
        }
    }

    internal sealed class ReadWriteMessage : IWriteMessage, IReadMessage
    {
        private byte[] buf;
        private int seekPos;
        private int lengthBits;

        public ReadWriteMessage()
        {
            buf = new byte[MsgConstants.InitialBufferSize];
            seekPos = 0;
            lengthBits = 0;
        }

        public ReadWriteMessage(byte[] b, int bitPos, int lBits, bool copyBuf)
        {
            buf = copyBuf ? (byte[])b.Clone() : b;
            seekPos = bitPos;
            lengthBits = lBits;
        }

        public int BitPosition
        {
            get => seekPos;
            set => seekPos = value;
        }

        public int BytePosition => seekPos / 8;

        public byte[] Buffer => buf;

        public int LengthBits
        {
            get
            {
                lengthBits = seekPos > lengthBits ? seekPos : lengthBits;
                return lengthBits;
            }
            set
            {
                lengthBits = value;
                seekPos = seekPos > lengthBits ? lengthBits : seekPos;
            }
        }

        public int LengthBytes => (LengthBits + 7) / 8;

        public NetworkConnection Sender => null;

        public void WriteBoolean(bool val)
        {
            MsgWriter.WriteBoolean(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WritePadBits()
        {
            MsgWriter.WritePadBits(ref buf, ref seekPos, ref lengthBits);
        }

        public void WriteByte(byte val)
        {
            MsgWriter.WriteByte(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt16(UInt16 val)
        {
            MsgWriter.WriteUInt16(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt16(Int16 val)
        {
            MsgWriter.WriteInt16(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt32(UInt32 val)
        {
            MsgWriter.WriteUInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt32(Int32 val)
        {
            MsgWriter.WriteInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteUInt64(UInt64 val)
        {
            MsgWriter.WriteUInt64(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteInt64(Int64 val)
        {
            MsgWriter.WriteInt64(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteSingle(Single val)
        {
            MsgWriter.WriteSingle(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteDouble(Double val)
        {
            MsgWriter.WriteDouble(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteColorR8G8B8(Color val)
        {
            MsgWriter.WriteColorR8G8B8(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteColorR8G8B8A8(Color val)
        {
            MsgWriter.WriteColorR8G8B8A8(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteVariableUInt32(UInt32 val)
        {
            MsgWriter.WriteVariableUInt32(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteString(String val)
        {
            MsgWriter.WriteString(ref buf, ref seekPos, ref lengthBits, val);
        }

        public void WriteIdentifier(Identifier val)
        {
            WriteString(val.Value);
        }

        public void WriteRangedInteger(int val, int min, int max)
        {
            MsgWriter.WriteRangedInteger(ref buf, ref seekPos, ref lengthBits, val, min, max);
        }

        public void WriteRangedSingle(Single val, Single min, Single max, int bitCount)
        {
            MsgWriter.WriteRangedSingle(ref buf, ref seekPos, ref lengthBits, val, min, max, bitCount);
        }

        public void WriteBytes(byte[] val, int startPos, int length)
        {
            MsgWriter.WriteBytes(ref buf, ref seekPos, ref lengthBits, val, startPos, length);
        }

        public bool ReadBoolean()
        {
            return MsgReader.ReadBoolean(buf, ref seekPos);
        }

        public void ReadPadBits() { MsgReader.ReadPadBits(ref seekPos); }

        public byte ReadByte()
        {
            return MsgReader.ReadByte(buf, ref seekPos);
        }

        public byte PeekByte()
        {
            return MsgReader.PeekByte(buf, ref seekPos);
        }

        public UInt16 ReadUInt16()
        {
            return MsgReader.ReadUInt16(buf, ref seekPos);
        }

        public Int16 ReadInt16()
        {
            return MsgReader.ReadInt16(buf, ref seekPos);
        }

        public UInt32 ReadUInt32()
        {
            return MsgReader.ReadUInt32(buf, ref seekPos);
        }

        public Int32 ReadInt32()
        {
            return MsgReader.ReadInt32(buf, ref seekPos);
        }

        public UInt64 ReadUInt64()
        {
            return MsgReader.ReadUInt64(buf, ref seekPos);
        }

        public Int64 ReadInt64()
        {
            return MsgReader.ReadInt64(buf, ref seekPos);
        }

        public Single ReadSingle()
        {
            return MsgReader.ReadSingle(buf, ref seekPos);
        }

        public Double ReadDouble()
        {
            return MsgReader.ReadDouble(buf, ref seekPos);
        }

        public UInt32 ReadVariableUInt32()
        {
            return MsgReader.ReadVariableUInt32(buf, ref seekPos);
        }

        public String ReadString()
        {
            return MsgReader.ReadString(buf, ref seekPos);
        }

        public Identifier ReadIdentifier()
        {
            return ReadString().ToIdentifier();
        }

        public Color ReadColorR8G8B8()
        {
            return MsgReader.ReadColorR8G8B8(buf, ref seekPos);
        }

        public Color ReadColorR8G8B8A8()
        {
            return MsgReader.ReadColorR8G8B8A8(buf, ref seekPos);
        }

        public int ReadRangedInteger(int min, int max)
        {
            return MsgReader.ReadRangedInteger(buf, ref seekPos, min, max);
        }

        public Single ReadRangedSingle(Single min, Single max, int bitCount)
        {
            return MsgReader.ReadRangedSingle(buf, ref seekPos, min, max, bitCount);
        }

        public byte[] ReadBytes(int numberOfBytes)
        {
            return MsgReader.ReadBytes(buf, ref seekPos, numberOfBytes);
        }

        public byte[] PrepareForSending(bool compressPastThreshold, out bool isCompressed, out int outLength)
        {
            throw new InvalidOperationException("ReadWriteMessages are not to be sent");
        }

    }
}