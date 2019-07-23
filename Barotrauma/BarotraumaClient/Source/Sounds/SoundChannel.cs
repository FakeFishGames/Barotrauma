using System;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Sounds
{
    public class SoundSourcePool : IDisposable
    {
        public uint[] ALSources
        {
            get;
            private set;
        }

        public SoundSourcePool(int sourceCount = SoundManager.SOURCE_COUNT)
        {
            int alError = Al.NoError;

            ALSources = new uint[sourceCount];
            for (int i = 0; i < sourceCount; i++)
            {
                Al.GenSource(out ALSources[i]);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Error generating alSource[" + i.ToString() + "]: " + Al.GetErrorString(alError));
                }

                if (!Al.IsSource(ALSources[i]))
                {
                    throw new Exception("Generated alSource[" + i.ToString() + "] is invalid!");
                }

                Al.SourceStop(ALSources[i]);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Error stopping newly generated alSource[" + i.ToString() + "]: " + Al.GetErrorString(alError));
                }

                Al.Sourcef(ALSources[i], Al.MinGain, 0.0f);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Error setting min gain: " + Al.GetErrorString(alError));
                }

                Al.Sourcef(ALSources[i], Al.MaxGain, 1.0f);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Error setting max gain: " + Al.GetErrorString(alError));
                }

                Al.Sourcef(ALSources[i], Al.RolloffFactor, 1.0f);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Error setting rolloff factor: " + Al.GetErrorString(alError));
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < ALSources.Length; i++)
            {
                Al.DeleteSource(ALSources[i]);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to delete ALSources[" + i.ToString() + "]: " + Al.GetErrorString(alError));
                }
            }
            ALSources = null;
        }
    }

    public class SoundChannel : IDisposable
    {
        private const int STREAM_BUFFER_SIZE = 65536;
        private short[] streamShortBuffer;

        private Vector3? position;
        public Vector3? Position
        {
            get { return position; }
            set
            {
                position = value;

                if (ALSourceIndex < 0) return;

                if (position != null)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.SourceRelative, Al.False);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to enable source's relative flag: " + Al.GetErrorString(alError));
                    }

                    Al.Source3f(alSource, Al.Position, position.Value.X, position.Value.Y, position.Value.Z);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to set source's position: " + Al.GetErrorString(alError));
                    }
                }
                else
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.SourceRelative, Al.True);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to disable source's relative flag: " + Al.GetErrorString(alError));
                    }

                    Al.Source3f(alSource, Al.Position, 0.0f, 0.0f, 0.0f);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset source's position: " + Al.GetErrorString(alError));
                    }
                }
            }
        }

        private float near;
        public float Near
        {
            get { return near; }
            set
            {
                near = value;

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                Al.Sourcef(alSource, Al.ReferenceDistance, near);
                
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's reference distance: " + Al.GetErrorString(alError));
                }
            }
        }

        private float far;
        public float Far
        {
            get { return far; }
            set
            {
                far = value;

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                Al.Sourcef(alSource, Al.MaxDistance, far);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's max distance: " + Al.GetErrorString(alError));
                }
            }
        }

        private float gain;
        public float Gain
        {
            get { return gain; }
            set
            {
                gain = Math.Max(Math.Min(value, 1.0f), 0.0f);

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                float effectiveGain = gain;
                if (category != null) effectiveGain *= Sound.Owner.GetCategoryGainMultiplier(category);

                Al.Sourcef(alSource, Al.Gain, effectiveGain);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's gain: " + Al.GetErrorString(alError));
                }
            }
        }

        private bool looping;
        public bool Looping
        {
            get { return looping; }
            set
            {
                looping = value;

                if (ALSourceIndex < 0) return;

                if (!IsStream)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.Looping, looping ? Al.True : Al.False);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to set source's looping state: " + Al.GetErrorString(alError));
                    }
                }
            }
        }

        public bool FilledByNetwork
        {
            get;
            private set;
        }
        
        private int decayTimer;
        
        private bool muffled;
        public bool Muffled
        {
            get { return muffled; }
            set
            {
                if (muffled == value) return;

                muffled = value;

                if (ALSourceIndex < 0) return;

                if (!IsPlaying) return;

                if (!IsStream)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    int playbackPos; Al.GetSourcei(alSource, Al.SampleOffset, out playbackPos);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to get source's playback position: " + Al.GetErrorString(alError));
                    }

                    Al.SourceStop(alSource);

                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to stop source: " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(alSource, Al.Buffer, muffled ? (int)Sound.ALMuffledBuffer : (int)Sound.ALBuffer);

                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source: " + Al.GetErrorString(alError));
                    }

                    Al.SourcePlay(alSource);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to replay source: " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(alSource, Al.SampleOffset, playbackPos);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset playback position: " + Al.GetErrorString(alError));
                    }
                }
            }
        }

        private string category;
        public string Category
        {
            get { return category; }
            set
            {
                category = value;
                Gain = gain;
            }
        }

        public Sound Sound
        {
            get;
            private set;
        }

        public int ALSourceIndex
        {
            get;
            private set;
        } = -1;

        public bool IsStream
        {
            get;
            private set;
        }
        private int streamSeekPos;
        private bool startedPlaying;
        private bool reachedEndSample;
        private readonly uint[] streamBuffers;
        private readonly List<uint> emptyBuffers;

        private object mutex;

        public bool IsPlaying
        {
            get
            {
                if (ALSourceIndex < 0) return false;
                if (IsStream && !reachedEndSample) return true;
                int state;
                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                if (!Al.IsSource(alSource)) return false;
                Al.GetSourcei(alSource, Al.SourceState, out state);
                bool playing = state == Al.Playing;
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to determine playing state from source: " + Al.GetErrorString(alError));
                }
                return playing;
            }
        }

        public SoundChannel(Sound sound, float gain, Vector3? position, float near, float far, string category, bool muffle = false)
        {
            Sound = sound;

            IsStream = sound.Stream;
            FilledByNetwork = sound is VoipSound;
            decayTimer = 0;
            streamSeekPos = 0; reachedEndSample = false;
            startedPlaying = true;
            
            mutex = new object();

            lock (mutex)
            {
                if (sound.Owner.CountPlayingInstances(sound) < sound.MaxSimultaneousInstances)
                {
                    ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(this);
                }

                if (ALSourceIndex >= 0)
                {
                    if (!IsStream)
                    {
                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Buffer, 0);
                        int alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to reset source buffer: " + Al.GetErrorString(alError));
                        }

                        if (!Al.IsBuffer(sound.ALBuffer))
                        {
                            throw new Exception(sound.Filename + " has an invalid buffer!");
                        }

                        uint alBuffer = sound.Owner.GetCategoryMuffle(category) || muffle ? sound.ALMuffledBuffer : sound.ALBuffer;
                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Buffer, (int)alBuffer);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to bind buffer to source (" + ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex) + "," + sound.ALBuffer.ToString() + "): " + Al.GetErrorString(alError));
                        }

                        Al.SourcePlay(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to play source: " + Al.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Buffer, (int)sound.ALBuffer);
                        int alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to reset source buffer: " + Al.GetErrorString(alError));
                        }

                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Looping, Al.False);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to set stream looping state: " + Al.GetErrorString(alError));
                        }

                        streamShortBuffer = new short[STREAM_BUFFER_SIZE];

                        streamBuffers = new uint[4];
                        emptyBuffers = new List<uint>();
                        for (int i = 0; i < 4; i++)
                        {
                            Al.GenBuffer(out streamBuffers[i]);

                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to generate stream buffers: " + Al.GetErrorString(alError));
                            }

                            if (!Al.IsBuffer(streamBuffers[i]))
                            {
                                throw new Exception("Generated streamBuffer[" + i.ToString() + "] is invalid!");
                            }
                        }

                        Sound.Owner.InitStreamThread();
                    }
                }
                
                this.Position = position;
                this.Gain = gain;
                this.Looping = false;
                this.Near = near;
                this.Far = far;
                this.Category = category;
            }            
        }

        public bool FadingOutAndDisposing;
        public void FadeOutAndDispose()
        {
            FadingOutAndDisposing = true;
        }

        public void Dispose()
        {
            lock (mutex)
            {
                if (ALSourceIndex >= 0)
                {
                    Al.SourceStop(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to stop source: " + Al.GetErrorString(alError));
                    }
                
                    if (IsStream)
                    {
                        uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                        Al.SourceStop(alSource);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to stop streamed source: " + Al.GetErrorString(alError));
                        }

                        int buffersToUnqueue = 0;
                        uint[] unqueuedBuffers = null;

                        buffersToUnqueue = 0;
                        Al.GetSourcei(alSource, Al.BuffersProcessed, out buffersToUnqueue);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + Al.GetErrorString(alError));
                        }

                        unqueuedBuffers = new uint[buffersToUnqueue];
                        Al.SourceUnqueueBuffers(alSource, buffersToUnqueue, unqueuedBuffers);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + Al.GetErrorString(alError));
                        }
                        
                        Al.Sourcei(alSource, Al.Buffer, 0);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to reset buffer for streamed source: " + Al.GetErrorString(alError));
                        }
                        
                        for (int i = 0; i < 4; i++)
                        {
                            Al.DeleteBuffer(streamBuffers[i]);
                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to delete streamBuffers[" + i.ToString() + "] ("+streamBuffers[i].ToString()+"): " + Al.GetErrorString(alError));
                            }
                        }

                        reachedEndSample = true;
                    }
                    else
                    {
                        Al.Sourcei(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Buffer, 0);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to unbind buffer to non-streamed source: " + Al.GetErrorString(alError));
                        }
                    }

                    ALSourceIndex = -1;
                }
            }
        }

        public void UpdateStream()
        {
            if (!IsStream) throw new Exception("Called UpdateStream on a non-streamed sound channel!");

            lock (mutex)
            {
                if (!reachedEndSample)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                    int state;
                    Al.GetSourcei(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.SourceState, out state);
                    bool playing = state == Al.Playing;
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to determine playing state from streamed source: " + Al.GetErrorString(alError));
                    }
                    
                    int buffersToUnqueue = 0;
                    uint[] unqueuedBuffers = null;
                    if (!startedPlaying)
                    {
                        buffersToUnqueue = 0;
                        Al.GetSourcei(alSource, Al.BuffersProcessed, out buffersToUnqueue);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + Al.GetErrorString(alError));
                        }

                        unqueuedBuffers = new uint[buffersToUnqueue+emptyBuffers.Count];
                        Al.SourceUnqueueBuffers(alSource, buffersToUnqueue, unqueuedBuffers);
                        for (int i = 0; i < emptyBuffers.Count; i++)
                        {
                            unqueuedBuffers[buffersToUnqueue + i] = emptyBuffers[i];
                        }
                        buffersToUnqueue += emptyBuffers.Count;
                        emptyBuffers.Clear();
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + Al.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        startedPlaying = false;
                        buffersToUnqueue = 4;
                        unqueuedBuffers = new uint[4];
                        for (int i = 0; i < 4; i++)
                        {
                            unqueuedBuffers[i] = streamBuffers[i];
                        }
                    }
                    
                    for (int i = 0; i < buffersToUnqueue; i++)
                    {
                        short[] buffer = streamShortBuffer;
                        int readSamples = Sound.FillStreamBuffer(streamSeekPos, buffer);
                        if (FilledByNetwork)
                        {
                            if (Sound is VoipSound voipSound)
                            {
                                voipSound.ApplyFilters(buffer, readSamples);
                            }

                            if (readSamples <= 0)
                            {
                                decayTimer++;
                                if (decayTimer > 120) //TODO: replace magic number
                                {
                                    reachedEndSample = true;
                                }
                            }
                            else
                            {
                                decayTimer = 0;
                            }
                        }
                        else if (Sound.StreamsReliably)
                        {
                            streamSeekPos += readSamples;
                            if (readSamples < STREAM_BUFFER_SIZE)
                            {
                                if (looping)
                                {
                                    streamSeekPos = 0;
                                }
                                else
                                {
                                    reachedEndSample = true;
                                }
                            }
                        }
                        
                        if (readSamples > 0)
                        {
                            Al.BufferData<short>(unqueuedBuffers[i], Sound.ALFormat, buffer, readSamples, Sound.SampleRate);

                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to assign data to stream buffer: " +
                                    Al.GetErrorString(alError) + ": " + unqueuedBuffers[i].ToString() + "/" + unqueuedBuffers.Length + ", readSamples: " + readSamples);
                            }

                            Al.SourceQueueBuffer(alSource, unqueuedBuffers[i]);
                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to queue buffer[" + i.ToString() + "] to stream: " + Al.GetErrorString(alError));
                            }
                        }
                        else if (readSamples < 0)
                        {
                            reachedEndSample = true;
                        }
                        else
                        {
                            emptyBuffers.Add((uint)unqueuedBuffers[i]);
                        }
                    }
                    
                    Al.GetSourcei(alSource, Al.SourceState, out state);
                    if (state != Al.Playing)
                    {
                        Al.SourcePlay(alSource);
                    }
                }
            }
        }
    }
}
