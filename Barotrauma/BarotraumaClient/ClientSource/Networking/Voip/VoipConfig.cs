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
        public const int FREQUENCY = 48000; //48Khz
        public const int BITRATE = 16000; //16Kbps
        public const int BUFFER_SIZE = (8 * MAX_COMPRESSED_SIZE * FREQUENCY) / BITRATE; //20ms window

        public static OpusEncoder CreateEncoder()
        {
            var encoder = new OpusEncoder(FREQUENCY, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_AUTO;
            encoder.Bitrate = BITRATE;
            encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            return encoder;
        }
        
        public static OpusDecoder CreateDecoder()
        {
            return new OpusDecoder(FREQUENCY, 1);
        }
    }
}
