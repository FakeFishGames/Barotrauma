using Concentus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public static bool Ready = false;

        public const int FREQUENCY = 48000; //not amazing, but not bad audio quality
        public const int BUFFER_SIZE = 2880; //60ms window, the max Opus seems to support
        
        public static OpusEncoder Encoder
        {
            get;
            private set;
        }
        public static OpusDecoder Decoder
        {
            get;
            private set;
        }

        public static void SetupEncoding()
        {
            if (!Ready)
            {
                Encoder = new OpusEncoder(FREQUENCY, 1, OpusApplication.OPUS_APPLICATION_VOIP);
                Encoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_AUTO;
                Encoder.Bitrate = 8 * MAX_COMPRESSED_SIZE * FREQUENCY / BUFFER_SIZE;
                Encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

                Decoder = new OpusDecoder(FREQUENCY, 1);

                Ready = true;
            }
        }
    }
}
