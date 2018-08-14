using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public const int MAX_COMPRESSED_SIZE = 120; //amount of bytes we expect each 60ms of audio to fit in

        public const int SEND_INTERVAL_MS = 120;
    }
}
