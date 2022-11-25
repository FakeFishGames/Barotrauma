using Barotrauma.IO;
using Barotrauma.Networking;
using Concentus.Structs;
using Microsoft.Xna.Framework;
using OpenAL;
using System;
using System.Collections.Generic;

namespace Barotrauma.Sounds
{
    class VoipSound : Sound
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

        private OpusDecoder decoder;

        public bool UseRadioFilter;
        public bool UseMuffleFilter;

        public float Near { get; private set; }
        public float Far { get; private set; }

        private BiQuad[] muffleFilters = new BiQuad[]
        {
            new LowpassFilter(VoipConfig.FREQUENCY, 800)
        };
        private BiQuad[] radioFilters = new BiQuad[]
        {
            new BandpassFilter(VoipConfig.FREQUENCY, 2000)
        };
        private const float PostRadioFilterBoost = 1.2f;

        private float gain;
        public float Gain
        {
            get { return soundChannel == null ? 0.0f : gain; }
            set
            {
                if (soundChannel == null) { return; }
                gain = value;
                soundChannel.Gain = value * GameSettings.CurrentConfig.Audio.VoiceChatVolume;
            }
        }

        public float CurrentAmplitude
        {
            get { return soundChannel?.CurrentAmplitude ?? 0.0f; }
        }

        public VoipSound(string name, SoundManager owner, VoipQueue q) : base(owner, $"VoIP ({name})", true, true, getFullPath: false)
        {
            decoder = VoipConfig.CreateDecoder();

            ALFormat = Al.FormatMono16;
            SampleRate = VoipConfig.FREQUENCY;

            queue = q;
            bufferID = queue.LatestBufferID;

            soundChannel = null;

            SoundChannel chn = new SoundChannel(this, 1.0f, null, 1.0f, 0.4f, 1.0f, "voip", false);
            soundChannel = chn;
            Gain = 1.0f;
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
            for (int i = 0; i < readSamples; i++)
            {
                float fVal = ShortToFloat(buffer[i]);

                if (gain * GameSettings.CurrentConfig.Audio.VoiceChatVolume > 1.0f) //TODO: take distance into account?
                {
                    fVal = Math.Clamp(fVal * gain * GameSettings.CurrentConfig.Audio.VoiceChatVolume, -1f, 1f);
                }

                if (UseMuffleFilter)
                {                
                    foreach (var filter in muffleFilters)
                    {
                        fVal = filter.Process(fVal);
                    }
                }
                if (UseRadioFilter)
                {
                    foreach (var filter in radioFilters)
                    {
                        fVal = Math.Clamp(filter.Process(fVal) * PostRadioFilterBoost, -1f, 1f);
                    }
                }
                buffer[i] = FloatToShort(fVal);
            }
        }

        public override SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(Vector3? position, float gain, float freqMult = 1.0f, bool muffle = false)
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
            try
            {
                if (compressedSize > 0)
                {
                    decoder.Decode(compressedBuffer, 0, compressedSize, buffer, 0, VoipConfig.BUFFER_SIZE);
                    bufferID++;
                    return VoipConfig.BUFFER_SIZE;
                }
                if (bufferID < queue.LatestBufferID - (VoipQueue.BUFFER_COUNT - 1)) bufferID = queue.LatestBufferID - (VoipQueue.BUFFER_COUNT - 1);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to decode Opus buffer (buffer size {compressedBuffer.Length}, packet size {compressedSize})", e);
                bufferID = queue.LatestBufferID - (VoipQueue.BUFFER_COUNT - 1);
            }

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
