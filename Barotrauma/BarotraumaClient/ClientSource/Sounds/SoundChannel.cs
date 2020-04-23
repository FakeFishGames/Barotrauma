using System;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Threading;

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
        private const int STREAM_BUFFER_SIZE = 8820;
        private short[] streamShortBuffer;

        private string debugName = "SoundChannel";

        private Vector3? position;
        public Vector3? Position
        {
            get { return position; }
            set
            {
                position = value;

                if (ALSourceIndex < 0) { return; }

                if (position != null)
                {
                    if (float.IsNaN(position.Value.X))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.X is NaN");
                    }
                    if (float.IsNaN(position.Value.Y))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.Y is NaN");
                    }
                    if (float.IsNaN(position.Value.Z))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.Z is NaN");
                    }

                    if (float.IsInfinity(position.Value.X))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.X is Infinity");
                    }
                    if (float.IsInfinity(position.Value.Y))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.Y is Infinity");
                    }
                    if (float.IsInfinity(position.Value.Z))
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", position.Z is Infinity");
                    }

                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.SourceRelative, Al.False);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to enable source's relative flag: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Source3f(alSource, Al.Position, position.Value.X, position.Value.Y, position.Value.Z);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to set source's position: " + debugName + ", " + Al.GetErrorString(alError));
                    }
                }
                else
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.SourceRelative, Al.True);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to disable source's relative flag: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Source3f(alSource, Al.Position, 0.0f, 0.0f, 0.0f);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset source's position: " + debugName + ", " + Al.GetErrorString(alError));
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

                if (ALSourceIndex < 0) { return; }

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                Al.Sourcef(alSource, Al.ReferenceDistance, near);
                
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's reference distance: " + debugName + ", " + Al.GetErrorString(alError));
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

                if (ALSourceIndex < 0) { return; }

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                Al.Sourcef(alSource, Al.MaxDistance, far);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's max distance: " + debugName + ", " + Al.GetErrorString(alError));
                }
            }
        }

        private float gain;
        public float Gain
        {
            get { return gain; }
            set
            {
                gain = Math.Clamp(value, 0.0f, 1.0f);

                if (ALSourceIndex < 0) { return; }

                uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                float effectiveGain = gain;
                if (category != null) effectiveGain *= Sound.Owner.GetCategoryGainMultiplier(category);

                Al.Sourcef(alSource, Al.Gain, effectiveGain);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set source's gain: " + debugName + ", " + Al.GetErrorString(alError));
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

                if (ALSourceIndex < 0) { return; }

                if (!IsStream)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    Al.Sourcei(alSource, Al.Looping, looping ? Al.True : Al.False);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to set source's looping state: " + debugName + ", " + Al.GetErrorString(alError));
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
                if (muffled == value) { return; }

                muffled = value;

                if (ALSourceIndex < 0) { return; }

                if (!IsPlaying) return;

                if (!IsStream)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);
                    int playbackPos; Al.GetSourcei(alSource, Al.SampleOffset, out playbackPos);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to get source's playback position: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.SourceStop(alSource);

                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to stop source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(alSource, Al.Buffer, muffled ? (int)Sound.ALMuffledBuffer : (int)Sound.ALBuffer);

                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.SourcePlay(alSource);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to replay source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(alSource, Al.SampleOffset, playbackPos);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset playback position: " + debugName + ", " + Al.GetErrorString(alError));
                    }
                }
            }
        }

        private float streamAmplitude;
        public float CurrentAmplitude
        {
            get
            {
                if (!IsPlaying) { return 0.0f; }
                
                uint alSource = Sound?.Owner?.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex) ?? 0;

                if (alSource == 0) { return 0.0f; }

                if (!IsStream)
                {
                    int playbackPos; Al.GetSourcei(alSource, Al.SampleOffset, out playbackPos);
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to get source's playback position: " + debugName + ", " + Al.GetErrorString(alError));
                    }
                    return Sound.GetAmplitudeAtPlaybackPos(playbackPos);
                }
                else
                {
                    float retVal = -1.0f;
                    Monitor.Enter(mutex);
                    retVal = streamAmplitude;
                    Monitor.Exit(mutex);
                    return retVal;
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
        private int buffersToRequeue;
        private bool reachedEndSample;
        private int queueStartIndex;
        private readonly uint[] streamBuffers;
        private uint[] unqueuedBuffers;
        private float[] streamBufferAmplitudes;

        public int StreamSeekPos
        {
            get { return streamSeekPos; }
            set 
            {
                if (!IsStream)
                {
                    throw new InvalidOperationException("Cannot set StreamSeekPos on a non-streaming sound channel.");
                }
                streamSeekPos = Math.Max(value, 0);
            }
        }

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
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to determine playing state from source: " + debugName + ", " + Al.GetErrorString(alError));
                }
                bool playing = state == Al.Playing;
                return playing;
            }
        }

        public SoundChannel(Sound sound, float gain, Vector3? position, float near, float far, string category, bool muffle = false)
        {
            Sound = sound;

            debugName = sound == null ?
                "SoundChannel (null)" :
                $"SoundChannel ({(string.IsNullOrEmpty(sound.Filename) ? "filename empty" : sound.Filename) })";

            IsStream = sound.Stream;
            FilledByNetwork = sound is VoipSound;
            decayTimer = 0;
            streamSeekPos = 0; reachedEndSample = false;
            buffersToRequeue = 4;
            muffled = muffle;

            if (IsStream)
            {
                mutex = new object();
            }

            try
            {
                if (mutex != null) { Monitor.Enter(mutex); }
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
                            throw new Exception("Failed to reset source buffer: " + debugName + ", " + Al.GetErrorString(alError));
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
                            throw new Exception("Failed to bind buffer to source (" + ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex) + "," + sound.ALBuffer.ToString() + "): " + debugName + ", " + Al.GetErrorString(alError));
                        }

                        Al.SourcePlay(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to play source: " + debugName + ", " + Al.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        uint alBuffer = sound.Owner.GetCategoryMuffle(category) || muffle ? sound.ALMuffledBuffer : sound.ALBuffer;
                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Buffer, (int)alBuffer);
                        int alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to reset source buffer: " + debugName + ", " + Al.GetErrorString(alError));
                        }

                        Al.Sourcei(sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex), Al.Looping, Al.False);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to set stream looping state: " + debugName + ", " + Al.GetErrorString(alError));
                        }

                        streamShortBuffer = new short[STREAM_BUFFER_SIZE];

                        streamBuffers = new uint[4];
                        unqueuedBuffers = new uint[4];
                        streamBufferAmplitudes = new float[4];
                        for (int i = 0; i < 4; i++)
                        {
                            Al.GenBuffer(out streamBuffers[i]);

                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to generate stream buffers: " + debugName + ", " + Al.GetErrorString(alError));
                            }

                            if (!Al.IsBuffer(streamBuffers[i]))
                            {
                                throw new Exception("Generated streamBuffer[" + i.ToString() + "] is invalid! " + debugName);
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
            catch
            {
                throw;
            }
            finally
            {
                if (mutex != null) { Monitor.Exit(mutex); }
            }

            Sound.Owner.Update();
        }

        public override string ToString()
        {
            return debugName;
        }

        public bool FadingOutAndDisposing
        {
            get;
            private set;
        }
        public void FadeOutAndDispose()
        {
            FadingOutAndDisposing = true;
        }

        public void Dispose()
        {
            try
            {
                if (mutex != null) { Monitor.Enter(mutex); }
                if (ALSourceIndex >= 0)
                {
                    Al.SourceStop(Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex));
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to stop source: " + debugName + ", " + Al.GetErrorString(alError));
                    }
                
                    if (IsStream)
                    {
                        uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                        Al.SourceStop(alSource);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to stop streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                        }

                        int buffersToRequeue = 0;
                        
                        buffersToRequeue = 0;
                        Al.GetSourcei(alSource, Al.BuffersProcessed, out buffersToRequeue);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                        }
 
                        Al.SourceUnqueueBuffers(alSource, buffersToRequeue, unqueuedBuffers);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                        }
                        
                        Al.Sourcei(alSource, Al.Buffer, 0);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to reset buffer for streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                        }
                        
                        for (int i = 0; i < 4; i++)
                        {
                            Al.DeleteBuffer(streamBuffers[i]);
                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to delete streamBuffers[" + i.ToString() + "] (" + streamBuffers[i].ToString() + "): " + debugName + ", " + Al.GetErrorString(alError));
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
                            throw new Exception("Failed to unbind buffer to non-streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                        }
                    }

                    ALSourceIndex = -1;
                    debugName += " [DISPOSED]";
                }
            }
            finally
            {
                if (mutex != null) { Monitor.Exit(mutex); }
            }
        }

        public void UpdateStream()
        {
            if (!IsStream) { throw new Exception("Called UpdateStream on a non-streamed sound channel!"); }

            try
            {
                Monitor.Enter(mutex);
                if (!reachedEndSample)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(Sound.SourcePoolIndex, ALSourceIndex);

                    int state;
                    Al.GetSourcei(alSource, Al.SourceState, out state);
                    bool playing = state == Al.Playing;
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to determine playing state from streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    int unqueuedBufferCount;
                    Al.GetSourcei(alSource, Al.BuffersProcessed, out unqueuedBufferCount);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to determine processed buffers from streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.SourceUnqueueBuffers(alSource, unqueuedBufferCount, unqueuedBuffers);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to unqueue buffers from streamed source: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    buffersToRequeue += unqueuedBufferCount;

                    int iterCount = buffersToRequeue;
                    for (int k = 0; k < iterCount; k++)
                    {
                        int index = queueStartIndex;
                        short[] buffer = streamShortBuffer;
                        int readSamples = Sound.FillStreamBuffer(streamSeekPos, buffer);
                        float readAmplitude = 0.0f;

                        for (int i = 0; i < Math.Min(readSamples, buffer.Length); i++)
                        {
                            float sampleF = ((float)buffer[i]) / ((float)short.MaxValue);
                            readAmplitude = Math.Max(readAmplitude, Math.Abs(sampleF));
                        }

                        if (FilledByNetwork)
                        {
                            if (Sound is VoipSound voipSound)
                            {
                                voipSound.ApplyFilters(buffer, readSamples);
                            }

                            if (readSamples <= 0)
                            {
                                streamAmplitude *= 0.5f;
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
                            streamBufferAmplitudes[index] = readAmplitude;

                            Al.BufferData<short>(streamBuffers[index], Sound.ALFormat, buffer, readSamples, Sound.SampleRate);

                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to assign data to stream buffer: " +
                                    Al.GetErrorString(alError) + ": " + streamBuffers[index].ToString() + "/" + streamBuffers.Length + ", readSamples: " + readSamples + ", " + debugName);
                            }

                            Al.SourceQueueBuffer(alSource, streamBuffers[index]);
                            queueStartIndex = (queueStartIndex + 1) % 4;

                            alError = Al.GetError();
                            if (alError != Al.NoError)
                            {
                                throw new Exception("Failed to queue streamBuffer[" + index.ToString() + "] to stream: " + debugName + ", " + Al.GetErrorString(alError));
                            }
                        }
                        else
                        {
                            if (readSamples < 0)
                            {
                                reachedEndSample = true;
                            }
                            break;
                        }
                        buffersToRequeue--;
                    }

                    streamAmplitude = streamBufferAmplitudes[queueStartIndex];

                    Al.GetSourcei(alSource, Al.SourceState, out state);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to retrieve stream source state: " + debugName + ", " + Al.GetErrorString(alError));
                    }

                    if (state != Al.Playing)
                    {
                        Al.SourcePlay(alSource);
                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to start stream playback: " + debugName + ", " + Al.GetErrorString(alError));
                        }
                    }
                }

                if (reachedEndSample)
                {
                    streamAmplitude = 0.0f;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"An exception was thrown when updating a sound stream ({debugName})", e);
            }
            finally
            {
                Monitor.Exit(mutex);
            }
        }
    }
}
