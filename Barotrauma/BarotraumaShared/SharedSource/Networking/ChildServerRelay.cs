using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    static partial class ChildServerRelay
    {
        private static Stream writeStream;
        private static Stream readStream;
        private static volatile bool shutDown;

        private static byte[] tempBytes;
        private enum ReadState
        {
            WaitingForPacketStart,
            WaitingForPacketEnd
        };
        private static ReadState readState;
        private static byte[] readIncBuf;
        private static int readIncOffset;
        private static int readIncTotal;

        private static Queue<byte[]> msgsToWrite;
        private static Queue<byte[]> msgsToRead;

        private static Thread readThread;
        private static Thread writeThread;

        private static CancellationTokenSource readCancellationToken;

        private static void PrivateStart()
        {
            readState = ReadState.WaitingForPacketStart;
            readIncOffset = 0;
            readIncTotal = 0;

            tempBytes = new byte[MsgConstants.MTU * 2];

            msgsToWrite = new Queue<byte[]>();
            msgsToRead = new Queue<byte[]>();

            shutDown = false;

            readCancellationToken = new CancellationTokenSource();

            readThread = new Thread(UpdateRead);
            writeThread = new Thread(UpdateWrite);
            readThread.Start();
            writeThread.Start();
        }

        private static void PrivateShutDown()
        {
            shutDown = true;
            readCancellationToken?.Cancel();
            readThread?.Join(); readThread = null;
            writeThread?.Join(); writeThread = null;
            readCancellationToken?.Dispose();
            readCancellationToken = null;
            readStream?.Dispose(); readStream = null;
            writeStream?.Dispose(); writeStream = null;
        }

        private static void UpdateRead()
        {
            while (!shutDown)
            {
                Task<int> readTask = readStream?.ReadAsync(tempBytes, 0, tempBytes.Length, readCancellationToken.Token);
                TimeSpan ts = TimeSpan.FromMilliseconds(15000);
                if (readTask == null || !readTask.Wait(ts))
                {
                    readCancellationToken?.Cancel();
                    shutDown = true;
                    return;
                }

                if (readTask.Status != TaskStatus.RanToCompletion)
                {
                    shutDown = true;
                    return;
                }

                int readLen = readTask.Result;

                int procIndex = 0;

                while (procIndex < readLen)
                {
                    if (readState == ReadState.WaitingForPacketStart)
                    {
                        readIncTotal = tempBytes[procIndex] | (tempBytes[procIndex + 1] << 8);
                        readIncOffset = 0;
                        readIncBuf = new byte[readIncTotal];
                        procIndex += 2;
                        readState = ReadState.WaitingForPacketEnd;
                    }
                    else if (readState == ReadState.WaitingForPacketEnd)
                    {
                        if ((readIncTotal - readIncOffset) > (readLen - procIndex))
                        {
                            Array.Copy(tempBytes, procIndex, readIncBuf, readIncOffset, readLen - procIndex);
                            readIncOffset += (readLen - procIndex);
                            procIndex = readLen;
                        }
                        else
                        {
                            Array.Copy(tempBytes, procIndex, readIncBuf, readIncOffset, readIncTotal - readIncOffset);
                            procIndex += (readIncTotal - readIncOffset);
                            readIncOffset = readIncTotal;
                            lock (msgsToRead)
                            {
                                msgsToRead.Enqueue(readIncBuf);
                            }
                            readIncBuf = null;
                            readState = ReadState.WaitingForPacketStart;
                        }
                    }

                    if (shutDown) { break; }
                }
                Thread.Yield();
            }
        }

        private static void UpdateWrite()
        {
            while (!shutDown)
            {
                bool msgAvailable; byte[] msg;
                lock (msgsToWrite)
                {
                    msgAvailable = msgsToWrite.TryDequeue(out msg);
                }
                while (msgAvailable)
                {
                    byte[] lengthBytes = new byte[2];
                    lengthBytes[0] = (byte)(msg.Length & 0xFF);
                    lengthBytes[1] = (byte)((msg.Length >> 8) & 0xFF);
                    writeStream?.Write(lengthBytes, 0, 2);
                    writeStream?.Write(msg, 0, msg.Length);

                    if (shutDown) { break; }

                    lock (msgsToWrite)
                    {
                        msgAvailable = msgsToWrite.TryDequeue(out msg);
                    }
                }
                Thread.Yield();
            }
        }

        public static void Write(byte[] msg)
        {
            if (shutDown) { return; }

            lock (msgsToWrite)
            {
                msgsToWrite.Enqueue(msg);
            }
        }

        public static bool Read(out byte[] msg)
        {
            if (shutDown) { msg = null; return false; }

            lock (msgsToRead)
            {
                return msgsToRead.TryDequeue(out msg);
            }
        }
    }
}

