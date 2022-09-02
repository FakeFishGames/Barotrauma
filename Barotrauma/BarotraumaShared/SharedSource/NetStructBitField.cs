#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma
{
    interface IWritableBitField
    {
        public void WriteBoolean(bool b);
        public void WriteInteger(int value, int min, int max);
        public void WriteFloat(float value, float min, float max, int numberOfBits);

        public void WriteToMessage(IWriteMessage msg);
    }

    interface IReadableBitField
    {
        public bool ReadBoolean();
        public int ReadInteger(int min, int max);
        public float ReadFloat(float min, float max, int numberOfBits);
    }

    sealed class WriteOnlyBitField : IWritableBitField, IDisposable
    {
        private const int AmountOfBoolsInByte = 7; // Reserve last bit for end marker
        private readonly List<byte> Buffer = new List<byte>();
        private int index;
        private bool disposed;

        public void WriteBoolean(bool b)
        {
            ThrowIfDisposed();

            int arrayIndex = (int)Math.Floor(index / (float)AmountOfBoolsInByte);
            if (arrayIndex >= Buffer.Count) { Buffer.Add(0); }

            int bitIndex = index % AmountOfBoolsInByte;
            Buffer[arrayIndex] |= (byte)(b ? 1u << bitIndex : 0);
            index++;
        }

        public void WriteInteger(int value, int min, int max)
        {
            ThrowIfDisposed();

            uint range = (uint)(max - min);
            int numberOfBits = NetUtility.BitsToHoldUInt(range);

            uint writeValue = (uint)(value - min);

            for (int i = 0; i < numberOfBits; i++)
            {
                WriteBoolean((writeValue & (1u << i)) != 0);
            }
        }

        public void WriteFloat(float value, float min, float max, int numberOfBits)
        {
            ThrowIfDisposed();

            float range = max - min;
            float unit = (value - min) / range;
            uint maxVal = (1u << numberOfBits) - 1;

            uint writeValue = (uint)(maxVal * unit);
            for (int i = 0; i < numberOfBits; i++)
            {
                WriteBoolean((writeValue & (1u << i)) != 0);
            }
        }

        public void WriteToMessage(IWriteMessage msg)
        {
            ThrowIfDisposed();

            if (Buffer.Count == 0) { Buffer.Add(0); }

            Buffer[^1] |= 1 << AmountOfBoolsInByte; // mark the last byte so we know when to stop reading

            foreach (byte b in Buffer)
            {
                msg.WriteByte(b);
            }

            Dispose();
        }

        public void Dispose()
        {
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) { throw new ObjectDisposedException(nameof(WriteOnlyBitField)); }
        }
    }

    sealed class ReadOnlyBitField : IReadableBitField
    {
        private const int AmountOfBoolsInByte = 7; // Reserve last bit for end marker
        private readonly ImmutableArray<byte> buffer;
        private int index;

        public ReadOnlyBitField(IReadMessage inc)
        {
            List<byte> bytes = new List<byte>();
            byte currentByte;
            int reads = 0;
            do
            {
                currentByte = inc.ReadByte();
                bytes.Add(currentByte);

                reads++;
                if (reads > 100)
                {
                    throw new Exception($"Failed to find the end of the bit field after 100 reads. Terminating to prevent the game from freezing.");
                }
            }
            while (!IsBitSet(currentByte, AmountOfBoolsInByte));

            buffer = bytes.ToImmutableArray();
        }

        public bool ReadBoolean()
        {
            int arrayIndex = (int)MathF.Floor(index / (float)AmountOfBoolsInByte);
            int bitIndex = index % AmountOfBoolsInByte;
            index++;
            return IsBitSet(buffer[arrayIndex], bitIndex);
        }

        public int ReadInteger(int min, int max)
        {
            uint range = (uint)(max - min);
            int numberOfBits = NetUtility.BitsToHoldUInt(range);

            uint value = 0;
            for (int i = 0; i < numberOfBits; i++)
            {
                value |= ReadBoolean() ? 1u << i : 0u;
            }

            return (int)(min + value);
        }

        public float ReadFloat(float min, float max, int numberOfBits)
        {
            int maxInt = (1 << numberOfBits) - 1;

            uint value = 0;
            for (int i = 0; i < numberOfBits; i++)
            {
                value |= ReadBoolean() ? 1u << i : 0u;
            }

            float range = max - min;
            return min + range * value / maxInt;
        }

        private static bool IsBitSet(byte b, int bitIndex) => (b & (1u << bitIndex)) != 0;
    }
}