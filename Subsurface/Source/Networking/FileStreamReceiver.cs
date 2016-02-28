using Lidgren.Network;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    class FileStreamReceiver : IDisposable
    {
        const int MaxFileSize = 1000000;

        public delegate void OnFinished(FileStreamReceiver fileStreamReceiver);
        private OnFinished onFinished;

        private NetClient client;
        private ulong length;
        private ulong received;
        private FileStream writeStream;
        private int timeStarted;

        private string downloadFolder;

        private FileTransferMessageType fileType;
        
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

        public FileTransferMessageType FileType
        {
            get { return fileType; }
        }
                
        public FileTransferStatus Status
        {
            get;
            private set;
        }

        public string ErrorMessage
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
            get { return (float)received / (float)length; }
        }

        public FileStreamReceiver(NetClient client, string filePath, FileTransferMessageType fileType, OnFinished onFinished)
        {
            this.client = client;

            this.downloadFolder = filePath;
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
                ErrorMessage = "Error while receiving file ''"+FileName+"'' {"+e.Message+"}";
                DeleteFile();

                if (onFinished != null) onFinished(this);
            }
        }

        private bool ValidateInitialData(byte type, string fileName, ulong fileSize)
        {
            if (fileSize > MaxFileSize)
            {
                ErrorMessage = "File too large (" + MathUtils.GetBytesReadable((long)fileSize) + ")";
                return false;
            }

            if (type != (byte)fileType)
            {
                ErrorMessage = "Unexpected file type ''" + type + "'' (expected " + fileType + ")";
                return false;
            }

            if (!Regex.Match(fileName, @"^[\w\- ]+[\w\-. ]*$").Success)
            {
                ErrorMessage = "Illegal characters in file name ''" + fileName + "''";
                return false;
            }

            switch (type)
            {
                case (byte)FileTransferMessageType.Submarine:
                    if (Path.GetExtension(fileName) != ".sub")
                    {
                        ErrorMessage = "Wrong file extension ''" + Path.GetExtension(fileName) + "''! (Expected .sub)";
                        return false;
                    }
                    break;
            }

            return true;
        }

        public void DeleteFile()
        {
            if (FileName == null) return;

            string file = Path.Combine(downloadFolder, FileName);

            if (writeStream!=null)
            {
                writeStream.Flush();
                writeStream.Close();
                writeStream.Dispose();
                writeStream = null;
            }

            Status = FileTransferStatus.Canceled;

            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Couldn't delete file ''" + file + "''!", e);
                }
            }
        }

        private void TryReadMessage(NetIncomingMessage inc)
        {
            if (Status == FileTransferStatus.Error || 
                Status == FileTransferStatus.Finished || 
                Status == FileTransferStatus.Canceled) return;

            byte transferMessageType = inc.ReadByte();

            //int chunkLen = inc.LengthBytes;
            if (length == 0)
            {
                if (transferMessageType != (byte)FileTransferMessageType.Initiate) return;

                if (!string.IsNullOrWhiteSpace(downloadFolder) && !Directory.Exists(downloadFolder))
                {
                    Directory.CreateDirectory(downloadFolder);
                }

                byte fileTypeByte = inc.ReadByte();


                length = inc.ReadUInt64();
                FileName = inc.ReadString();

                if (!ValidateInitialData(fileTypeByte, FileName, length))
                {
                    Status = FileTransferStatus.Error;
                    DeleteFile();
                    if (onFinished != null) onFinished(this);
                    return;
                }

                writeStream = new FileStream(Path.Combine(downloadFolder, FileName), FileMode.Create, FileAccess.Write, FileShare.None);
                timeStarted = Environment.TickCount;

                Status = FileTransferStatus.NotStarted;

                return;
            }


            if (received + (ulong)inc.LengthBytes > length*1.1f)
            {
                ErrorMessage = "Receiving more data than expected (> " + MathUtils.GetBytesReadable((long)(received + (ulong)inc.LengthBytes)) + ")";
                Status = FileTransferStatus.Error;
                if (onFinished != null) onFinished(this);
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
                writeStream.Flush();
                writeStream.Close();
                writeStream.Dispose();
                writeStream = null;

                Status = IsReceivedFileValid() ? FileTransferStatus.Finished : FileTransferStatus.Error;
                if (onFinished!=null) onFinished(this);

                if (Status == FileTransferStatus.Error) DeleteFile();
                Dispose();
            }
        }

        private bool IsReceivedFileValid()
        {
            switch (fileType)
            {
                case FileTransferMessageType.Submarine:
                    string file = Path.Combine(downloadFolder, FileName);
                    Stream stream = null;

                    try
                    {
                        stream = SaveUtil.DecompressFiletoStream(file);
                    }
                    catch (Exception e)
                    {
                        ErrorMessage = "Loading submarine ''" + file + "'' failed! {"+ e.Message + "}";
                        return false;
                    }  

                    if (stream == null)
                    {
                        ErrorMessage = "Decompressing submarine file''" + file + "'' failed!";
                        return false;
                    }

                    try
                    {
                        stream.Position = 0;

                        XmlReaderSettings settings = new XmlReaderSettings();
                        settings.DtdProcessing = DtdProcessing.Prohibit;
                        settings.IgnoreProcessingInstructions = true;

                        using (var reader = XmlReader.Create(stream, settings))
                        {
                            while (reader.Read())
                            {

                            }
                        }
                    }
                    catch
                    {
                        stream.Close();
                        stream.Dispose();

                        ErrorMessage = "Parsing file ''"+file+"'' failed! The file may not be a valid submarine file.";
                        return false;
                    }

                    stream.Close();
                    stream.Dispose();
                break;
            }

            return true;
        }
        
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (writeStream != null)
            {
                writeStream.Flush();
                writeStream.Close();
                writeStream.Dispose();
            }
        }
    }
    
     
}
