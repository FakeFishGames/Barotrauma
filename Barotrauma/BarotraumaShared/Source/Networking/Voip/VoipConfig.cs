using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public const int MAX_COMPRESSED_SIZE = 120; //amount of bytes we expect each 60ms of audio to fit in

        public static readonly TimeSpan SEND_INTERVAL = new TimeSpan(0,0,0,0,120);
    }
}
