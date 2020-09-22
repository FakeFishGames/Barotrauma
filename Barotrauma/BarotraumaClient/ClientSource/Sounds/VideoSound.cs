using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;
using System.Threading;
using Barotrauma.Media;

namespace Barotrauma.Sounds
{
    public class VideoSound : Sound
    {
        private readonly object mutex;
        private Queue<short[]> sampleQueue;

        private SoundChannel soundChannel;
        private Video video;

        public VideoSound(SoundManager owner, string filename, int sampleRate, int channelCount, Video vid) : base(owner, filename, true, false)
        {
            ALFormat = channelCount == 2 ? Al.FormatStereo16 : Al.FormatMono16;
            SampleRate = sampleRate;

            sampleQueue = new Queue<short[]>();
            mutex = new object();

            soundChannel = null;

            video = vid;
        }

        public override float GetAmplitudeAtPlaybackPos(int playbackPos)
        {
            throw new NotImplementedException();
        }

        public override bool IsPlaying()
        {
            bool retVal = false;
            lock (mutex)
            {
                retVal = soundChannel != null && soundChannel.IsPlaying;
            }
            return retVal;
        }

        public void Enqueue(short[] buf)
        {
            lock (mutex)
            {
                sampleQueue.Enqueue(buf);
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
            SoundChannel chn = null;
            lock (mutex)
            {
                if (soundChannel != null)
                {
                    soundChannel.Dispose();
                    soundChannel = null;
                }
            }
            chn = new SoundChannel(this, gain, null, 1.0f, 1.0f, 3.0f, "video", false);
            lock (mutex)
            {
                soundChannel = chn;
            }
            return chn;
        }

        public override SoundChannel Play()
        {
            return Play(BaseGain);
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!video.IsPlaying) return -1;

            short[] buf;
            int readAmount = 0;
            lock (mutex)
            {
                while (readAmount<buffer.Length)
                {
                    if (sampleQueue.Count == 0) break;
                    buf = sampleQueue.Peek();
                    if (readAmount + buf.Length >= buffer.Length) break;
                    buf = sampleQueue.Dequeue();
                    buf.CopyTo(buffer, readAmount);
                    readAmount += buf.Length;
                }
            }
            return readAmount*2;
        }

        public override void Dispose()
        {
            lock (mutex)
            {
                if (soundChannel != null)
                {
                    soundChannel.Dispose();
                }
                base.Dispose();
            }
        }
    }
}
