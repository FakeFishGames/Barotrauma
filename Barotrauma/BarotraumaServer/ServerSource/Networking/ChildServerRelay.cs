using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Barotrauma.Networking
{
    static partial class ChildServerRelay
    {
        public static void Start(string writeHandle, string readHandle)
        {
            var writePipe = new AnonymousPipeClientStream(PipeDirection.Out, writeHandle);
            var readPipe = new AnonymousPipeClientStream(PipeDirection.In, readHandle);

            writeStream = writePipe; readStream = readPipe;

            PrivateStart();
        }

        public static void NotifyCrash(string msg)
        {
            errorsToWrite.Enqueue(msg);
            Thread.Sleep(1000);
        }
        
        public static void ShutDown()
        {
            PrivateShutDown();
        }
    }
}
