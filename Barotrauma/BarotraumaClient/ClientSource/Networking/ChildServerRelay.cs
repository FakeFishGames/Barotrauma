using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;

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
            CrashString = null;
            CrashReportFilePath = null;
            
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
            writePipe?.Dispose(); writePipe = null;
            readPipe?.Dispose(); readPipe = null;
            shutDown = true;
        }

        public static void ShutDown()
        {
            Process?.Kill(); Process = null;
            writePipe = null; readPipe = null;

            PrivateShutDown();
        }

        public static string CrashString { get; private set; }
        public static string CrashReportFilePath { get; private set; }

        public static LocalizedString CrashMessage
            => string.IsNullOrEmpty(CrashReportFilePath)
                ? TextManager.Get("ServerProcessClosed")
                : TextManager.GetWithVariable("ServerProcessCrashed", "[reportfilepath]", CrashReportFilePath);
        
        static partial void HandleCrashString(string str)
        {
            DebugConsole.ThrowError($"The server has crashed: {str}");
            CrashReportFilePath = str.Split("||").FirstOrDefault() ?? "servercrashreport.log";
            CrashString = str;
        }
    }
}
