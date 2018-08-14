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

        public byte ID
        {
            get;
            protected set;
        }

        public VoipQueue(byte id)
        {
            BufferToQueue = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            newestBuffer = BUFFER_COUNT - 1;
            bufferLengths = new int[BUFFER_COUNT];
            buffers = new byte[BUFFER_COUNT][];
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                buffers[i] = new byte[VoipConfig.MAX_COMPRESSED_SIZE];
            }
            ID = id;
        }

        public void EnqueueBuffer(int length)
        {
            if (length > byte.MaxValue) return;

            newestBuffer = (newestBuffer + 1) % BUFFER_COUNT;

            bufferLengths[newestBuffer] = length;
            BufferToQueue.CopyTo(buffers[newestBuffer], 0);
        }

        public virtual void Write(NetBuffer msg)
        {
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                int index = (newestBuffer + i + 1) % BUFFER_COUNT;

                msg.Write((byte)bufferLengths[index]);
                msg.Write(buffers[index], 0, bufferLengths[index]);
            }
        }

        public virtual void Read(NetBuffer msg)
        {
            for (int i=0;i<BUFFER_COUNT;i++)
            {
                bufferLengths[i] = msg.ReadByte();
                msg.ReadBytes(buffers[i], 0, bufferLengths[i]);
            }
            newestBuffer = BUFFER_COUNT - 1;
        }
    }
}
