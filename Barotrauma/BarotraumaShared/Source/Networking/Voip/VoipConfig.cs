using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public const int MAX_COMPRESSED_SIZE = 120; //amount of bytes we expect each 60ms of audio to fit in

        public const int SEND_INTERVAL_MS = 200;

        public class VoipQueue
        {
            private const int BUFFER_COUNT = 5;
            private int[] bufferLengths;
            private byte[][] buffers;
            private int newestBuffer;

            public byte[] BufferToQueue
            {
                get;
                private set;
            }

            public VoipQueue()
            {
                BufferToQueue = new byte[MAX_COMPRESSED_SIZE];
                newestBuffer = BUFFER_COUNT - 1;
                bufferLengths = new int[BUFFER_COUNT];
                buffers = new byte[BUFFER_COUNT][];
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    buffers[i] = new byte[MAX_COMPRESSED_SIZE];
                }
            }

            public void EnqueueBuffer(int length)
            {
                if (length > byte.MaxValue) return;

                newestBuffer = (newestBuffer + 1) % BUFFER_COUNT;

                bufferLengths[newestBuffer] = length;
                BufferToQueue.CopyTo(buffers[newestBuffer], 0);
            }

            public void Write(NetBuffer msg)
            {
                for (int i=0;i<BUFFER_COUNT;i++)
                {
                    int index = (newestBuffer+i+1) % BUFFER_COUNT;

                    msg.Write((byte)bufferLengths[index]);
                    msg.Write(buffers[index], 0, bufferLengths[index]);
                }
            }
        }
    }
}
