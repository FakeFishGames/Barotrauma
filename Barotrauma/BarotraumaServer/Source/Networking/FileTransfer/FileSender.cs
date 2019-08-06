using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    class FileSender
    {
        public class FileTransferOut
        {
            private readonly byte[] data;

            private readonly DateTime startingTime;

            private readonly NetworkConnection connection;

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
                get { return KnownReceivedOffset / (float)Data.Length; }
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

            public bool Acknowledged;

            public int SentOffset
            {
                get;
                set;
            }

            public int KnownReceivedOffset;

            public NetworkConnection Connection
            {
                get { return connection; }
            }

            public int ID;

            public FileTransferOut(NetworkConnection recipient, FileTransferType fileType, string filePath)
            {
                connection = recipient;

                FileType = fileType;
                FilePath = filePath;
                FileName = Path.GetFileName(filePath);

                Acknowledged = false;
                SentOffset = 0;
                KnownReceivedOffset = 0;

                Status = FileTransferStatus.NotStarted;
                
                startingTime = DateTime.Now;
                
                int maxRetries = 4;
                for (int i = 0; i <= maxRetries; i++)
                {
                    try
                    {
                        data = File.ReadAllBytes(filePath);
                    }
                    catch (IOException e)
                    {
                        if (i >= maxRetries) { throw; }
                        DebugConsole.NewMessage("Failed to initiate a file transfer {" + e.Message + "}, retrying in 250 ms...", Color.Red);
                        Thread.Sleep(250);
                    }
                }
            }
        }

        const int MaxTransferCount = 16;
        const int MaxTransferCountPerRecipient = 5;

        public static TimeSpan MaxTransferDuration = new TimeSpan(0, 2, 0);
        
        public delegate void FileTransferDelegate(FileTransferOut fileStreamReceiver);
        public FileTransferDelegate OnStarted;
        public FileTransferDelegate OnEnded;

        private readonly List<FileTransferOut> activeTransfers;

        private readonly int chunkLen;

        private readonly ServerPeer peer;

        public List<FileTransferOut> ActiveTransfers
        {
            get { return activeTransfers; }
        }

        public FileSender(ServerPeer serverPeer, int mtu)
        {
            peer = serverPeer;
            chunkLen = mtu - 100;

            activeTransfers = new List<FileTransferOut>();
        }

        public FileTransferOut StartTransfer(NetworkConnection recipient, FileTransferType fileType, string filePath)
        {
            if (activeTransfers.Count >= MaxTransferCount)
            {
                return null;
            }

            if (activeTransfers.Count(t => t.Connection == recipient) > MaxTransferCountPerRecipient)
            {
                return null;
            }

            if (!File.Exists(filePath))
            {
                DebugConsole.ThrowError("Failed to initiate file transfer (file \"" + filePath + "\" not found.");
                return null;
            }

            FileTransferOut transfer = null;
            try
            {
                transfer = new FileTransferOut(recipient, fileType, filePath)
                {
                    ID = 1
                };
                while (activeTransfers.Any(t => t.Connection == recipient && t.ID == transfer.ID))
                {
                    transfer.ID++;
                }
                activeTransfers.Add(transfer);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to initiate file transfer", e);
                return null;
            }

            OnStarted(transfer);

            return transfer;
        }

        public void Update(float deltaTime)
        {
            activeTransfers.RemoveAll(t => t.Connection.Status != NetworkConnectionStatus.Connected);

            var endedTransfers = activeTransfers.FindAll(t => 
                t.Connection.Status != NetworkConnectionStatus.Connected ||
                t.Status == FileTransferStatus.Finished ||
                t.Status == FileTransferStatus.Canceled || 
                t.Status == FileTransferStatus.Error);

            foreach (FileTransferOut transfer in endedTransfers)
            {
                activeTransfers.Remove(transfer);
                OnEnded(transfer);
            }

            foreach (FileTransferOut transfer in activeTransfers)
            {
                transfer.WaitTimer -= deltaTime;
                if (transfer.WaitTimer > 0.0f) continue;
                
                transfer.WaitTimer = 0.05f;// transfer.Connection.AverageRoundtripTime;

                // send another part of the file
                long remaining = transfer.Data.Length - transfer.SentOffset;
                int sendByteCount = (remaining > chunkLen ? chunkLen : (int)remaining);
                
                IWriteMessage message;

                try
                {
                    //first message; send length, file name etc
                    //wait for acknowledgement before sending data
                    if (!transfer.Acknowledged)
                    {
                        message = new WriteOnlyMessage();
                        message.Write((byte)ServerPacketHeader.FILE_TRANSFER);

                        //if the recipient is the owner of the server (= a client running the server from the main exe)
                        //we don't need to send anything, the client can just read the file directly
                        if (transfer.Connection == GameMain.Server.OwnerConnection)
                        {
                            message.Write((byte)FileTransferMessageType.TransferOnSameMachine);
                            message.Write((byte)transfer.ID);
                            message.Write((byte)transfer.FileType);
                            message.Write(transfer.FilePath);
                            peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable);
                            transfer.Status = FileTransferStatus.Finished;
                        }
                        else
                        {
                            message.Write((byte)FileTransferMessageType.Initiate);
                            message.Write((byte)transfer.ID);
                            message.Write((byte)transfer.FileType);
                            //message.Write((ushort)chunkLen);
                            message.Write(transfer.Data.Length);
                            message.Write(transfer.FileName);
                            peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable);

                            transfer.Status = FileTransferStatus.Sending;

                            if (GameSettings.VerboseLogging)
                            {
                                DebugConsole.Log("Sending file transfer initiation message: ");
                                DebugConsole.Log("  File: " + transfer.FileName);
                                DebugConsole.Log("  Size: " + transfer.Data.Length);
                                DebugConsole.Log("  ID: " + transfer.ID);
                            }
                        }
                        return;
                    }

                    message = new WriteOnlyMessage();
                    message.Write((byte)ServerPacketHeader.FILE_TRANSFER);
                    message.Write((byte)FileTransferMessageType.Data);

                    message.Write((byte)transfer.ID);
                    message.Write(transfer.SentOffset);

                    byte[] sendBytes = new byte[sendByteCount];
                    Array.Copy(transfer.Data, transfer.SentOffset, sendBytes, 0, sendByteCount);

                    message.Write((ushort)sendByteCount);
                    message.Write(sendBytes, 0, sendByteCount);

                    transfer.SentOffset += sendByteCount;
                    if (transfer.SentOffset > transfer.KnownReceivedOffset + chunkLen * 5 ||
                        transfer.SentOffset >= transfer.Data.Length)
                    {
                        transfer.SentOffset = transfer.KnownReceivedOffset;
                    }

                    peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("FileSender threw an exception when trying to send data", e);
                    GameAnalyticsManager.AddErrorEventOnce(
                        "FileSender.Update:Exception", 
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error, 
                        "FileSender threw an exception when trying to send data:\n" + e.Message + "\n" + e.StackTrace);
                    transfer.Status = FileTransferStatus.Error;
                    break;
                }

                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.Log("Sending " + sendByteCount + " bytes of the file " + transfer.FileName + " (" + transfer.SentOffset + "/" + transfer.Data.Length + " sent)");
                }
            }
        }

        public void CancelTransfer(FileTransferOut transfer)
        {
            transfer.Status = FileTransferStatus.Canceled;
            activeTransfers.Remove(transfer);

            OnEnded(transfer);

            GameMain.Server.SendCancelTransferMsg(transfer);
        }

        public void ReadFileRequest(IReadMessage inc, Client client)
        {
            byte messageType = inc.ReadByte();

            if (messageType == (byte)FileTransferMessageType.Cancel)
            {
                byte transferId = inc.ReadByte();
                var matchingTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                if (matchingTransfer != null) CancelTransfer(matchingTransfer);

                return;
            }
            else if (messageType == (byte)FileTransferMessageType.Data)
            {
                byte transferId = inc.ReadByte();
                var matchingTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                if (matchingTransfer != null)
                {
                    matchingTransfer.Acknowledged = true;
                    int offset = inc.ReadInt32();
                    matchingTransfer.KnownReceivedOffset = offset > matchingTransfer.KnownReceivedOffset ? offset : matchingTransfer.KnownReceivedOffset;
                    if (matchingTransfer.SentOffset < matchingTransfer.KnownReceivedOffset) { matchingTransfer.SentOffset = matchingTransfer.KnownReceivedOffset; }

                    if (matchingTransfer.KnownReceivedOffset >= matchingTransfer.Data.Length)
                    {
                        matchingTransfer.Status = FileTransferStatus.Finished;
                    }
                }
            }

            byte fileType = inc.ReadByte();
            switch (fileType)
            {
                case (byte)FileTransferType.Submarine:
                    string fileName = inc.ReadString();
                    string fileHash = inc.ReadString();
                    var requestedSubmarine = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == fileName && s.MD5Hash.Hash == fileHash);

                    if (requestedSubmarine != null)
                    {
                        StartTransfer(inc.Sender, FileTransferType.Submarine, requestedSubmarine.FilePath);
                    }
                    break;
                case (byte)FileTransferType.CampaignSave:
                    if (GameMain.GameSession != null &&
                        !ActiveTransfers.Any(t => t.Connection == inc.Sender && t.FileType == FileTransferType.CampaignSave))
                    {                       
                        StartTransfer(inc.Sender, FileTransferType.CampaignSave, GameMain.GameSession.SavePath);
                        if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                        {
                            client.LastCampaignSaveSendTime = new Pair<ushort, float>(campaign.LastSaveID, (float)Lidgren.Network.NetTime.Now);
                        }
                    }
                    break;
            }
        }

    }
}
