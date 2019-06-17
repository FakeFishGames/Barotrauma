using System;

namespace Barotrauma.Networking
{
    public interface IWriteMessage
    {
        void Write(bool val);
        void WritePadBits();
        void Write(Byte val);
        void Write(Int16 val);
        void Write(UInt16 val);
        void Write(Int32 val);
        void Write(UInt32 val);
        void Write(Int64 val);
        void Write(UInt64 val);
        void Write(Single val);
        void Write(Double val);
        void Write(string val);
        void WriteRangedInteger(int val, int min, int max);
    }
}
