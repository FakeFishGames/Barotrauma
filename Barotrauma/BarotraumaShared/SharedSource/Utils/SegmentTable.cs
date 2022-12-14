#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking;

/*
 * What are segment tables for?
 *
 * Segment tables help make our networking packet reading code more robust by
 * clearly stating where part of a message begins. Previously we would've done
 * something like:
 *
 *      msg.WriteByte(SegmentType.A);
 *      ...
 *      msg.WriteByte(SegmentType.B);
 *      ...
 *      msg.WriteByte(SegmentType.EndOfMessage);
 *
 * The problem with this design is that it's hard to debug when the writing and reading
 * code do not align for whatever reason. INetSerializableStruct is an awesome way
 * of avoiding that problem, but deploying it on a broad scale means rewriting most
 * of the netcode. That isn't going to happen any time soon, so this exists as an easier
 * way of increasing robustness.
 *
 * A segment table is laid out as follows:
 *
 * [TablePointer: UInt16]
 * [Segment: arbitrary]
 * ...
 * [Segment: arbitrary]
 * [NumberOfSegments: UInt16]
 * [(Identifier, SegmentPointer): (T, UInt16)]
 * ...
 * [(Identifier, SegmentPointer): (T, UInt16)]
 *
 * A pointer in this context is an offset relative to the BitPosition where the TablePointer is written.
 *
 * It is used as follows:
 * 
 *      using (var segmentTable = SegmentTableWriter<T>.StartWriting(outMsg))
 *      {
 *          segmentTable.StartNewSegment(T.A);
 *          ... write segment to outMsg ...
 *          segmentTable.StartNewSegment(T.B);
 *          ... write segment to outMsg ...
 *      }
 *      peer.SendMessage(outMsg);
 *
 *      ...
 *
 *      SegmentTableReader<T>.Read(inc,
 *          segmentDataReader: (segment, inc) =>
 *          {
 *              switch (segment)
 *              {
 *                 ... read segments ...
 *              }
 *          }
 *      }
 * 
 * The advantages of this approach are:
 * - If a message is truncated or corrupted near the end, it becomes far more obvious because the table
 *   would not be read properly and look like garbage when printed to the console.
 * - If the reading and writing code for a segment disagree on something, issues will be isolated to that
 *   one segment.
 * - The code no longer has to fiddle with padding and temporary buffers because the segment table is able
 *   to handle content that is not byte-aligned just fine.
 * - Exception handling is far easier when using a segment table, when combined with a using statement
 *   any uncaught exception will result in the entire table being skipped, allowing the remainder of the
 *   message to still be read.
 * - It's harder to make mistakes in the implementation of segments themselves with this approach. By using
 *   the SegmentTableWriter and SegmentTableReader types, you get a type-safe way of delimiting segments
 *   and it's harder to forget to finalize a packet.
 */

[NetworkSerialize]
public readonly record struct Segment<T>(T Identifier, UInt16 Pointer) : INetSerializableStruct where T : struct;

readonly ref struct SegmentTableWriter<T> where T : struct
{
    private readonly IWriteMessage message;
    private readonly List<Segment<T>> segments;
    public readonly int PointerLocation;
    private SegmentTableWriter(IWriteMessage message, int pointerLocation)
    {
        this.message = message;
        this.PointerLocation = pointerLocation;
        this.segments = new List<Segment<T>>();
    }

    public static SegmentTableWriter<T> StartWriting(IWriteMessage msg)
    {
        var retVal = new SegmentTableWriter<T>(msg, msg.BitPosition);
        msg.WriteUInt16(0); //reserve space for the table pointer
        return retVal;
    }

    private void ThrowOnInvalidState()
    {
        if (segments.Count >= UInt16.MaxValue)
        {
            throw new InvalidOperationException($"Too many segments in SegmentTable<{typeof(T).Name}>");
        }

        if (message.BitPosition - PointerLocation > UInt16.MaxValue)
        {
            throw new OverflowException(
                $"Too much data is being stored in SegmentTable<{typeof(T).Name}> ({segments.Count} segments)");
        }
    }
    
    public void StartNewSegment(T value)
    {
        ThrowOnInvalidState();
        segments.Add(new Segment<T>(value, (UInt16)(message.BitPosition-PointerLocation)));
    }

    public void Dispose()
    {
        ThrowOnInvalidState();
        int tablePosition = message.BitPosition;
        
        //rewrite the table pointer now that we know where the table ends
        message.BitPosition = PointerLocation;
        message.WriteUInt16((UInt16)(tablePosition-PointerLocation));

        //write the table
        message.BitPosition = tablePosition;
        message.WriteUInt16((UInt16)segments.Count);
        foreach (var segment in segments)
        {
            message.WriteNetSerializableStruct(segment);
        }
    }
}

readonly ref struct SegmentTableReader<T> where T : struct
{
    private class SegmentReadMsg : IReadMessage
    {
        private readonly IReadMessage underlyingMsg;
        private readonly IReadOnlyList<Segment<T>> segments;
        private readonly int segmentIndex;
        private readonly int offset;
        private readonly int lengthBits;
        public SegmentReadMsg(IReadMessage underlyingMsg, IReadOnlyList<Segment<T>> segments, int segmentIndex, int offset, int lengthBits)
        {
            this.underlyingMsg = underlyingMsg;
            this.segments = segments;
            this.segmentIndex = segmentIndex;
            this.offset = offset;
            this.lengthBits = lengthBits;

            if (offset + lengthBits >= underlyingMsg.LengthBits)
            {
                throw new Exception(
                    $"Segment table is corrupt, segment length is invalid: {offset} + {lengthBits} >= {underlyingMsg.LengthBits}");
            }
        }

        private void Check()
        {
            if (BitPosition > lengthBits)
            {
                throw new Exception($"Tried to read too much data from segment.");
            }
        }
        
        private TRead Check<TRead>(TRead v)
        {
            Check();
            return v;
        }

        public bool ReadBoolean() => Check(underlyingMsg.ReadBoolean());

        public void ReadPadBits()
        {
            Check(); underlyingMsg.ReadPadBits();
        }

        public byte ReadByte() => Check(underlyingMsg.ReadByte());

        public byte PeekByte() => Check(underlyingMsg.PeekByte());

        public ushort ReadUInt16() => Check(underlyingMsg.ReadUInt16());

        public short ReadInt16() => Check(underlyingMsg.ReadInt16());

        public uint ReadUInt32() => Check(underlyingMsg.ReadUInt32());

        public int ReadInt32() => Check(underlyingMsg.ReadInt32());

        public ulong ReadUInt64() => Check(underlyingMsg.ReadUInt64());

        public long ReadInt64() => Check(underlyingMsg.ReadInt64());

        public float ReadSingle() => Check(underlyingMsg.ReadSingle());

        public double ReadDouble() => Check(underlyingMsg.ReadDouble());

        public uint ReadVariableUInt32() => Check(underlyingMsg.ReadVariableUInt32());

        public string ReadString() => Check(underlyingMsg.ReadString());

        public Identifier ReadIdentifier() => Check(underlyingMsg.ReadIdentifier());

        public Color ReadColorR8G8B8() => Check(underlyingMsg.ReadColorR8G8B8());

        public Color ReadColorR8G8B8A8() => Check(underlyingMsg.ReadColorR8G8B8A8());

        public int ReadRangedInteger(int min, int max) => Check(underlyingMsg.ReadRangedInteger(min, max));

        public float ReadRangedSingle(float min, float max, int bitCount) => Check(underlyingMsg.ReadRangedSingle(min, max, bitCount));

        public byte[] ReadBytes(int numberOfBytes) => Check(underlyingMsg.ReadBytes(numberOfBytes));

        public int BitPosition
        {
            get => underlyingMsg.BitPosition - offset;
            set => Check(underlyingMsg.BitPosition = value + offset);
        }

        public int BytePosition => BitPosition / 8;

        public byte[] Buffer => underlyingMsg.Buffer;

        public int LengthBits
        {
            get => lengthBits;
            set => throw new InvalidOperationException($"Cannot resize {nameof(SegmentReadMsg)}");
        }

        public int LengthBytes => lengthBits / 8;

        public NetworkConnection Sender => underlyingMsg.Sender;
    }
    
    private readonly IReadMessage message;
    private readonly List<Segment<T>> segments;
    private readonly int exitLocation;
    public readonly int PointerLocation;
    private SegmentTableReader(IReadMessage message, List<Segment<T>> segments, int pointerLocation, int exitLocation)
    {
        this.message = message;
        this.segments = segments;
        this.PointerLocation = pointerLocation;
        this.exitLocation = exitLocation;
    }
    
    public IReadOnlyList<Segment<T>> Segments => segments;

    public enum BreakSegmentReading
    {
        No,
        Yes
    }

    public delegate BreakSegmentReading SegmentDataReader(
        T segmentHeader,
        IReadMessage incMsg);
    
    public delegate void ExceptionHandler(
        Segment<T> segmentWithError,
        Segment<T>[] previousSegments,
        Exception exceptionThrown);
    
    public static void Read(
        IReadMessage msg,
        SegmentDataReader segmentDataReader,
        ExceptionHandler? exceptionHandler = null)
    {
        int pointerLocation = msg.BitPosition;
        int tablePointer = msg.ReadUInt16();
        int tableLocation = pointerLocation + tablePointer;

        int returnPosition = msg.BitPosition;

        //read the table
        var segments = new List<Segment<T>>();
        msg.BitPosition = tableLocation;
        int numSegments = msg.ReadUInt16();
        for (int i = 0; i < numSegments; i++)
        {
            segments.Add(INetSerializableStruct.Read<Segment<T>>(msg));
        }

        //store the exit location and go back to the top
        int exitLocation = msg.BitPosition;
        msg.BitPosition = returnPosition;
        using var segmentTable = new SegmentTableReader<T>(msg, segments, pointerLocation, exitLocation);

        for (int i = 0; i < segmentTable.Segments.Count; i++)
        {
            var segment = segmentTable.Segments[i];
            msg.BitPosition = segmentTable.PointerLocation + segment.Pointer;
            try
            {
                if (segmentDataReader(segment.Identifier, new SegmentReadMsg(
                        msg,
                        segments,
                        i,
                        offset: segmentTable.PointerLocation + segment.Pointer,
                        lengthBits: (i < segmentTable.Segments.Count - 1 ? segments[i + 1].Pointer : tablePointer) -
                                    segment.Pointer))
                    is BreakSegmentReading.Yes)
                {
                    break;
                }
            }
            catch (Exception e)
            {
                var prevSegments = segments.Take(i).ToArray();
                if (exceptionHandler is not null)
                {
                    exceptionHandler(segment, prevSegments, e);
                }
                else
                {
                    throw new Exception(
                        $"Exception thrown while reading segment {segment.Identifier} at position {segment.Pointer}." +
                        (prevSegments.Any() ? $" Previous segments: {string.Join(", ", prevSegments)}." : ""),
                        e);
                }
            }
        }
    }

    public void Dispose()
    {
        message.BitPosition = exitLocation;
    }
}
