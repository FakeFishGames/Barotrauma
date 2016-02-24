using Lidgren.Network;
using System;
using System.IO;

namespace Barotrauma.Networking
{
    class FileStreamReceiver
    {
        private NetClient s_client;
        private ulong s_length;
        private ulong s_received;
        private FileStream s_writeStream;
        private int s_timeStarted;

        public FileTransferStatus Status
        {
            get;
            private set;
        }

        public float BytesPerSecond
        {
            get;
            private set;
        }

        public FileStreamReceiver(NetClient client)
        {
            s_client = client;

            Status = FileTransferStatus.NotStarted;
        }

        public void ReadMessage(NetIncomingMessage inc)
        {
            int chunkLen = inc.LengthBytes;
            if (s_length == 0)
            {
                s_length = inc.ReadUInt64();
                string filename = inc.ReadString();
                s_writeStream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None);
                s_timeStarted = Environment.TickCount;

                Status = FileTransferStatus.NotStarted;

                return;
            }

            byte[] all = inc.ReadBytes(inc.LengthBytes);
            s_received += (ulong)all.Length;
            s_writeStream.Write(all, 0, all.Length);
            
            int passed = Environment.TickCount - s_timeStarted;
            float psec = passed / 1000.0f;

            BytesPerSecond = s_received / psec;

            Status = FileTransferStatus.Receiving;

            if (s_received >= s_length)
            {
                s_writeStream.Flush();
                s_writeStream.Close();
                s_writeStream.Dispose();

                Status = FileTransferStatus.Finished;                
            }
        }
    }
}
