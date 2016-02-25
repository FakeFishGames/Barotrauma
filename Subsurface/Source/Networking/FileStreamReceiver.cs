using Lidgren.Network;
using System;
using System.IO;

namespace Barotrauma.Networking
{
    class FileStreamReceiver : IDisposable
    {
        public delegate void OnFinished(FileStreamReceiver fileStreamReceiver);
        private OnFinished onFinished;

        private NetClient client;
        private ulong length;
        private ulong received;
        private FileStream writeStream;
        private int timeStarted;

        private string filePath;

        private FileTransferType fileType;

        public string FileName
        {
            get;
            private set;
        }

        public ulong FileSize
        {
            get { return length; }
        }

        public ulong Received
        {
            get { return received; }
        }

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

        public float Progress
        {
            get { return length / (float)received; }

        }

        public FileStreamReceiver(NetClient client, string filePath, FileTransferType fileType, OnFinished onFinished)
        {
            client = client;

            this.filePath = filePath;
            this.fileType = fileType;

            this.onFinished = onFinished;

            Status = FileTransferStatus.NotStarted;
        }

        public void ReadMessage(NetIncomingMessage inc)
        {
            try
            {
                TryReadMessage(inc);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while receiving file ''"+FileName+"''", e);
                Status = FileTransferStatus.Error;
            }
        }

        private void TryReadMessage(NetIncomingMessage inc)
        {
            //int chunkLen = inc.LengthBytes;
            if (length == 0)
            {

                if (!Directory.Exists(filePath))
                {
                    Directory.CreateDirectory(filePath);
                }

                byte fileTypeByte = inc.ReadByte();
                if (fileTypeByte != (byte)fileType)
                {
                    Status = FileTransferStatus.Error;
                    return;
                }

                length = inc.ReadUInt64();
                FileName = inc.ReadString();
                writeStream = new FileStream(Path.Combine(filePath, FileName), FileMode.Create, FileAccess.Write, FileShare.None);
                timeStarted = Environment.TickCount;

                Status = FileTransferStatus.NotStarted;

                return;
            }

            byte[] all = inc.ReadBytes(inc.LengthBytes - inc.PositionInBytes);
            received += (ulong)all.Length;
            writeStream.Write(all, 0, all.Length);
            
            int passed = Environment.TickCount - timeStarted;
            float psec = passed / 1000.0f;

            BytesPerSecond = received / psec;

            Status = FileTransferStatus.Receiving;

            if (received >= length)
            {
                Status = FileTransferStatus.Finished;
                if (onFinished!=null) onFinished(this);
            }
        }
        
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            writeStream.Flush();
            writeStream.Close();
            writeStream.Dispose();
        }
    }
    
     
}
