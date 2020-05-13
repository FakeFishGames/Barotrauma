using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{

    public class VoipQueue : IDisposable
    {
        public const int BUFFER_COUNT = 8;
        protected int[] bufferLengths;
        protected byte[][] buffers;
        protected int newestBufferInd;
        protected bool firstRead;

        public int EnqueuedTotalLength
        {
            get
            {
                int enqueuedTotalLength = 0;
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    enqueuedTotalLength += bufferLengths[i];
                }
                return enqueuedTotalLength;
            }
        }

        public byte[] BufferToQueue
        {
            get;
            protected set;
        }

        public virtual byte QueueID
        {
            get;
            protected set;
        }

        public UInt16 LatestBufferID
        {
            get;
            protected set;
        }

        public bool CanSend
        {
            get;
            protected set;
        }

        public bool CanReceive
        {
            get;
            protected set;
        }

        public bool ForceLocal
        {
            get;
            set;
        }

        public DateTime LastReadTime
        {
            get;
            private set;
        }

        public VoipQueue(byte id, bool canSend, bool canReceive)
        {
            BufferToQueue = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            newestBufferInd = BUFFER_COUNT - 1;
            bufferLengths = new int[BUFFER_COUNT];
            buffers = new byte[BUFFER_COUNT][];
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                buffers[i] = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            }
            QueueID = id;
            CanSend = canSend;
            CanReceive = canReceive;
            LatestBufferID = BUFFER_COUNT - 1;
            firstRead = true;

            LastReadTime = DateTime.Now;
        }

        public void EnqueueBuffer(int length)
        {
            if (length > byte.MaxValue) { return; }

            newestBufferInd = (newestBufferInd + 1) % BUFFER_COUNT;

            int enqueuedTotalLength = EnqueuedTotalLength;

            bufferLengths[newestBufferInd] = length;
            BufferToQueue.CopyTo(buffers[newestBufferInd], 0);

            if ((enqueuedTotalLength + length) > 0) { LatestBufferID++; }
        }

        public void RetrieveBuffer(int id, out int outSize, out byte[] outBuf)
        {
            lock (buffers)
            {
                if (id >= LatestBufferID - (BUFFER_COUNT - 1) && id <= LatestBufferID)
                {
                    int index = newestBufferInd - (LatestBufferID - id); 
                    if (index < 0) { index += BUFFER_COUNT; }
                    outSize = bufferLengths[index];
                    outBuf = buffers[index];
                    return;
                }
            }
            outSize = -1;
            outBuf = null;
        }

        public virtual void Write(IWriteMessage msg)
        {
            if (!CanSend) { throw new Exception("Called Write on a VoipQueue not set up for sending"); }

            msg.Write((UInt16)LatestBufferID);
            msg.Write(ForceLocal); msg.WritePadBits();
            lock (buffers)
            {
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    int index = (newestBufferInd + i + 1) % BUFFER_COUNT;

                    msg.Write((byte)bufferLengths[index]);
                    msg.Write(buffers[index], 0, bufferLengths[index]);
                }
            }
        }

        public virtual bool Read(IReadMessage msg, bool discardData = false)
        {
            if (!CanReceive) { throw new Exception("Called Read on a VoipQueue not set up for receiving"); }

            UInt16 incLatestBufferID = msg.ReadUInt16();
            if ((firstRead || NetIdUtils.IdMoreRecent(incLatestBufferID, LatestBufferID)) && !discardData)
            {
                ForceLocal = msg.ReadBoolean(); msg.ReadPadBits();

                firstRead = false;
                lock (buffers)
                {
                    for (int i = 0; i < BUFFER_COUNT; i++)
                    {
                        bufferLengths[i] = msg.ReadByte();
                        buffers[i] = msg.ReadBytes(bufferLengths[i]);
                    }
                }
                newestBufferInd = BUFFER_COUNT - 1;
                LatestBufferID = incLatestBufferID;
                LastReadTime = DateTime.Now;
                return true;
            }
            else
            {
                msg.ReadBoolean(); msg.ReadPadBits();
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    byte len = msg.ReadByte();
                    msg.BitPosition += len * 8;
                }
                return false;
            }
        }

        public virtual void Dispose() { }
    }
}
