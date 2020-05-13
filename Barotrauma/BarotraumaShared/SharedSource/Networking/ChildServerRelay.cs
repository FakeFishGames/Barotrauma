using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    static partial class ChildServerRelay
    {
        private static System.IO.Stream writeStream;
        private static System.IO.Stream readStream;
        private static volatile bool shutDown;
        public static bool HasShutDown
        {
            get { return shutDown; }
        }
        private static ManualResetEvent writeManualResetEvent;

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

            writeManualResetEvent = new ManualResetEvent(false);

            readThread = new Thread(UpdateRead)
            {
                Name = "ChildServerRelay.ReadThread",
                IsBackground = true
            };
            writeThread = new Thread(UpdateWrite)
            {
                Name = "ChildServerRelay.WriteThread",
                IsBackground = true
            };
            readThread.Start();
            writeThread.Start();
        }

        private static void PrivateShutDown()
        {
            shutDown = true;
            writeManualResetEvent?.Set();
            readCancellationToken?.Cancel();
            readThread?.Join(); readThread = null;
            writeThread?.Join(); writeThread = null;
            readCancellationToken?.Dispose();
            readCancellationToken = null;
            readStream?.Dispose(); readStream = null;
            writeStream?.Dispose(); writeStream = null;
            msgsToRead?.Clear(); msgsToWrite?.Clear();
        }


        private static int ReadIncomingMsgs()
        {
            Task<int> readTask = readStream?.ReadAsync(tempBytes, 0, tempBytes.Length, readCancellationToken.Token);
            TimeSpan ts = TimeSpan.FromMilliseconds(100);
            for (int i = 0; i < 150; i++)
            {
                if (shutDown)
                {
                    readCancellationToken?.Cancel();
                    shutDown = true;
                    return -1;
                }

                if ((readTask?.IsCompleted ?? true) || (readTask?.Wait(ts) ?? true))
                {
                    break;
                }
            }

            if (readTask == null || !readTask.IsCompleted)
            {
                readCancellationToken?.Cancel();
                shutDown = true;
                return -1;
            }

            if (readTask.Status != TaskStatus.RanToCompletion)
            {
                shutDown = true;
                return -1;
            }

            return readTask.Result;
        }


        private static void UpdateRead()
        {
            while (!shutDown)
            {
#if SERVER
                if (!((readStream as AnonymousPipeClientStream)?.IsConnected ?? false))
                {
                    shutDown = true;
                    return;
                }
#else
                if (!((readStream as AnonymousPipeServerStream)?.IsConnected ?? false))
                {
                    shutDown = true;
                    return;
                }
#endif

                int readLen = ReadIncomingMsgs();

                if (readLen < 0) { shutDown = true; return; }

                int procIndex = 0;

                while (procIndex < readLen)
                {
                    if (readState == ReadState.WaitingForPacketStart)
                    {
                        readIncTotal = tempBytes[procIndex];
                        procIndex++;

                        if (procIndex >= readLen)
                        {
                            readLen = ReadIncomingMsgs();

                            if (readLen < 0) { shutDown = true; return; }

                            procIndex = 0;
                        }

                        readIncTotal |= (tempBytes[procIndex] << 8);
                        procIndex++;

                        if (readIncTotal <= 0) { continue; }

                        readIncOffset = 0;
                        readIncBuf = new byte[readIncTotal];
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
#if SERVER
                if (!((writeStream as AnonymousPipeClientStream)?.IsConnected ?? false))
                {
                    shutDown = true;
                    return;
                }
#else
                if (!((writeStream as AnonymousPipeServerStream)?.IsConnected ?? false))
                {
                    shutDown = true;
                    return;
                }
#endif
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

                    msg = lengthBytes.Concat(msg).ToArray();

                    try
                    {
                        writeStream?.Write(msg, 0, msg.Length);
                    }
                    catch (ObjectDisposedException)
                    {
                        shutDown = true;
                        break;
                    }
                    catch (System.IO.IOException)
                    {
                        shutDown = true;
                        break;
                    }

                    if (shutDown) { break; }

                    lock (msgsToWrite)
                    {
                        msgAvailable = msgsToWrite.TryDequeue(out msg);
                    }
                }
                if (!shutDown)
                {
                    writeManualResetEvent.Reset();
                    if (!writeManualResetEvent.WaitOne(1000))
                    {
                        if (shutDown)
                        {
                            return;
                        }
                        try
                        {
                            //heartbeat to keep the other end alive
                            byte[] lengthBytes = new byte[2];
                            lengthBytes[0] = (byte)0;
                            lengthBytes[1] = (byte)0;
                            writeStream?.Write(lengthBytes, 0, 2);
                        }
                        catch (ObjectDisposedException)
                        {
                            shutDown = true;
                            break;
                        }
                        catch (System.IO.IOException)
                        {
                            shutDown = true;
                            break;
                        }
                    }
                }
            }
        }

        public static void Write(byte[] msg)
        {
            if (shutDown) { return; }

            lock (msgsToWrite)
            {
                msgsToWrite.Enqueue(msg);
                writeManualResetEvent.Set();
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

