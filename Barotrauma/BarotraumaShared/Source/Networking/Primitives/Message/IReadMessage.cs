using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public interface IReadMessage
    {
        bool ReadBoolean();
        void ReadPadBits();
        byte ReadByte();
        UInt16 ReadUInt16();
        Int16 ReadInt16();
        UInt32 ReadUInt32();
        Int32 ReadInt32();
        UInt64 ReadUInt64();
        Int64 ReadInt64();
        Single ReadSingle();
        Double ReadDouble();
        UInt64 Read7BitEncoded();
        String ReadString();
        int ReadRangedInteger(int min, int max);
    }
}
