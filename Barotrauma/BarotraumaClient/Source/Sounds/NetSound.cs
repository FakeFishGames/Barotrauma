using Barotrauma;
using Barotrauma.Networking;
using Concentus.Structs;
using Concentus.Enums;

namespace Barotrauma.Sounds
{
    public class NetSound : Sound
    {
        private const int SAMPLE_FREQUENCY = 32000;
        private const int BITRATE = 16000;

        OpusDecoder opusDecoder;
        //OpusEncoder opusEncoder;

        public NetSound(SoundManager owner, Client client) : base(owner, client.Name, true)
        {
            this.client = client;

            opusDecoder = new OpusDecoder(SAMPLE_FREQUENCY, 1);
            /*opusEncoder = new OpusEncoder(SAMPLE_FREQUENCY, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            opusEncoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_AUTO;
            opusEncoder.Bitrate = BITRATE;
            opusEncoder.ForceMode = OpusMode.MODE_SILK_ONLY;
            opusEncoder.UseDTX = true;
            opusEncoder.UseInbandFEC = true;*/
        }

        private Client client;
    }
}
