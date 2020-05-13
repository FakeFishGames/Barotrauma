using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    static partial class ChildServerRelay
    {
        public static Process Process;
        private static bool localHandlesDisposed;
        private static AnonymousPipeServerStream writePipe;
        private static AnonymousPipeServerStream readPipe;

        public static void Start(ProcessStartInfo processInfo)
        {
            writePipe = new AnonymousPipeServerStream(PipeDirection.Out, System.IO.HandleInheritability.Inheritable);
            readPipe = new AnonymousPipeServerStream(PipeDirection.In, System.IO.HandleInheritability.Inheritable);

            writeStream = writePipe; readStream = readPipe;

            PrivateStart();

            processInfo.Arguments += " -pipes " + writePipe.GetClientHandleAsString() + " " + readPipe.GetClientHandleAsString();
            Process = Process.Start(processInfo);

            localHandlesDisposed = false;
        }

        public static void DisposeLocalHandles()
        {
            if (localHandlesDisposed) { return; }
            writePipe.DisposeLocalCopyOfClientHandle(); readPipe.DisposeLocalCopyOfClientHandle();
            localHandlesDisposed = true;
        }

        public static void ClosePipes()
        {
            writePipe?.Close();
            readPipe?.Close(); 
            shutDown = true;
        }

        public static void ShutDown()
        {
            Process?.Kill(); Process = null;
            writePipe = null; readPipe = null;

            PrivateShutDown();
        }
    }
}
