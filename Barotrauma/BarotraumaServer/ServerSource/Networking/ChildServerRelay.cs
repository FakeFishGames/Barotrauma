using System.IO.Pipes;

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

        public static void ShutDown()
        {
            PrivateShutDown();
        }
    }
}
