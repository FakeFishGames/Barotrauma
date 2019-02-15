using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using OpenTK.Audio.OpenAL;
using System;

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
        public int bufferID = 0;
        
        private SoundChannel soundChannel;

        public VoipSound(SoundManager owner, VoipQueue q) : base(owner, "voip", true, true)
        {
            VoipConfig.SetupEncoding();

            ALFormat = ALFormat.Mono16;
            SampleRate = VoipConfig.FREQUENCY;

            queue = q;
            bufferID = queue.LatestBufferID;

            soundChannel = null;

            SoundChannel chn = new SoundChannel(this, 1.0f, null, 0.4f, 1.0f, "voip", false);
            soundChannel = chn;
        }

        public void SetPosition(Vector3? pos)
        {
            soundChannel.Near = 300.0f;
            soundChannel.Far = 750.0f;
            soundChannel.Position = pos;
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
