using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

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
