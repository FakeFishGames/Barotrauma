using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    class FileSender
    {
        public class FileTransferOut
        {
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

            private float waitTimer;
            public float WaitTimer
            {
                get => waitTimer;
                set
                {
                    if (value > 0.0f)
                    {
                        //setting a wait timer means that network conditions
                        //aren't ideal, slow down the packet rate
                        PacketsPerUpdate = Math.Max(PacketsPerUpdate / 4.0f, 1.0f);
                    }
                    waitTimer = value;
                }
            }

            public const int MaxPacketsPerUpdate = 10;
            public float PacketsPerUpdate { get; set; } = 1.0f;

            public byte[] Data { get; }

            public bool Acknowledged;

            public int SentOffset
            {
                get;
                set;
            }

            public int KnownReceivedOffset;

            public NetworkConnection Connection { get; }

            public DateTime StartingTime { get; }

            public int ID;

            public FileTransferOut(NetworkConnection recipient, FileTransferType fileType, string filePath)
            {
                Connection = recipient;

                FileType = fileType;
                FilePath = filePath;
                FileName = Path.GetFileName(filePath);

                Acknowledged = false;
                SentOffset = 0;
                KnownReceivedOffset = 0;

                Status = FileTransferStatus.NotStarted;
                
                StartingTime = DateTime.Now;
                
                int maxRetries = 4;
                for (int i = 0; i <= maxRetries; i++)
                {
                    try
                    {
                        Data = File.ReadAllBytes(filePath);
                    }
                    catch (System.IO.IOException e)
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

#if DEBUG
        public float StallPacketsTime { get; set; }
#endif

        public IReadOnlyList<FileTransferOut> ActiveTransfers => activeTransfers;

        public FileSender(ServerPeer serverPeer, int mtu)
        {
            peer = serverPeer;
            chunkLen = mtu - 200;

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
                DebugConsole.ThrowError("Failed to initiate file transfer (file \"" + filePath + "\" not found).\n" + Environment.StackTrace);
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
            GameMain.Server.LastClientListUpdateID++;

            return transfer;
        }

        public void Update(float deltaTime)
        {
            int numRemoved = activeTransfers.RemoveAll(t => t.Connection.Status != NetworkConnectionStatus.Connected);

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
                if (transfer.WaitTimer > 0.0f) { continue; }
                Send(transfer);
            }

            if (numRemoved > 0 || endedTransfers.Count > 0)
            {
                GameMain.Server.LastClientListUpdateID++;
            }
        }

        private void Send(FileTransferOut transfer)
        {
            // send another part of the file
            IWriteMessage message;

            try
            {
                //first message; send length, file name etc
                //wait for acknowledgement before sending data
                if (!transfer.Acknowledged)
                {
                    message = new WriteOnlyMessage();
                    message.WriteByte((byte)ServerPacketHeader.FILE_TRANSFER);

                    //if the recipient is the owner of the server (= a client running the server from the main exe)
                    //we don't need to send anything, the client can just read the file directly
                    if (transfer.Connection == GameMain.Server.OwnerConnection)
                    {
                        message.WriteByte((byte)FileTransferMessageType.TransferOnSameMachine);
                        message.WriteByte((byte)transfer.ID);
                        message.WriteByte((byte)transfer.FileType);
                        message.WriteString(transfer.FilePath);
                        peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable);
                        transfer.Status = FileTransferStatus.Finished;
                    }
                    else
                    {
                        message.WriteByte((byte)FileTransferMessageType.Initiate);
                        message.WriteByte((byte)transfer.ID);
                        message.WriteByte((byte)transfer.FileType);
                        //message.Write((ushort)chunkLen);
                        message.WriteInt32(transfer.Data.Length);
                        message.WriteString(transfer.FileName);
                        peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable);

                        transfer.Status = FileTransferStatus.Sending;

                        if (GameSettings.CurrentConfig.VerboseLogging)
                        {
                            DebugConsole.Log("Sending file transfer initiation message: ");
                            DebugConsole.Log("  File: " + transfer.FileName);
                            DebugConsole.Log("  Size: " + transfer.Data.Length);
                            DebugConsole.Log("  ID: " + transfer.ID);
                        }
                    }
                    transfer.WaitTimer = 0.1f;
                    return;
                }

                for (int i = 0; i < Math.Floor(transfer.PacketsPerUpdate); i++)
                {
                    long remaining = transfer.Data.Length - transfer.SentOffset;
                    int sendByteCount = (remaining > chunkLen ? chunkLen : (int)remaining);
                    
                    message = new WriteOnlyMessage();
                    message.WriteByte((byte)ServerPacketHeader.FILE_TRANSFER);
                    message.WriteByte((byte)FileTransferMessageType.Data);

                    message.WriteByte((byte)transfer.ID);
                    message.WriteInt32(transfer.SentOffset);

                    message.WriteUInt16((ushort)sendByteCount);
                    int chunkDestPos = message.BytePosition;
                    message.BitPosition += sendByteCount * 8;
                    message.LengthBits = Math.Max(message.LengthBits, message.BitPosition);
                    Array.Copy(transfer.Data, transfer.SentOffset, message.Buffer, chunkDestPos, sendByteCount);

                    transfer.SentOffset += sendByteCount;

                    peer.Send(message, transfer.Connection, DeliveryMethod.Unreliable, compressPastThreshold: false);

                    if (GameSettings.CurrentConfig.VerboseLogging)
                    {
                        DebugConsole.Log($"Sending {sendByteCount} bytes of the file {transfer.FileName} ({transfer.SentOffset / 1000}/{transfer.Data.Length / 1000} kB sent)");
                    }
                    
                    if (transfer.SentOffset >= transfer.Data.Length)
                    {
                        transfer.SentOffset = transfer.KnownReceivedOffset;
                        transfer.WaitTimer = 1.0f;
                    }

                    //try to increase the packet rate so large files get sent faster,
                    //this gets reset when packet loss or disorder sets in
                    transfer.PacketsPerUpdate = Math.Min(FileTransferOut.MaxPacketsPerUpdate,
                        transfer.PacketsPerUpdate + 0.05f);
                }
                
#if DEBUG
                transfer.WaitTimer = Math.Max(transfer.WaitTimer, StallPacketsTime);
#endif
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("FileSender threw an exception when trying to send data", e);
                GameAnalyticsManager.AddErrorEventOnce(
                    "FileSender.Update:Exception",
                    GameAnalyticsManager.ErrorSeverity.Error,
                    "FileSender threw an exception when trying to send data:\n" + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
                transfer.Status = FileTransferStatus.Error;
                return;
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
            FileTransferMessageType messageType = (FileTransferMessageType)inc.ReadByte();

            if (messageType == FileTransferMessageType.Cancel)
            {
                byte transferId = inc.ReadByte();
                var matchingTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                if (matchingTransfer != null) CancelTransfer(matchingTransfer);
                return;
            }
            else if (messageType == FileTransferMessageType.Data)
            {
                byte transferId = inc.ReadByte();
                var matchingTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                if (matchingTransfer != null)
                {
                    matchingTransfer.Acknowledged = true;
                    int expecting = inc.ReadInt32(); //the offset the client is waiting for
                    int lastSeen = Math.Min(matchingTransfer.SentOffset, inc.ReadInt32()); //the last offset the client got from us
                    matchingTransfer.KnownReceivedOffset = Math.Max(expecting, matchingTransfer.KnownReceivedOffset);
                    if (matchingTransfer.SentOffset < matchingTransfer.KnownReceivedOffset)
                    {
                        matchingTransfer.WaitTimer = 0.0f;
                        matchingTransfer.SentOffset = matchingTransfer.KnownReceivedOffset; 
                    }
                    
                    if (lastSeen - matchingTransfer.KnownReceivedOffset >= chunkLen * 10 ||
                        matchingTransfer.SentOffset >= matchingTransfer.Data.Length)
                    {
                        matchingTransfer.SentOffset = matchingTransfer.KnownReceivedOffset;
                        matchingTransfer.WaitTimer = 1.0f;
                    }

                    if (matchingTransfer.KnownReceivedOffset >= matchingTransfer.Data.Length)
                    {
                        matchingTransfer.Status = FileTransferStatus.Finished;
                    }
                }
                return;
            }

            FileTransferType fileType = (FileTransferType)inc.ReadByte();
            switch (fileType)
            {
                case FileTransferType.Submarine:
                    string fileName = inc.ReadString();
                    string fileHash = inc.ReadString();
                    var requestedSubmarine = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == fileName && s.MD5Hash.StringRepresentation == fileHash);

                    DebugConsole.Log($"Received a submarine file request from \"{client.Name}\" ({fileName}).");

                    if (requestedSubmarine != null)
                    {
                        if (activeTransfers.Any(t => t.Connection == inc.Sender && t.FilePath == requestedSubmarine.FilePath))
                        {
                            DebugConsole.Log($"Ignoring a submarine file request from \"{client.Name}\" ({fileName}) - already transferring.");
                            return;
                        }
                        StartTransfer(inc.Sender, FileTransferType.Submarine, requestedSubmarine.FilePath);
                    }
                    break;
                case FileTransferType.CampaignSave:
                    if (GameMain.GameSession != null &&
                        !ActiveTransfers.Any(t => t.Connection == inc.Sender && t.FileType == FileTransferType.CampaignSave))
                    {
                        StartTransfer(inc.Sender, FileTransferType.CampaignSave, GameMain.GameSession.SavePath);
                        if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                        {
                            client.LastCampaignSaveSendTime = (campaign.LastSaveID, (float)Lidgren.Network.NetTime.Now);
                        }
                    }
                    break;
                case FileTransferType.Mod:
                    string modName = inc.ReadString();
                    Md5Hash modHash = Md5Hash.StringAsHash(inc.ReadString());

                    DebugConsole.Log($"Received a mod file request from \"{client.Name}\" ({modName}).");

                    if (!GameMain.Server.ServerSettings.AllowModDownloads) { return; }
                    if (!(GameMain.Server.ModSender is { Ready: true })) { return; }
                    
                    ContentPackage mod = ContentPackageManager.AllPackages.FirstOrDefault(p => p.Hash.Equals(modHash));

                    if (mod is null) { return; }
                    
                    string modCompressedPath = ModSender.GetCompressedModPath(mod);
                    if (!File.Exists(modCompressedPath)) { return; }

                    if (activeTransfers.Any(t => t.Connection == inc.Sender && t.FilePath == modCompressedPath))
                    {
                        DebugConsole.Log($"Ignoring a mod file request from \"{client.Name}\" ({modName}) - already transferring.");
                        return;
                    }

                    StartTransfer(inc.Sender, FileTransferType.Mod, modCompressedPath);
                    
                    break;
            }
        }

    }
}
