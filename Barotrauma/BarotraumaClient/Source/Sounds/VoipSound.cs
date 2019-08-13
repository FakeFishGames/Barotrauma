using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using OpenAL;
using System;
using System.Collections.Generic;

namespace Barotrauma.Sounds
{
    public class VoipSound : Sound
    {
        public override SoundManager.SourcePoolIndex SourcePoolIndex
        {
            get
            {
                return SoundManager.SourcePoolIndex.Voice;
            }
        }

        public new bool IsPlaying
        {
            get
            {
                return soundChannel != null && soundChannel.IsPlaying;
            }
        }

        private VoipQueue queue;
        private int bufferID = 0;
        
        private SoundChannel soundChannel;

        public bool UseRadioFilter;
        public bool UseMuffleFilter;

        public float Near { get; private set; }
        public float Far { get; private set; }

        private static BiQuad[] muffleFilters = new BiQuad[]
        {
            new LowpassFilter(VoipConfig.FREQUENCY, 800)
        };
        private static BiQuad[] radioFilters = new BiQuad[]
        {
            new BandpassFilter(VoipConfig.FREQUENCY, 2000)
        };

        public float Gain
        {
            get { return soundChannel == null ? 0.0f : soundChannel.Gain; }
            set
            {
                if (soundChannel == null) { return; }
                soundChannel.Gain = value;
            }
        }

        public VoipSound(SoundManager owner, VoipQueue q) : base(owner, "voip", true, true)
        {
            VoipConfig.SetupEncoding();

            ALFormat = Al.FormatMono16;
            SampleRate = VoipConfig.FREQUENCY;

            queue = q;
            bufferID = queue.LatestBufferID;

            soundChannel = null;

            SoundChannel chn = new SoundChannel(this, 1.0f, null, 0.4f, 1.0f, "voip", false);
            soundChannel = chn;
        }

        public override float GetAmplitudeAtPlaybackPos(int playbackPos)
        {
            throw new NotImplementedException(); //TODO: implement?
        }

        public void SetPosition(Vector3? pos)
        {
            soundChannel.Position = pos;
        }

        public void SetRange(float near, float far)
        {
            soundChannel.Near = Near = near;
            soundChannel.Far = Far = far;
        }

        public void ApplyFilters(short[] buffer, int readSamples)
        {
            if (UseMuffleFilter)
            {
                ApplyFilters(radioFilters, buffer, readSamples);
            }

            if (UseRadioFilter)
            {
                ApplyFilters(radioFilters, buffer, readSamples);
            }
        }

        private void ApplyFilters(IEnumerable<BiQuad> filters, short[] buffer, int readSamples)
        {
            for (int i = 0; i < readSamples; i++)
            {
                float fVal = ShortToFloat(buffer[i]);
                foreach (var filter in filters)
                {
                    fVal = filter.Process(fVal);
                }
                buffer[i] = FloatToShort(fVal);
            }
        }

        public override SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(Vector3? position, float gain, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(float gain)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play()
        {
            throw new InvalidOperationException();
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            queue.RetrieveBuffer(bufferID, out int compressedSize, out byte[] compressedBuffer);
            if (compressedSize > 0)
            {
                VoipConfig.Decoder.Decode(compressedBuffer, 0, compressedSize, buffer, 0, VoipConfig.BUFFER_SIZE);
                bufferID++;
                return VoipConfig.BUFFER_SIZE * 2;
            }
            if (bufferID < queue.LatestBufferID - (VoipQueue.BUFFER_COUNT - 1)) bufferID = queue.LatestBufferID - (VoipQueue.BUFFER_COUNT - 1);

            return 0;
        }

        public override void Dispose()
        {
            if (soundChannel != null)
            {
                soundChannel.Dispose();
                soundChannel = null;
            }
            base.Dispose();
        }
    }
}
