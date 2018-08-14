using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{

    public class VoipQueue
    {
        protected const int BUFFER_COUNT = 5;
        protected int[] bufferLengths;
        protected byte[][] buffers;
        protected int newestBuffer;

        public byte[] BufferToQueue
        {
            get;
            protected set;
        }

        public byte QueueID
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

        public VoipQueue(byte id, bool canSend, bool canReceive)
        {
            BufferToQueue = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            newestBuffer = BUFFER_COUNT - 1;
            bufferLengths = new int[BUFFER_COUNT];
            buffers = new byte[BUFFER_COUNT][];
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                buffers[i] = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            }
            QueueID = id;
            CanSend = canSend;
            CanReceive = canReceive;
            LatestBufferID = BUFFER_COUNT-1;
        }

        public void EnqueueBuffer(int length)
        {
            if (length > byte.MaxValue) return;

            newestBuffer = (newestBuffer + 1) % BUFFER_COUNT;

            bufferLengths[newestBuffer] = length;
            BufferToQueue.CopyTo(buffers[newestBuffer], 0);

            LatestBufferID++;
        }

        public virtual void Write(NetBuffer msg)
        {
            if (!CanSend) throw new Exception("Called Write on a VoipQueue not set up for sending");

            msg.Write((UInt16)LatestBufferID);
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                int index = (newestBuffer + i + 1) % BUFFER_COUNT;

                msg.Write((byte)bufferLengths[index]);
                msg.Write(buffers[index], 0, bufferLengths[index]);
            }
        }

        public virtual void Read(NetBuffer msg)
        {
            if (!CanReceive) throw new Exception("Called Read on a VoipQueue not set up for receiving");

            UInt16 incLatestBufferID = msg.ReadUInt16();
            if (incLatestBufferID > LatestBufferID)
            {
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    bufferLengths[i] = msg.ReadByte();
                    msg.ReadBytes(buffers[i], 0, bufferLengths[i]);
                }
                newestBuffer = BUFFER_COUNT - 1;
            }
            else
            {
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    byte len = msg.ReadByte();
                    msg.Position += len * 8;
                }
            }
        }
    }
}
