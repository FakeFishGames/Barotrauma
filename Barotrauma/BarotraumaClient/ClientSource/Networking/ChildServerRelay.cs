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
        public static Process Process;
        private static bool localHandlesDisposed;
        private static AnonymousPipeServerStream writePipe;
        private static AnonymousPipeServerStream readPipe;

        public static void Start(ProcessStartInfo processInfo)
        {
            writePipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            readPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            writeStream = writePipe; readStream = readPipe;

            PrivateStart();

            processInfo.Arguments += " -pipes " + writePipe.GetClientHandleAsString() + " " + readPipe.GetClientHandleAsString();
            DebugConsole.NewMessage(processInfo.Arguments, Microsoft.Xna.Framework.Color.Orange);
            Process = Process.Start(processInfo);

            localHandlesDisposed = false;
        }

        public static void DisposeLocalHandles()
        {
            if (localHandlesDisposed) { return; }
            writePipe.DisposeLocalCopyOfClientHandle(); readPipe.DisposeLocalCopyOfClientHandle();
            localHandlesDisposed = true;
        }

        public static void ShutDown()
        {
            Process?.Kill(); Process = null;
            writePipe = null; readPipe = null;

            PrivateShutDown();
        }
    }
}
