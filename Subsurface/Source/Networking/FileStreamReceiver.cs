using Lidgren.Network;
using System;
using System.IO;
using System.Text.RegularExpressions;

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

        private string filePath;

        private FileTransferType fileType;
        
        public string FileName
        {
            get;
            private set;
        }
        
        public string FilePath
        {
            get { return filePath; }
        }

        public ulong FileSize
        {
            get { return length; }
        }

        public ulong Received
        {
            get { return received; }
        }

        public FileTransferType FileType
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

        public FileStreamReceiver(NetClient client, string filePath, FileTransferType fileType, OnFinished onFinished)
        {
            this.client = client;

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

        private bool ValidateInitialData(byte type, string fileName, ulong fileSize)
        {
            if (fileSize > MaxFileSize)
            {
                ErrorMessage = "File too large (" + MathUtils.GetBytesReadable((long)fileSize) + ")";
                Status = FileTransferStatus.Error;
                return false;
            }

            if (type != (byte)fileType)
            {
                ErrorMessage = "Unexpected file type ''"+type+"'' (expected "+fileType+")";
                Status = FileTransferStatus.Error;
                return false;
            }

            if (!Regex.Match(fileName, @"^[\w\- ]+[\w\-. ]*$").Success)
            {
                ErrorMessage = "Illegal characters in file name ''"+fileName+"''";
                Status = FileTransferStatus.Error;
                return false;
            }

            switch (type)
            {
                case (byte)FileTransferType.Submarine:
                    if (Path.GetExtension(fileName) != ".sub")
                    {
                        ErrorMessage = "Wrong file extension ''" + Path.GetExtension(fileName)+"''! (Expected .sub)";

                        Status = FileTransferStatus.Error;
                        return false;
                    }
                    break;
            }

            return true;
        }

        public void DeleteFile()
        {
            string file = Path.Combine(filePath, FileName);

            writeStream.Flush();
            writeStream.Close();
            writeStream.Dispose();
            writeStream = null;

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

            //int chunkLen = inc.LengthBytes;
            if (length == 0)
            {

                if (!Directory.Exists(filePath))
                {
                    Directory.CreateDirectory(filePath);
                }

                byte fileTypeByte = inc.ReadByte();

                length = inc.ReadUInt64();
                FileName = inc.ReadString();

                if (!ValidateInitialData(fileTypeByte, FileName, length))
                {
                    Status = FileTransferStatus.Error;
                    if (onFinished != null) onFinished(this);
                    return;
                }

                writeStream = new FileStream(Path.Combine(filePath, FileName), FileMode.Create, FileAccess.Write, FileShare.None);
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
            if (writeStream != null)
            {
                writeStream.Flush();
                writeStream.Close();
                writeStream.Dispose();
            }
        }
    }
    
     
}
