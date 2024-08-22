using System;
using System.Collections.Generic;
using System.Text;
using Concentus.Structs;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public const int MAX_COMPRESSED_SIZE = 40; //amount of bytes we expect each 20ms of audio to fit in

        public static readonly TimeSpan SEND_INTERVAL = new TimeSpan(0,0,0,0,20);

        public const int FREQUENCY = 48000; //48Khz
        public const int BITRATE = 16000; //16Kbps
        public const int BUFFER_SIZE = (8 * MAX_COMPRESSED_SIZE * FREQUENCY) / BITRATE; //20ms window

        public static OpusDecoder CreateDecoder()
        {
            return new OpusDecoder(FREQUENCY, 1);
        }
    }
}
