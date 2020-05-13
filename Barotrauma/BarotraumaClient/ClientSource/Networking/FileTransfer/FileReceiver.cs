using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace Barotrauma.Networking
{
    class FileReceiver
    {
        public class FileTransferIn : IDisposable
        {            
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

            public int FileSize
            {
                get;
                set;
            }

            public int Received
            {
                get;
                private set;
            }

            public FileTransferType FileType
            {
                get;
                private set;
            }

            public FileTransferStatus Status
            {
                get;
                set;
            }

            public float BytesPerSecond
            {
                get;
                private set;
            }

            public float Progress
            {
                get { return Received / (float)FileSize; }
            }

            public FileStream WriteStream
            {
                get;
                private set;
            }

            public int TimeStarted
            {
                get;
                private set;
            }

            public NetworkConnection Connection
            {
                get;
                private set;
            }

            public int ID;

            public FileTransferIn(NetworkConnection connection, string filePath, FileTransferType fileType)
            {
                FilePath = filePath;
                FileName = Path.GetFileName(FilePath);
                FileType = fileType;

                Connection = connection;               

                Status = FileTransferStatus.NotStarted;
            }

            public void OpenStream()
            {
                if (WriteStream != null)
                {
                    WriteStream.Flush();
                    WriteStream.Close();
                    WriteStream.Dispose();
                    WriteStream = null;
                }

                WriteStream = File.Open(FilePath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                TimeStarted = Environment.TickCount;
            }

            public void ReadBytes(IReadMessage inc, int bytesToRead)
            {
                if (Received + bytesToRead > FileSize)
                {
                    //strip out excess bytes
                    bytesToRead -= Received + bytesToRead - FileSize;
                }

                byte[] all = inc.ReadBytes(bytesToRead);
                Received += all.Length;
                WriteStream.Write(all, 0, all.Length);

                int passed = Environment.TickCount - TimeStarted;
                float psec = passed / 1000.0f;

                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.Log("Received " + all.Length + " bytes of the file " + FileName + " (" + Received + "/" + FileSize + " received)");
                }

                BytesPerSecond = Received / psec;

                Status = Received >= FileSize ? FileTransferStatus.Finished : FileTransferStatus.Receiving;
            }

            private bool disposed = false;
            protected virtual void Dispose(bool disposing)
            {
                if (disposed) return;
                
                if (disposing)
                {
                    if (WriteStream != null)
                    {
                        WriteStream.Flush();
                        WriteStream.Close();
                        WriteStream.Dispose();
                        WriteStream = null;
                    }
                }
                disposed = true;                
            }
            
            public void Dispose()
            {
                Dispose(true);
            }
        }
        
        const int MaxFileSize = 50000000; //50 MB
        
        public delegate void TransferInDelegate(FileTransferIn fileStreamReceiver);
        public TransferInDelegate OnFinished;
        public TransferInDelegate OnTransferFailed;

        private readonly List<FileTransferIn> activeTransfers;
        private readonly List<Pair<int, double>> finishedTransfers;

        private readonly Dictionary<FileTransferType, string> downloadFolders = new Dictionary<FileTransferType, string>()
        {
            { FileTransferType.Submarine, SaveUtil.SubmarineDownloadFolder },
            { FileTransferType.CampaignSave, SaveUtil.CampaignDownloadFolder }
        };

        public List<FileTransferIn> ActiveTransfers
        {
            get { return activeTransfers; }
        }

        public FileReceiver()
        {
            activeTransfers = new List<FileTransferIn>();
            finishedTransfers = new List<Pair<int, double>>();
        }
        
        public void ReadMessage(IReadMessage inc)
        {
            System.Diagnostics.Debug.Assert(!activeTransfers.Any(t => 
                t.Status == FileTransferStatus.Error ||
                t.Status == FileTransferStatus.Canceled ||
                t.Status == FileTransferStatus.Finished), "List of active file transfers contains entires that should have been removed");

            byte transferMessageType = inc.ReadByte();

            switch (transferMessageType)
            {
                case (byte)FileTransferMessageType.Initiate:
                    {
                        byte transferId = inc.ReadByte();
                        var existingTransfer = activeTransfers.Find(t => t.ID == transferId);
                        finishedTransfers.RemoveAll(t => t.First  == transferId);
                        byte fileType = inc.ReadByte();
                        //ushort chunkLen = inc.ReadUInt16();
                        int fileSize = inc.ReadInt32();
                        string fileName = inc.ReadString();

                        if (existingTransfer != null)
                        {
                            if (fileType != (byte)existingTransfer.FileType ||
                                fileSize != existingTransfer.FileSize ||
                                fileName != existingTransfer.FileName)
                            {
                                GameMain.Client.CancelFileTransfer(transferId);
                                DebugConsole.ThrowError("File transfer error: file transfer initiated with an ID that's already in use");
                            }
                            else //resend acknowledgement packet
                            {
                                GameMain.Client.UpdateFileTransfer(transferId, 0);
                            }
                            return;
                        }
                        
                        if (!ValidateInitialData(fileType, fileName, fileSize, out string errorMsg))
                        {
                            GameMain.Client.CancelFileTransfer(transferId);
                            DebugConsole.ThrowError("File transfer failed (" + errorMsg + ")");
                            return;
                        }

                        if (GameSettings.VerboseLogging)
                        {
                            DebugConsole.Log("Received file transfer initiation message: ");
                            DebugConsole.Log("  File: " + fileName);
                            DebugConsole.Log("  Size: " + fileSize);
                            DebugConsole.Log("  ID: " + transferId);
                        }

                        string downloadFolder = downloadFolders[(FileTransferType)fileType];
                        if (!Directory.Exists(downloadFolder))
                        {
                            try
                            {
                                Directory.CreateDirectory(downloadFolder);
                            }
                            catch (Exception e)
                            {
                                DebugConsole.ThrowError("Could not start a file transfer: failed to create the folder \"" + downloadFolder + "\".", e);
                                return;
                            }
                        }

                        FileTransferIn newTransfer = new FileTransferIn(inc.Sender, Path.Combine(downloadFolder, fileName), (FileTransferType)fileType)
                        {
                            ID = transferId,
                            Status = FileTransferStatus.Receiving,
                            FileSize = fileSize
                        };

                        int maxRetries = 4;
                        for (int i = 0; i <= maxRetries; i++)
                        {
                            try
                            {
                                newTransfer.OpenStream();
                            }
                            catch (System.IO.IOException e)
                            {
                                if (i < maxRetries)
                                {
                                    DebugConsole.NewMessage("Failed to initiate a file transfer {" + e.Message + "}, retrying in 250 ms...", Color.Red);
                                    Thread.Sleep(250);
                                }
                                else
                                {
                                    DebugConsole.NewMessage("Failed to initiate a file transfer {" + e.Message + "}", Color.Red);
                                    GameMain.Client.CancelFileTransfer(transferId);
                                    newTransfer.Status = FileTransferStatus.Error;
                                    OnTransferFailed(newTransfer);
                                    return;
                                }
                            }
                        }
                        activeTransfers.Add(newTransfer);

                        GameMain.Client.UpdateFileTransfer(transferId, 0); //send acknowledgement packet
                    }
                    break;
                case (byte)FileTransferMessageType.TransferOnSameMachine:
                    {
                        byte transferId = inc.ReadByte();
                        byte fileType = inc.ReadByte();
                        string filePath = inc.ReadString();

                        if (GameSettings.VerboseLogging)
                        {
                            DebugConsole.Log("Received file transfer message on the same machine: ");
                            DebugConsole.Log("  File: " + filePath);
                            DebugConsole.Log("  ID: " + transferId);
                        }

                        if (!File.Exists(filePath))
                        {
                            DebugConsole.ThrowError("File transfer on the same machine failed, file \"" + filePath + "\" not found.");
                            GameMain.Client.CancelFileTransfer(transferId);
                            return;
                        }

                        FileTransferIn directTransfer = new FileTransferIn(inc.Sender, filePath, (FileTransferType)fileType)
                        {
                            ID = transferId,
                            Status = FileTransferStatus.Finished,
                            FileSize = 0
                        };

                        Md5Hash.RemoveFromCache(directTransfer.FilePath);
                        OnFinished(directTransfer);
                    }
                    break;
                case (byte)FileTransferMessageType.Data:
                    {
                        byte transferId = inc.ReadByte();

                        var activeTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                        if (activeTransfer == null)
                        {
                            //it's possible for the server to send some extra data
                            //before it acknowledges that the download is finished,
                            //so let's suppress the error message in that case
                            finishedTransfers.RemoveAll(t => t.Second + 5.0 < Timing.TotalTime);
                            if (!finishedTransfers.Any(t => t.First == transferId))
                            {
                                GameMain.Client.CancelFileTransfer(transferId);
                                DebugConsole.ThrowError("File transfer error: received data without a transfer initiation message");
                            }
                            return;
                        }

                        int offset = inc.ReadInt32();
                        if (offset != activeTransfer.Received)
                        {
                            if (offset < activeTransfer.Received)
                            {
                                GameMain.Client.UpdateFileTransfer(activeTransfer.ID, activeTransfer.Received);
                            }
                            return;
                        }

                        int bytesToRead = inc.ReadUInt16();
                        
                        if (activeTransfer.Received + bytesToRead > activeTransfer.FileSize)
                        {
                            GameMain.Client.CancelFileTransfer(transferId);
                            DebugConsole.ThrowError("File transfer error: Received more data than expected (total received: " + activeTransfer.Received +
                                ", msg received: " + (inc.LengthBytes - inc.BytePosition) +
                                ", msg length: " + inc.LengthBytes +
                                ", msg read: " + inc.BytePosition +
                                ", filesize: " + activeTransfer.FileSize);
                            activeTransfer.Status = FileTransferStatus.Error;
                            StopTransfer(activeTransfer);
                            return;
                        }

                        try
                        {
                            activeTransfer.ReadBytes(inc, bytesToRead);
                        }
                        catch (Exception e)
                        {
                            GameMain.Client.CancelFileTransfer(transferId);
                            DebugConsole.ThrowError("File transfer error: " + e.Message);
                            activeTransfer.Status = FileTransferStatus.Error;
                            StopTransfer(activeTransfer, true);
                            return;
                        }

                        if (activeTransfer.Status == FileTransferStatus.Finished)
                        {
                            GameMain.Client.UpdateFileTransfer(activeTransfer.ID, activeTransfer.Received, true);
                            activeTransfer.Dispose();

                            if (ValidateReceivedData(activeTransfer, out string errorMessage))
                            {
                                finishedTransfers.Add(new Pair<int, double>(transferId, Timing.TotalTime));
                                StopTransfer(activeTransfer);
                                Md5Hash.RemoveFromCache(activeTransfer.FilePath);
                                OnFinished(activeTransfer);
                            }
                            else
                            {
                                new GUIMessageBox("File transfer aborted", errorMessage);

                                activeTransfer.Status = FileTransferStatus.Error;
                                StopTransfer(activeTransfer, true);
                            }
                        }
                    }
                    break;
                case (byte)FileTransferMessageType.Cancel:
                    {
                        byte transferId = inc.ReadByte();
                        var matchingTransfer = activeTransfers.Find(t => t.Connection == inc.Sender && t.ID == transferId);
                        if (matchingTransfer != null)
                        {
                            new GUIMessageBox("File transfer cancelled", "The server has cancelled the transfer of the file \"" + matchingTransfer.FileName + "\".");
                            StopTransfer(matchingTransfer);
                        }
                        break;
                    }
            }
        }

        private bool ValidateInitialData(byte type, string fileName, int fileSize, out string errorMessage)
        {
            errorMessage = "";

            if (fileSize > MaxFileSize)
            {
                errorMessage = "File too large (" + MathUtils.GetBytesReadable(fileSize) + ")";
                return false;
            }

            if (!Enum.IsDefined(typeof(FileTransferType), (int)type))
            {
                errorMessage = "Unknown file type";
                return false;
            }

            if (string.IsNullOrEmpty(fileName) ||
<<<<<<< HEAD
                fileName.IndexOfAny(Path.GetInvalidFileNameChars().ToArray()) > -1)
=======
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
>>>>>>> master
            {
                errorMessage = "Illegal characters in file name ''" + fileName + "''";
                return false;
            }

            switch (type)
            {
                case (byte)FileTransferType.Submarine:
                    if (Path.GetExtension(fileName) != ".sub")
                    {
                        errorMessage = "Wrong file extension ''" + Path.GetExtension(fileName) + "''! (Expected .sub)";
                        return false;
                    }
                    break;
                case (byte)FileTransferType.CampaignSave:
                    if (Path.GetExtension(fileName) != ".save")
                    {
                        errorMessage = "Wrong file extension ''" + Path.GetExtension(fileName) + "''! (Expected .save)";
                        return false;
                    }
                    break;
            }

            return true;
        }

        private bool ValidateReceivedData(FileTransferIn fileTransfer, out string ErrorMessage)
        {
            ErrorMessage = "";
            switch (fileTransfer.FileType)
            {
                case FileTransferType.Submarine:
<<<<<<< HEAD
                    System.IO.Stream stream;
=======
                    Stream stream;
>>>>>>> master
                    try
                    {
                        stream = SaveUtil.DecompressFiletoStream(fileTransfer.FilePath);
                    }
                    catch (Exception e)
                    {
                        ErrorMessage = "Loading received submarine \"" + fileTransfer.FileName + "\" failed! {" + e.Message + "}";
                        return false;
                    }

                    if (stream == null)
                    {
                        ErrorMessage = "Decompressing received submarine file \"" + fileTransfer.FilePath + "\" failed!";
                        return false;
                    }

                    try
                    {
                        stream.Position = 0;

                        XmlReaderSettings settings = new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            IgnoreProcessingInstructions = true
                        };

                        using (var reader = XmlReader.Create(stream, settings))
                        {
                            while (reader.Read());
                        }
                    }
                    catch
                    {
                        stream?.Close();
                        ErrorMessage = "Parsing file \"" + fileTransfer.FilePath + "\" failed! The file may not be a valid submarine file.";
                        return false;
                    }

                    stream?.Close();
                    break;
                case FileTransferType.CampaignSave:
                    //TODO: verify that the received file is a valid save file
                    break;
            }

            return true;
        }
        
        public void StopTransfer(FileTransferIn transfer, bool deleteFile = false)
        {
            if (transfer.Status != FileTransferStatus.Finished && 
                transfer.Status != FileTransferStatus.Error)
            {
                transfer.Status = FileTransferStatus.Canceled;
            }

            if (activeTransfers.Contains(transfer)) activeTransfers.Remove(transfer);
            transfer.Dispose();

            if (deleteFile && File.Exists(transfer.FilePath))
            {
                try
                {
                    File.Delete(transfer.FilePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to delete file \"" + transfer.FilePath + "\" (" + e.Message + ")");
                }
            }
        }
    }
}
