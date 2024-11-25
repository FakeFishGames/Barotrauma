using Concentus.Enums;
using Concentus.Structs;

namespace Barotrauma.Networking
{
    static partial class VoipConfig
    {
        public static OpusEncoder CreateEncoder()
        {
            var encoder = new OpusEncoder(FREQUENCY, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_AUTO;
            encoder.Bitrate = BITRATE;
            encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            return encoder;
        }
    }
}
