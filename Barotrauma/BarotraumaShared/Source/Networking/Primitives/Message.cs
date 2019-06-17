using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    internal static class MsgWriter
    {
        internal static int Write(byte[] buf, int bitPos, bool val)
        {
            int bytePos = bitPos / 8;
            int bitOffset = bitPos % 8;
            byte bitFlag = (byte)(1 << bitOffset);
            byte bitMask = (byte)((~bitFlag) & 0xff);
            buf[bytePos] &= bitMask;
            if (val) buf[bytePos] |= bitFlag;
            return bitPos + 1;
        }

        internal static int WritePadBits(byte[] buf, int bitPos)
        {
            int bitOffset = bitPos % 8;
            return bitPos + ((8 - bitOffset) % 8);
        }

        internal static int Write(byte[] buf, int bitPos, byte val)
        {
            int bytePos = WritePadBits(buf, bitPos) / 8;
            buf[bytePos] = val;
            return (bytePos * 8) + 8;
        }

        internal static int Write(byte[] buf, int bitPos, UInt16 val)
        {
            int bytePos = WritePadBits(buf, bitPos) / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            return (bytePos * 8) + 16;
        }

        internal static int Write(byte[] buf, int bitPos, Int16 val)
        {
            return Write(buf, bitPos, (UInt16)val);
        }

        internal static int Write(byte[] buf, int bitPos, UInt32 val)
        {
            int bytePos = WritePadBits(buf, bitPos) / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            buf[bytePos + 2] = (byte)((val >> 16) & 0xff);
            buf[bytePos + 3] = (byte)((val >> 24) & 0xff);
            return (bytePos * 8) + 32;
        }

        internal static int Write(byte[] buf, int bitPos, Int32 val)
        {
            return Write(buf, bitPos, (UInt32)val);
        }

        internal static int Write(byte[] buf, int bitPos, UInt64 val)
        {
            int bytePos = WritePadBits(buf, bitPos) / 8;
            buf[bytePos] = (byte)(val & 0xff);
            buf[bytePos + 1] = (byte)((val >> 8) & 0xff);
            buf[bytePos + 2] = (byte)((val >> 16) & 0xff);
            buf[bytePos + 3] = (byte)((val >> 24) & 0xff);
            buf[bytePos + 4] = (byte)((val >> 32) & 0xff);
            buf[bytePos + 5] = (byte)((val >> 40) & 0xff);
            buf[bytePos + 6] = (byte)((val >> 48) & 0xff);
            buf[bytePos + 7] = (byte)((val >> 56) & 0xff);
            return (bytePos * 8) + 32;
        }

        internal static int Write(byte[] buf, int bitPos, Int64 val)
        {
            return Write(buf, bitPos, (UInt64)val);
        }
        
        internal static int Write(byte[] buf, int bitPos, Single val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            int bytePos = WritePadBits(buf, bitPos) / 8;
            for (int i = 0; i < 4; i++)
            {
                buf[bytePos + i] = bytes[i];
            }
            return (bytePos*8) + 32;
        }

        internal static int Write(byte[] buf, int bitPos, Double val)
        {
            return Write(buf, bitPos, BitConverter.DoubleToInt64Bits(val));
        }

        internal static int Write7BitEncoded(byte[] buf, int bitPos, UInt64 val)
        {
            int bytePos = WritePadBits(buf, bitPos) / 8;
            byte b = (byte)(val & 0x7f);
            if (val > 0x7f)
            {
                b |= 0x80;
            }
            buf[bytePos] = b;
            if (val > 0x7f)
            {
                return Write7BitEncoded(buf, (bytePos * 8) + 8, val >> 7);
            }
            return (bytePos * 8) + 8;
        }

        internal static int Write(byte[] buf, int bitPos, String val)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(val);
            bitPos = Write7BitEncoded(buf, bitPos, (UInt64)bytes.Length); 
            for (int i=0;i<val.Length;i++)
            {
                bitPos = Write7BitEncoded(buf, bitPos, bytes[i]);
            }
            return bitPos;
        }

        internal static int WriteRangedInteger(byte[] buf, int bitPos, int val, int min, int max)
        {
            int diff = max - min;
            int normalized = val - min;
            if (normalized < 0) normalized = 0;
            if (normalized > diff) normalized = diff;
            int requiredBits = 1;
            while ((1 << requiredBits) < diff) { requiredBits++; }
            for (int i=0;i<requiredBits;i++)
            {
                bitPos = Write(buf, bitPos, ((normalized >> i) & 0x1) != 0);
            }
            return bitPos;
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
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void WritePadBits()
        {
            seekPos = MsgWriter.WritePadBits(buf, seekPos);
        }

        public void Write(byte val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(UInt16 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(Int16 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(UInt32 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(Int32 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(UInt64 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(Int64 val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(Single val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(Double val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void Write(String val)
        {
            seekPos = MsgWriter.Write(buf, seekPos, val);
        }

        public void WriteRangedInteger(int val, int min, int max)
        {
            seekPos = MsgWriter.WriteRangedInteger(buf, seekPos, val, min, max);
        }
    }
}
