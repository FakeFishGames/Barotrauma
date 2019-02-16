using System;
using OpenTK.Audio.OpenAL;
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
            ALError alError = ALError.NoError;

            ALSources = new uint[sourceCount];
            for (int i = 0; i < sourceCount; i++)
            {
                AL.GenSource(out ALSources[i]);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Error generating alSource[" + i.ToString() + "]: " + AL.GetErrorString(alError));
                }

                if (!AL.IsSource(ALSources[i]))
                {
                    throw new Exception("Generated alSource[" + i.ToString() + "] is invalid!");
                }

                AL.SourceStop(ALSources[i]);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Error stopping newly generated alSource[" + i.ToString() + "]: " + AL.GetErrorString(alError));
                }

                AL.Source(ALSources[i], ALSourcef.MinGain, 0.0f);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Error setting min gain: " + AL.GetErrorString(alError));
                }

                AL.Source(ALSources[i], ALSourcef.MaxGain, 1.0f);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Error setting max gain: " + AL.GetErrorString(alError));
                }

                AL.Source(ALSources[i], ALSourcef.RolloffFactor, 1.0f);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Error setting rolloff factor: " + AL.GetErrorString(alError));
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < ALSources.Length; i++)
            {
                AL.DeleteSource(ref ALSources[i]);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to delete ALSources[" + i.ToString() + "]: " + AL.GetErrorString(alError));
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
                    AL.Source(alSource, ALSourceb.SourceRelative, false);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to enable source's relative flag: " + AL.GetErrorString(alError));
                    }

                    AL.Source(alSource, ALSource3f.Position, position.Value.X, position.Value.Y, position.Value.Z);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to set source's position: " + AL.GetErrorString(alError));
                    }
                }
                else
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    AL.Source(alSource, ALSourceb.SourceRelative, true);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to disable source's relative flag: " + AL.GetErrorString(alError));
                    }

                    AL.Source(alSource, ALSource3f.Position, 0.0f, 0.0f, 0.0f);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to reset source's position: " + AL.GetErrorString(alError));
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
                AL.Source(alSource, ALSourcef.ReferenceDistance, near);
                
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's reference distance: " + AL.GetErrorString(alError));
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
                AL.Source(alSource, ALSourcef.MaxDistance, far);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's max distance: " + AL.GetErrorString(alError));
                }
            }
        }

        private float gain;
        public float Gain
        {
            get { return gain; }
            set
            {
                gain = Math.Max(Math.Min(value,1.0f),0.0f);

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                float effectiveGain = gain;
                if (category != null) effectiveGain *= Sound.Owner.GetCategoryGainMultiplier(category);

                AL.Source(alSource, ALSourcef.Gain, effectiveGain);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's gain: " + AL.GetErrorString(alError));
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
                    AL.Source(alSource, ALSourceb.Looping, looping);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to set source's looping state: " + AL.GetErrorString(alError));
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
                    int playbackPos; AL.GetSource(alSource, ALGetSourcei.SampleOffset, out playbackPos);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to get source's playback position: " + AL.GetErrorString(alError));
                    }

                    AL.SourceStop(alSource);

                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to stop source: " + AL.GetErrorString(alError));
                    }

                    AL.BindBufferToSource(alSource,(uint)(muffled ? Sound.ALMuffledBuffer : Sound.ALBuffer));

                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source: " + AL.GetErrorString(alError));
                    }

                    AL.SourcePlay(alSource);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to replay source: " + AL.GetErrorString(alError));
                    }

                    AL.Source(alSource, ALSourcei.SampleOffset, playbackPos);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to reset playback position: " + AL.GetErrorString(alError));
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
        }

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
                bool playing = AL.GetSourceState(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex)) == ALSourceState.Playing;
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to determine playing state from source: " + AL.GetErrorString(alError));
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
                ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(this);
                if (ALSourceIndex >= 0)
                {
                    if (!IsStream)
                    {
                        AL.BindBufferToSource(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), 0);
                        ALError alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to reset source buffer: " + AL.GetErrorString(alError));
                        }

                        if (!AL.IsBuffer(sound.ALBuffer))
                        {
                            throw new Exception(sound.Filename + " has an invalid buffer!");
                        }

                        uint alBuffer = sound.Owner.GetCategoryMuffle(category) || muffle ? sound.ALMuffledBuffer : sound.ALBuffer;
                        AL.BindBufferToSource(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), alBuffer);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to bind buffer to source (" + ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex) + "," + sound.ALBuffer.ToString() + "): " + AL.GetErrorString(alError));
                        }

                        AL.SourcePlay(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to play source: " + AL.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        AL.BindBufferToSource(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), (uint)sound.ALBuffer);
                        ALError alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to reset source buffer: " + AL.GetErrorString(alError));
                        }

                        AL.Source(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), ALSourceb.Looping, false);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to set stream looping state: " + AL.GetErrorString(alError));
                        }

                        streamShortBuffer = new short[STREAM_BUFFER_SIZE];

                        streamBuffers = new uint[4];
                        emptyBuffers = new List<uint>();
                        for (int i = 0; i < 4; i++)
                        {
                            AL.GenBuffer(out streamBuffers[i]);

                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to generate stream buffers: " + AL.GetErrorString(alError));
                            }

                            if (!AL.IsBuffer(streamBuffers[i]))
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
        
        public void Dispose()
        {
            lock (mutex)
            {
                if (ALSourceIndex >= 0)
                {
                    AL.SourceStop(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to stop source: " + AL.GetErrorString(alError));
                    }
                
                    if (IsStream)
                    {
                        uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                        AL.SourceStop(alSource);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to stop streamed source: " + AL.GetErrorString(alError));
                        }

                        int buffersToUnqueue = 0;
                        int[] unqueuedBuffers = null;

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

                        AL.BindBufferToSource(alSource, 0);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to reset buffer for streamed source: " + AL.GetErrorString(alError));
                        }
                        
                        for (int i = 0; i < 4; i++)
                        {
                            AL.DeleteBuffer(ref streamBuffers[i]);
                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to delete streamBuffers[" + i.ToString() + "] ("+streamBuffers[i].ToString()+"): " + AL.GetErrorString(alError));
                            }
                        }

                        reachedEndSample = true;
                    }
                    else
                    {
                        AL.BindBufferToSource(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), 0);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to unbind buffer to non-streamed source: " + AL.GetErrorString(alError));
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

                    bool playing = AL.GetSourceState(alSource) == ALSourceState.Playing;
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to determine playing state from streamed source: " + AL.GetErrorString(alError));
                    }
                    
                    int buffersToUnqueue = 0;
                    int[] unqueuedBuffers = null;
                    if (!startedPlaying)
                    {
                        buffersToUnqueue = 0;
                        AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out buffersToUnqueue);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + AL.GetErrorString(alError));
                        }

                        unqueuedBuffers = new int[buffersToUnqueue+emptyBuffers.Count];
                        AL.SourceUnqueueBuffers((int)alSource, buffersToUnqueue, unqueuedBuffers);
                        for (int i = 0; i < emptyBuffers.Count; i++)
                        {
                            unqueuedBuffers[buffersToUnqueue + i] = (int)emptyBuffers[i];
                        }
                        buffersToUnqueue += emptyBuffers.Count;
                        emptyBuffers.Clear();
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + AL.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        startedPlaying = false;
                        buffersToUnqueue = 4;
                        unqueuedBuffers = new int[4];
                        for (int i = 0; i < 4; i++)
                        {
                            unqueuedBuffers[i] = (int)streamBuffers[i];
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
                            AL.BufferData<short>(unqueuedBuffers[i], Sound.ALFormat, buffer, readSamples, Sound.SampleRate);

                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to assign data to stream buffer: " +
                                    AL.GetErrorString(alError) + ": " + unqueuedBuffers[i].ToString() + "/" + unqueuedBuffers.Length + ", readSamples: " + readSamples);
                            }

                            AL.SourceQueueBuffer((int)alSource, unqueuedBuffers[i]);
                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to queue buffer[" + i.ToString() + "] to stream: " + AL.GetErrorString(alError));
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

                    if (AL.GetSourceState(alSource) != ALSourceState.Playing)
                    {
                        AL.SourcePlay(alSource);
                    }
                }
            }
        }
    }
}
