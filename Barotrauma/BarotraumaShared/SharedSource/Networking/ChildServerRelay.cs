using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
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

        private enum WriteStatus : byte
        {
            Success = 0x00,
            Heartbeat = 0x01,
            RequestShutdown = 0xCC,
            Crash = 0xFF
        }

        private static ManualResetEvent writeManualResetEvent;

        private enum StatusEnum
        {
            NeverStarted,
            Active,
            RequestedShutDown,
            ShutDown
        }
        
        private static volatile StatusEnum status = StatusEnum.NeverStarted;
        public static bool HasShutDown => status is StatusEnum.ShutDown;

        private const int ReadBufferSize = MsgConstants.MTU * 2;
        private static byte[] readTempBytes;
        private static int readIncOffset;
        private static int readIncTotal;

        private static ConcurrentQueue<byte[]> msgsToWrite;
        private static ConcurrentQueue<string> errorsToWrite;

        private static ConcurrentQueue<byte[]> msgsToRead;

        private static Thread readThread;
        private static Thread writeThread;

        private static CancellationTokenSource readCancellationToken;

        private static void PrivateStart()
        {
            status = StatusEnum.Active;

            readIncOffset = 0;
            readIncTotal = 0;

            readTempBytes = new byte[ReadBufferSize];

            msgsToWrite = new ConcurrentQueue<byte[]>();
            errorsToWrite = new ConcurrentQueue<string>();
            
            msgsToRead = new ConcurrentQueue<byte[]>();

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
            if (Thread.CurrentThread != GameMain.MainThread)
            {
                throw new InvalidOperationException(
                    $"Cannot call {nameof(ChildServerRelay)}.{nameof(PrivateShutDown)} from a thread other than the main one");
            }
            if (status is StatusEnum.NeverStarted) { return; }
            status = StatusEnum.ShutDown;
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


        private static Option<int> ReadIncomingMsgs()
        {
            Task<int> readTask = readStream?.ReadAsync(readTempBytes, 0, readTempBytes.Length, readCancellationToken.Token);
            if (readTask is null) { return Option<int>.None(); }

            int timeOutMilliseconds = 100;
            for (int i = 0; i < 150; i++)
            {
                if (status is StatusEnum.ShutDown)
                {
                    readCancellationToken?.Cancel();
                    return Option<int>.None();
                }
                try
                {
                    if (readTask.IsCompleted || readTask.Wait(timeOutMilliseconds, readCancellationToken.Token))
                    {
                        break;
                    }
                }
                catch (AggregateException aggregateException)
                {
                    if (aggregateException.InnerException is OperationCanceledException) { return Option<int>.None(); }
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return Option<int>.None();
                }
            }

            if (readTask.Status == TaskStatus.RanToCompletion)
            {
                return Option<int>.Some(readTask.Result);
            }

            bool swallowException =
                status is not StatusEnum.Active
                && readTask.Exception?.InnerException is ObjectDisposedException or System.IO.IOException;
            if (swallowException)
            {
                readCancellationToken?.Cancel();
                return Option<int>.None();
            }
            throw new Exception(
                $"ChildServerRelay readTask did not run to completion: status was {readTask.Status}.",
                readTask.Exception);
        }

        private static void CheckPipeConnected(string name, PipeType pipe)
        {
            if (status is StatusEnum.Active && pipe is not { IsConnected: true })
            {
                throw new Exception($"{name} was disconnected unexpectedly");
            }
        }

        static partial void HandleCrashString(string str);
        
        private static void UpdateRead()
        {
            Span<byte> msgLengthSpan = stackalloc byte[4 + 1];
            while (!HasShutDown)
            {
                CheckPipeConnected(nameof(readStream), readStream);

                bool readBytes(Span<byte> readTo)
                {
                    for (int i = 0; i < readTo.Length; i++)
                    {
                        if (readIncOffset >= readIncTotal)
                        {
                            if (!ReadIncomingMsgs().TryUnwrap(out readIncTotal)) { return false; }
                            readIncOffset = 0;
                            if (readIncTotal == 0) { Thread.Yield(); continue; }
                        }
                        readTo[i] = readTempBytes[readIncOffset];
                        readIncOffset++;
                    }
                    return true;
                }

                if (!readBytes(msgLengthSpan)) { status = StatusEnum.ShutDown; break; }

                int msgLength = msgLengthSpan[0]
                                | (msgLengthSpan[1] << 8)
                                | (msgLengthSpan[2] << 16)
                                | (msgLengthSpan[3] << 24);
                WriteStatus writeStatus = (WriteStatus)msgLengthSpan[4];

                byte[] msg = msgLength > 0 ? new byte[msgLength] : Array.Empty<byte>();
                if (msg.Length > 0 && !readBytes(msg.AsSpan())) { status = StatusEnum.ShutDown; break; }

                switch (writeStatus)
                {
                    case WriteStatus.Success:
                        msgsToRead.Enqueue(msg);
                        break;
                    case WriteStatus.Heartbeat:
                        //do nothing
                        break;
                    case WriteStatus.RequestShutdown:
                        status = StatusEnum.ShutDown;
                        break;
                    case WriteStatus.Crash:
                        HandleCrashString(Encoding.UTF8.GetString(msg));
                        status = StatusEnum.ShutDown;
                        break;
                }

                Thread.Yield();
            }
        }

        private static void UpdateWrite()
        {
            while (!HasShutDown)
            {
                CheckPipeConnected(nameof(writeStream), writeStream);

                void writeMsg(WriteStatus writeStatus, byte[] msg)
                {
                    // It's SUPER IMPORTANT that this stack allocation
                    // remains in this local function and is never inlined,
                    // because C# is stupid and only calls for deallocation
                    // when the function returns; placing it in the loop
                    // this method is based around would lead to a stack
                    // overflow real quick!
                    Span<byte> headerBytes = stackalloc byte[4 + 1];

                    headerBytes[0] = (byte)(msg.Length & 0xFF);
                    headerBytes[1] = (byte)((msg.Length >> 8) & 0xFF);
                    headerBytes[2] = (byte)((msg.Length >> 16) & 0xFF);
                    headerBytes[3] = (byte)((msg.Length >> 24) & 0xFF);
                    
                    headerBytes[4] = (byte)writeStatus;

                    try
                    {
                        writeStream?.Write(headerBytes);
                        writeStream?.Write(msg);
                    }
                    catch (Exception exception)
                    {
                        switch (exception)
                        {
                            case ObjectDisposedException _:
                            case System.IO.IOException _:
                                if (!HasShutDown) { throw; }
                                break;
                            default:
                                throw;
                        };
                    }
                }

                if (status is StatusEnum.RequestedShutDown)
                {
                    writeMsg(WriteStatus.RequestShutdown, Array.Empty<byte>());
                    status = StatusEnum.ShutDown;
                }
                
                while (errorsToWrite.TryDequeue(out var error))
                {
                    writeMsg(WriteStatus.Crash, Encoding.UTF8.GetBytes(error));
                    status = StatusEnum.ShutDown;
                }
                
                while (msgsToWrite.TryDequeue(out var msg))
                {
                    writeMsg(WriteStatus.Success, msg);

                    if (HasShutDown) { break; }
                }
                
                if (!HasShutDown)
                {
                    writeManualResetEvent.Reset();
                    if (!writeManualResetEvent.WaitOne(1000))
                    {
                        if (HasShutDown) { return; }

                        //heartbeat to keep the other end alive
                        writeMsg(WriteStatus.Heartbeat, Array.Empty<byte>());
                    }
                }
            }
        }

        public static void Write(byte[] msg)
        {
            if (HasShutDown) { return; }

            if (msg.Length > 0x1fff_ffff)
            {
                //This message is extremely long and is close to breaking
                //ChildServerRelay, so let's not allow this to go through!
                return;
            }
            msgsToWrite.Enqueue(msg);
            writeManualResetEvent.Set();
        }

        public static bool Read(out byte[] msg)
        {
            if (HasShutDown) { msg = null; return false; }

            return msgsToRead.TryDequeue(out msg);
        }
    }
}

