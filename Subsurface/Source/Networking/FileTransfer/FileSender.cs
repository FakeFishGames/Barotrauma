using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Canceled
    }

    enum FileTransferMessageType
    {
        Unknown, Initiate, Data, Cancel
    }

    enum FileTransferType
    {
        Submarine
    }

    class FileSender
    {
        public class FileTransferOut
        {
            private byte[] tempBuffer;
            private byte[] data;

            private DateTime startingTime;

            private NetConnection connection;

            public FileTransferStatus Status;
            
            public string FileName
            {
                get;
                private set;
            }

            public string FilePath
            {
                get;
                private set;
            }

            public FileTransferType FileType
            {
                get;
                private set;
            }

            public float Progress
            {
                get { return 0.0f; }//inputStream == null ? 0.0f : (float)sentOffset / (float)inputStream.Length; }
            }

            public float WaitTimer
            {
                get;
                set;
            }

            public byte[] Data
            {
                get { return data; }
            }

            public int SentOffset
            {
                get;
                set;
            }

            public NetConnection Connection
            {
                get { return connection; }
            }

            public FileTransferOut(NetConnection recipient, FileTransferType fileType, string filePath)
            {
                connection = recipient;

                FileType = fileType;
                FilePath = filePath;
                FileName = Path.GetFileName(filePath);

                Status = FileTransferStatus.NotStarted;
                
                startingTime = DateTime.Now;

                data = File.ReadAllBytes(filePath);
            }
        }

        public static TimeSpan MaxTransferDuration = new TimeSpan(0, 2, 0);

        private List<FileTransferOut> activeTransfers;

        private int chunkLen;

        private NetPeer peer;

        public FileSender(NetworkMember networkMember)
        {
            peer = networkMember.netPeer;
            chunkLen = peer.Configuration.MaximumTransmissionUnit - 100;

            activeTransfers = new List<FileTransferOut>();
        }

        public FileTransferOut StartTransfer(NetConnection recipient, FileTransferType fileType, string filePath)
        {
            if (!File.Exists(filePath))
            {
                DebugConsole.ThrowError("Failed to initiate file transfer (file \""+filePath+"\" not found.");
                return null;
            }

            FileTransferOut transfer = null;
            try
            {
                transfer = new FileTransferOut(recipient, fileType, filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to initiate file transfer", e);
            }

            return transfer;
        }

        public void Update(float deltaTime)
        {
            foreach (FileTransferOut transfer in activeTransfers)
            {
                transfer.WaitTimer -= deltaTime;
                if (transfer.WaitTimer > 0.0f) return;
                
                if (!transfer.Connection.CanSendImmediately(NetDeliveryMethod.ReliableOrdered, 1)) continue;
                
                transfer.WaitTimer = transfer.Connection.AverageRoundtripTime;

                // send another part of the file
                long remaining = transfer.Data.Length - transfer.SentOffset;
                int sendByteCount = (remaining > chunkLen ? chunkLen : (int)remaining);
                
                NetOutgoingMessage message;

                //first message; send length, chunk length, file name etc
                if (transfer.SentOffset == 0)
                {
                    message = peer.CreateMessage(sendByteCount + 8 + 1);
                    message.Write((byte)ServerPacketHeader.FILE_TRANSFER);
                    message.Write((byte)FileTransferMessageType.Initiate);
                    message.Write((byte)transfer.FileType);
                    message.Write((ushort)chunkLen);
                    message.Write((ulong)transfer.Data.Length);
                    message.Write(transfer.FileName);
                    transfer.Connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);

                    transfer.Status = FileTransferStatus.Sending;
                }

                message = peer.CreateMessage(sendByteCount + 8 + 1);
                message.Write((byte)ServerPacketHeader.FILE_TRANSFER);
                message.Write((byte)FileTransferMessageType.Data);

                byte[] sendBytes = new byte[sendByteCount];
                Array.Copy(transfer.Data, transfer.SentOffset, sendBytes, 0, sendByteCount);

                message.Write(sendBytes);

                transfer.Connection.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 1);
                transfer.SentOffset += sendByteCount;
                
                if (remaining - sendByteCount <= 0)
                {
                    transfer.Status = FileTransferStatus.Finished;
                }
            }

            activeTransfers.RemoveAll(t => t.Status == FileTransferStatus.Finished);
        }

        public void CancelTransfer(FileTransferOut transfer)
        {
            transfer.Status = FileTransferStatus.Canceled;
            activeTransfers.Remove(transfer);
        }

    }
}
