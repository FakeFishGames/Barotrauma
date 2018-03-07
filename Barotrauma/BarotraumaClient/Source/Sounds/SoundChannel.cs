using System;
using OpenTK.Audio.OpenAL;

namespace Barotrauma
{
    class SoundChannel : IDisposable
    {
        private const int STREAM_BUFFER_SIZE = 65536;

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
        private int[] streamBuffers;

        public bool IsPlaying
        {
            get
            {
                if (ALSourceIndex < 0) return false;
                if (IsStream && !reachedEndSample) return true;
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
                else
                {
                    streamBuffers = new int[4];
                    for (int i=0;i<4;i++)
                    {
                        streamBuffers[i] = AL.GenBuffer();

                        ALError alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to generate stream buffers: " + AL.GetErrorString(alError));
                        }
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
                uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);

                bool playing = AL.GetSourceState(alSource) == ALSourceState.Playing;
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to determine playing state from streamed source: "+AL.GetErrorString(alError));
                }

                int buffersToUnqueue = 0;
                int[] unqueuedBuffers = null;
                if (streamSeekPos > 0)
                {
                    buffersToUnqueue = 0;
                    AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out buffersToUnqueue);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to determine processed buffers from streamed source: " + AL.GetErrorString(alError));
                    }

                    unqueuedBuffers = new int[buffersToUnqueue];
                    AL.SourceUnqueueBuffers((int)alSource, buffersToUnqueue, unqueuedBuffers);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to unqueue buffers from streamed source: " + AL.GetErrorString(alError));
                    }
                }
                else
                {
                    buffersToUnqueue = 4;
                    unqueuedBuffers = (int[])streamBuffers.Clone();
                }

                for (int i = 0; i < buffersToUnqueue; i++)
                {
                    short[] buffer = new short[STREAM_BUFFER_SIZE];
                    int readSamples = Sound.FillStreamBuffer(streamSeekPos, buffer);
                    streamSeekPos += readSamples;
                    if (readSamples < STREAM_BUFFER_SIZE)
                    {
                        reachedEndSample = true;
                    }
                    if (readSamples > 0)
                    {
                        AL.BufferData<short>(unqueuedBuffers[i], Sound.ALFormat, buffer, readSamples, Sound.SampleRate);

                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to assign data to stream buffer: " + AL.GetErrorString(alError));
                        }

                        AL.SourceQueueBuffer((int)alSource, unqueuedBuffers[i]);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to queue buffer["+i.ToString()+"] to stream: " + AL.GetErrorString(alError));
                        }
                    }
                }

                if (AL.GetSourceState(alSource) != ALSourceState.Playing)
                {
                    AL.SourcePlay(alSource);
                }
            }
        }
    }
}
