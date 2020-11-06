using System.Diagnostics;
using System.IO.Pipes;

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
            try
            {
                Process = Process.Start(processInfo);
            }
            catch
            {
                DebugConsole.ThrowError($"Failed to start ChildServerRelay Process. File: {processInfo.FileName}, arguments: {processInfo.Arguments}");
                throw;
            }

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
