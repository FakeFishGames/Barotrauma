using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    internal static class MsgWriter
    {
        internal static void Write(byte[] buf, ref int bitPos, bool val)
        {
            int bytePos = bitPos / 8;
            int bitOffset = bitPos % 8;
            byte bitFlag = (byte)(1 << bitOffset);
            byte bitMask = (byte)((~bitFlag) & 0xff);
            buf[bytePos] &= bitMask;
            if (val) buf[bytePos] |= bitFlag;
            bitPos++;
        }

        internal static void WritePadBits(byte[] buf, ref int bitPos)
        {
            int bitOffset = bitPos % 8;
            bitPos += ((8 - bitOffset) % 8);
        }

        internal static void Write(byte[] buf, ref int bitPos, byte val)
        {
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            buf[bytePos] = val;
            bitPos += 8;
        }

        internal static void Write(byte[] buf, ref int bitPos, UInt16 val)
        {
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            bitPos += 16;
        }

        internal static void Write(byte[] buf, ref int bitPos, Int16 val)
        {
            Write(buf, ref bitPos, (UInt16)val);
        }

        internal static void Write(byte[] buf, ref int bitPos, UInt32 val)
        {
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            buf[bytePos + 2] = (byte)((val >> 16) & 0xff);
            buf[bytePos + 3] = (byte)((val >> 24) & 0xff);
            bitPos += 32;
        }

        internal static void Write(byte[] buf, ref int bitPos, Int32 val)
        {
            Write(buf, ref bitPos, (UInt32)val);
        }

        internal static void Write(byte[] buf, ref int bitPos, UInt64 val)
        {
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            buf[bytePos + 2] = (byte)((val >> 16) & 0xff);
            buf[bytePos + 3] = (byte)((val >> 24) & 0xff);
            buf[bytePos + 4] = (byte)((val >> 32) & 0xff);
            buf[bytePos + 5] = (byte)((val >> 40) & 0xff);
            buf[bytePos + 6] = (byte)((val >> 48) & 0xff);
            buf[bytePos + 7] = (byte)((val >> 56) & 0xff);
            bitPos += 64;
        }

        internal static void Write(byte[] buf, ref int bitPos, Int64 val)
        {
            Write(buf, ref bitPos, (UInt64)val);
        }
        
        internal static void Write(byte[] buf, ref int bitPos, Single val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            for (int i = 0; i < 4; i++)
            {
                buf[bytePos + i] = bytes[i];
            }
            bitPos += 32;
        }

        internal static void Write(byte[] buf, ref int bitPos, Double val)
        {
            Write(buf, ref bitPos, BitConverter.DoubleToInt64Bits(val));
        }

        internal static void Write7BitEncoded(byte[] buf, ref int bitPos, UInt64 val)
        {
            WritePadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            byte b = (byte)(val & 0x7f);
            if (val > 0x7f)
            {
                b |= 0x80;
            }
            buf[bytePos] = b;
            bitPos += 8;
            if (val > 0x7f)
            {
                Write7BitEncoded(buf, ref bitPos, val >> 7);
            }
        }

        internal static void Write(byte[] buf, ref int bitPos, String val)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(val);
            Write7BitEncoded(buf, ref bitPos, (UInt64)bytes.Length); 
            for (int i=0;i<val.Length;i++)
            {
                Write(buf, ref bitPos, bytes[i]);
            }
        }

        internal static void WriteRangedInteger(byte[] buf, ref int bitPos, int val, int min, int max)
        {
            int diff = max - min;
            int normalized = val - min;
            if (normalized < 0) normalized = 0;
            if (normalized > diff) normalized = diff;
            int requiredBits = 1;
            while ((1 << requiredBits) < diff) { requiredBits++; }
            for (int i=0;i<requiredBits;i++)
            {
                Write(buf, ref bitPos, ((normalized >> i) & 0x1) != 0);
            }
        }
    }

    internal static class MsgReader
    {
        internal static bool ReadBoolean(byte[] buf, ref int bitPos)
        {
            int bytePos = bitPos / 8;
            int bitOffset = bitPos % 8;
            bitPos++;
            return (buf[bytePos] & (1 << bitOffset)) != 0;
        }

        internal static void ReadPadBits(byte[] buf, ref int bitPos)
        {
            int bitOffset = bitPos % 8;
            bitPos += (8 - bitOffset) % 8;
        }

        internal static byte ReadByte(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            bitPos += 8;
            return buf[bytePos];
        }

        internal static UInt16 ReadUInt16(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            UInt16 retVal = buf[bytePos];
            retVal |= (UInt16)(((UInt32)buf[bytePos + 1]) << 8);
            bitPos += 16;
            return retVal;
        }

        internal static Int16 ReadInt16(byte[] buf, ref int bitPos)
        {
            return (Int16)ReadUInt16(buf, ref bitPos);
        }

        internal static UInt32 ReadUInt32(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            UInt32 retVal = buf[bytePos];
            retVal |= ((UInt32)buf[bytePos + 1]) << 8;
            retVal |= ((UInt32)buf[bytePos + 2]) << 16;
            retVal |= ((UInt32)buf[bytePos + 3]) << 24;
            bitPos += 32;
            return retVal;
        }

        internal static Int32 ReadInt32(byte[] buf, ref int bitPos)
        {
            return (Int32)ReadUInt32(buf, ref bitPos);
        }

        internal static UInt64 ReadUInt64(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            UInt64 retVal = buf[bytePos];
            retVal |= ((UInt64)buf[bytePos + 1]) << 8;
            retVal |= ((UInt64)buf[bytePos + 2]) << 16;
            retVal |= ((UInt64)buf[bytePos + 3]) << 24;
            retVal |= ((UInt64)buf[bytePos + 4]) << 32;
            retVal |= ((UInt64)buf[bytePos + 5]) << 40;
            retVal |= ((UInt64)buf[bytePos + 6]) << 48;
            retVal |= ((UInt64)buf[bytePos + 7]) << 56;
            bitPos += 64;
            return retVal;
        }

        internal static Int64 ReadInt64(byte[] buf, ref int bitPos)
        {
            return (Int64)ReadUInt64(buf, ref bitPos);
        }

        internal static Single ReadSingle(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            Single retVal = BitConverter.ToSingle(buf, bytePos);
            return retVal;
        }

        internal static Double ReadDouble(byte[] buf, ref int bitPos)
        {
            return BitConverter.Int64BitsToDouble(ReadInt64(buf, ref bitPos));
        }

        internal static UInt64 Read7BitEncoded(byte[] buf, ref int bitPos)
        {
            ReadPadBits(buf, ref bitPos);
            int bytePos = bitPos / 8;
            bitPos += 8;
            UInt64 retVal = (UInt64)(buf[bytePos]&0x7f);
            if ((buf[bytePos]&0x80) != 0)
            {
                retVal |= Read7BitEncoded(buf, ref bitPos)<<7;
            }
            return retVal;
        }

        internal static String ReadString(byte[] buf, ref int bitPos)
        {
            UInt64 length = Read7BitEncoded(buf, ref bitPos);
            byte[] bytes = new byte[length];
            for (UInt64 i=0;i<length;i++)
            {
                bytes[i] = ReadByte(buf, ref bitPos);
            }
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public class WriteOnlyMessage : IWriteMessage, IMessage
    {
        private byte[] buf = new byte[1200];
        private int seekPos = 0;

        public bool CanWrite { get { return true; } }
        public bool CanRead { get { return false; } }

        public void Write(bool val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void WritePadBits()
        {
            MsgWriter.WritePadBits(buf, ref seekPos);
        }

        public void Write(byte val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(UInt16 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(Int16 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(UInt32 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(Int32 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(UInt64 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(Int64 val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(Single val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(Double val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void Write(String val)
        {
            MsgWriter.Write(buf, ref seekPos, val);
        }

        public void WriteRangedInteger(int val, int min, int max)
        {
            MsgWriter.WriteRangedInteger(buf, ref seekPos, val, min, max);
        }
    }
}
