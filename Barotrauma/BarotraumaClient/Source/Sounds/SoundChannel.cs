using System;
using OpenTK.Audio.OpenAL;

namespace Barotrauma
{
    class SoundChannel : IDisposable
    {
        public Sound Sound
        {
            get;
            private set;
        }

        public int ALSourceIndex
        {
            get;
            private set;
        }

        public bool IsStream
        {
            get;
            private set;
        }

        private int streamSeekPos;
        private bool reachedEndSample;
        public bool IsPlaying
        {
            get
            {
                return AL.GetSourceState(ALSourceIndex) == ALSourceState.Playing;
            }
        }

        public SoundChannel(Sound sound)
        {
            Sound = sound;

            IsStream = sound.Stream;

            streamSeekPos = 0; reachedEndSample = false;

            ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(this);

            if (ALSourceIndex!=-1)
            {
                if (!IsStream)
                {
                    AL.BindBufferToSource(sound.Owner.GetSourceFromIndex(ALSourceIndex), (uint)sound.ALBuffer);
                }
            }
        }

        public void Dispose()
        {
            if (ALSourceIndex != -1)
            {
                AL.SourceStop(Sound.Owner.GetSourceFromIndex(ALSourceIndex));
                ALSourceIndex = -1;
            }
        }

        public void UpdateStream()
        {
            if (!IsStream) throw new Exception("Called UpdateStream on a non-streamed sound channel!");

            if (!reachedEndSample)
            {
                short[] buffer = new short[8192];
                int readSamples = Sound.FillStreamBuffer(streamSeekPos, buffer);
                if (readSamples < 8192)
                {
                    reachedEndSample = true;
                }
            }
        }
    }
}
