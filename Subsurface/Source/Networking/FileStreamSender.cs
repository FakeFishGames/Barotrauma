using Lidgren.Network;
using System;
using System.IO;

namespace Barotrauma.Networking
{
    enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Error, Canceled
    }

    enum FileTransferType
    {
        Unknown, Submarine
    }

    class FileStreamSender : IDisposable
    {
        private FileStream inputStream;
        private int sentOffset;
        private int chunkLen;
        private byte[] tempBuffer;
        private NetConnection connection;

        float waitTimer;


        private FileTransferType fileType;

        public FileTransferStatus Status
        {
            get;
            private set;
        }

        public string FileName
        {
            get;
            private set;
        }


        public static FileStreamSender Create(NetConnection conn, string fileName, FileTransferType fileType)
        {
            if (!File.Exists(fileName))
            {
                DebugConsole.ThrowError("Sending a file failed. File ''"+fileName+"'' not found.");
                return null;
            }

            return new FileStreamSender(conn, fileName, fileType);
        }

        private FileStreamSender(NetConnection conn, string fileName, FileTransferType fileType)
        {
            connection = conn;
            inputStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            chunkLen = connection.Peer.Configuration.MaximumTransmissionUnit - 100;
            tempBuffer = new byte[chunkLen];
            sentOffset = 0;
            
            FileName = fileName;

            this.fileType = fileType;

            Status = FileTransferStatus.NotStarted;
        }
        
        public void Update(float deltaTime)
        {
            if (inputStream == null) return;

            waitTimer -= deltaTime;
            if (waitTimer > 0.0f) return;
            
            if (!connection.CanSendImmediately(NetDeliveryMethod.ReliableOrdered, 1)) return;
            
            // send another part of the file!
            long remaining = inputStream.Length - sentOffset;
            int sendBytes = (remaining > chunkLen ? chunkLen : (int)remaining);

            // just assume we can read the whole thing in one Read()
            inputStream.Read(tempBuffer, 0, sendBytes);

            NetOutgoingMessage message;
            if (sentOffset == 0)
            {
                // first message; send length, chunk length and file name
                message = connection.Peer.CreateMessage(sendBytes + 8 + 1);
                message.Write((byte)PacketTypes.FileStream);
                message.Write((byte)fileType);
                message.Write((ulong)inputStream.Length);
                message.Write(Path.GetFileName(inputStream.Name));
                connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);

                Status = FileTransferStatus.Sending;
            }

            message = connection.Peer.CreateMessage(sendBytes + 8 + 1);
            message.Write((byte)PacketTypes.FileStream);
            message.Write(tempBuffer, 0, sendBytes);

            connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);
            sentOffset += sendBytes;

            waitTimer = connection.AverageRoundtripTime + 0.05f;

            //Program.Output("Sent " + m_sentOffset + "/" + m_inputStream.Length + " bytes to " + m_connection);

            if (remaining - sendBytes <= 0)
            {
                //Dispose();

                Status = FileTransferStatus.Finished;
            }            
        }


        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            inputStream.Close();
            inputStream.Dispose();
            inputStream = null;
        }
    }
}
