using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Barotrauma.Extensions;

#if SERVER
using PipeType = System.IO.Pipes.AnonymousPipeClientStream;
#else
using PipeType = System.IO.Pipes.AnonymousPipeServerStream;
#endif

namespace Barotrauma.Networking
{
    static partial class ChildServerRelay
    {
        private static PipeType writeStream;
        private static PipeType readStream;

        private static ManualResetEvent writeManualResetEvent;

        private static volatile bool shutDown;
        public static bool HasShutDown => shutDown;

        private const int ReadBufferSize = MsgConstants.MTU * 2;
        private static byte[] readTempBytes;
        private static int readIncOffset;
        private static int readIncTotal;

        private static ConcurrentQueue<byte[]> msgsToWrite;
        private static ConcurrentQueue<byte[]> msgsToRead;

        private static Thread readThread;
        private static Thread writeThread;

        private static CancellationTokenSource readCancellationToken;

        private static void PrivateStart()
        {
            readIncOffset = 0;
            readIncTotal = 0;

            readTempBytes = new byte[ReadBufferSize];

            msgsToWrite = new ConcurrentQueue<byte[]>();
            msgsToRead = new ConcurrentQueue<byte[]>();

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
            Task<int> readTask = readStream?.ReadAsync(readTempBytes, 0, readTempBytes.Length, readCancellationToken.Token);
            if (readTask is null) { return -1; }

            TimeSpan timeOut = TimeSpan.FromMilliseconds(100);
            for (int i = 0; i < 150; i++)
            {
                if (shutDown)
                {
                    readCancellationToken?.Cancel();
                    return -1;
                }

                if (readTask.IsCompleted || readTask.Wait(timeOut))
                {
                    break;
                }
            }

            if (readTask.Status != TaskStatus.RanToCompletion)
            {
                bool swallowException = shutDown
                    && ((readTask.Exception?.InnerException is ObjectDisposedException)
                        || (readTask.Exception?.InnerException is System.IO.IOException));
                if (swallowException)
                {
                    readCancellationToken?.Cancel();
                    return -1;
                }
                throw new Exception(
                    $"ChildServerRelay readTask did not run to completion: status was {readTask.Status}.",
                    readTask.Exception);
            }

            return readTask.Result;
        }

        private static void CheckPipeConnected(string name, PipeType pipe)
        {
            if (!(pipe is { IsConnected: true }))
            {
                throw new Exception($"{name} was disconnected unexpectedly");
            }
        }

        private static void UpdateRead()
        {
            Span<byte> msgLengthSpan = stackalloc byte[2];
            while (!shutDown)
            {
                CheckPipeConnected(nameof(readStream), readStream);

                bool readBytes(Span<byte> readTo)
                {
                    for (int i = 0; i < readTo.Length; i++)
                    {
                        if (readIncOffset >= readIncTotal)
                        {
                            readIncTotal = ReadIncomingMsgs();
                            readIncOffset = 0;
                            if (readIncTotal == 0) { Thread.Yield(); continue; }
                            if (readIncTotal < 0) { return false; }
                        }
                        readTo[i] = readTempBytes[readIncOffset];
                        readIncOffset++;
                    }
                    return true;
                }

                if (!readBytes(msgLengthSpan)) { shutDown = true; break; }

                int msgLength = msgLengthSpan[0] | (msgLengthSpan[1] << 8);

                if (msgLength > 0)
                {
                    byte[] msg = new byte[msgLength];
                    if (!readBytes(msg.AsSpan())) { shutDown = true; break; }

                    msgsToRead.Enqueue(msg);
                }

                Thread.Yield();
            }
        }

        private static void UpdateWrite()
        {
            while (!shutDown)
            {
                CheckPipeConnected(nameof(writeStream), writeStream);

                bool msgAvailable; byte[] msg;

                void writeMsg()
                {
                    // It's SUPER IMPORTANT that this stack allocation
                    // remains in this local function and is never inlined,
                    // because C# is stupid and only calls for deallocation
                    // when the function returns; placing it in the loop
                    // this method is based around would lead to a stack
                    // overflow real quick!
                    Span<byte> bytesToWrite = stackalloc byte[2 + msg.Length];

                    bytesToWrite[0] = (byte)(msg.Length & 0xFF);
                    bytesToWrite[1] = (byte)((msg.Length >> 8) & 0xFF);
                    Span<byte> msgSlice = bytesToWrite.Slice(2, msg.Length);

                    msg.AsSpan().CopyTo(msgSlice);

                    try
                    {
                        writeStream?.Write(bytesToWrite);
                    }
                    catch (Exception exception)
                    {
                        switch (exception)
                        {
                            case ObjectDisposedException _:
                            case System.IO.IOException _:
                                if (!shutDown) { throw; }
                                break;
                            default:
                                throw;
                        };
                    }
                }

                msgAvailable = msgsToWrite.TryDequeue(out msg);
                while (msgAvailable)
                {
                    writeMsg();

                    if (shutDown) { break; }

                    msgAvailable = msgsToWrite.TryDequeue(out msg);
                }
                if (!shutDown)
                {
                    writeManualResetEvent.Reset();
                    if (!writeManualResetEvent.WaitOne(1000))
                    {
                        if (shutDown) { return; }

                        //heartbeat to keep the other end alive
                        msg = Array.Empty<byte>(); writeMsg();
                    }
                }
            }
        }

        public static void Write(byte[] msg)
        {
            if (shutDown) { return; }

            msgsToWrite.Enqueue(msg);
            writeManualResetEvent.Set();
        }

        public static bool Read(out byte[] msg)
        {
            if (shutDown) { msg = null; return false; }

            return msgsToRead.TryDequeue(out msg);
        }
    }
}

