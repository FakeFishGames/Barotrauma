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
                if (ALSourceIndex < 0) return false;
                bool playing = AL.GetSourceState(Sound.Owner.GetSourceFromIndex(ALSourceIndex)) == ALSourceState.Playing;
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to determine playing state from source: "+AL.GetErrorString(alError));
                }
                return playing;
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
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source: " + AL.GetErrorString(alError));
                    }

                    AL.SourcePlay(sound.Owner.GetSourceFromIndex(ALSourceIndex));
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to play source: " + AL.GetErrorString(alError));
                    }
                }
            }
        }

        public void Dispose()
        {
            if (ALSourceIndex != -1)
            {
                AL.SourceStop(Sound.Owner.GetSourceFromIndex(ALSourceIndex));
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to stop source: " + AL.GetErrorString(alError));
                }
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
                streamSeekPos += readSamples;
                if (readSamples < 8192)
                {
                    reachedEndSample = true;
                }
            }
        }
    }
}
