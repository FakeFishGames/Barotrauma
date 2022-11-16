using System;

namespace Barotrauma.Networking
{
    interface IWriteMessage
    {
        void WriteBoolean(bool val);
        void WritePadBits();
        void WriteByte(byte val);
        void WriteInt16(Int16 val);
        void WriteUInt16(UInt16 val);
        void WriteInt32(Int32 val);
        void WriteUInt32(UInt32 val);
        void WriteInt64(Int64 val);
        void WriteUInt64(UInt64 val);
        void WriteSingle(Single val);
        void WriteDouble(Double val);
        void WriteColorR8G8B8(Microsoft.Xna.Framework.Color val);
        void WriteColorR8G8B8A8(Microsoft.Xna.Framework.Color val);
        void WriteVariableUInt32(UInt32 val);
        void WriteString(string val);
        void WriteIdentifier(Identifier val);
        void WriteRangedInteger(int val, int min, int max);
        void WriteRangedSingle(Single val, Single min, Single max, int bitCount);
        void WriteBytes(byte[] val, int startIndex, int length);

        byte[] PrepareForSending(bool compressPastThreshold, out bool isCompressed, out int outLength);

        int BitPosition { get; set; }
        int BytePosition { get; }
        byte[] Buffer { get; }
        int LengthBits { get; set; }
        int LengthBytes { get; }
    }
}
