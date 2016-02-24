using Lidgren.Network;
using System.IO;

namespace Barotrauma.Networking
{
    enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Error
    }

    class FileStreamSender
    {
        private FileStream inputStream;
        private int sentOffset;
        private int chunkLen;
        private byte[] tempBuffer;
        private NetConnection connection;

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

            
        public static FileStreamSender Create(NetConnection conn, string fileName)
        {
            if (!File.Exists(fileName))
            {
                DebugConsole.ThrowError("Sending a file failed. File ''"+fileName+"'' not found.");
                return null;
            }

            return new FileStreamSender(conn, fileName);
        }

        private FileStreamSender(NetConnection conn, string fileName)
        {
            connection = conn;
            inputStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            chunkLen = connection.Peer.Configuration.MaximumTransmissionUnit - 20;
            tempBuffer = new byte[chunkLen];
            sentOffset = 0;

            FileName = fileName;

            Status = FileTransferStatus.NotStarted;
        }
        
        public void Update()
        {
            if (inputStream == null) return;

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
                message = connection.Peer.CreateMessage(sendBytes + 8);
                message.Write((ulong)inputStream.Length);
                message.Write(Path.GetFileName(inputStream.Name));
                connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);

                Status = FileTransferStatus.Sending;
            }

            message = connection.Peer.CreateMessage(sendBytes + 8);
            message.Write(tempBuffer, 0, sendBytes);

            connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);
            sentOffset += sendBytes;

            //Program.Output("Sent " + m_sentOffset + "/" + m_inputStream.Length + " bytes to " + m_connection);

            if (remaining - sendBytes <= 0)
            {
                inputStream.Close();
                inputStream.Dispose();
                inputStream = null;

                Status = FileTransferStatus.Finished;
            }            
        }
    }
}
