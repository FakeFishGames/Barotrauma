using System;
using OpenAL;
using Microsoft.Xna.Framework;
using Barotrauma.IO;
using System.Xml.Linq;

namespace Barotrauma.Sounds
{
    public abstract class Sound : IDisposable
    {
        protected bool disposed;
        public bool Disposed
        {
            get { return disposed; }
        }

        public SoundManager Owner
        {
            get;
            protected set;
        }

        public string Filename
        {
            get;
            protected set;
        }

        public XElement XElement
        {
            get;
            protected set;
        }

        public bool Stream
        {
            get;
            protected set;
        }

        public bool StreamsReliably
        {
            get;
            protected set;
        }

        public virtual SoundManager.SourcePoolIndex SourcePoolIndex
        {
            get
            {
                return SoundManager.SourcePoolIndex.Default;
            }
        }

        private uint alBuffer;
        public uint ALBuffer
        {
            get { return !Stream ? alBuffer : 0; }
        }

        private uint alMuffledBuffer;
        public uint ALMuffledBuffer
        {
            get { return !Stream ? alMuffledBuffer : 0; }
        }

        public int ALFormat
        {
            get;
            protected set;
        }

        public int SampleRate
        {
            get;
            protected set;
        }

        public bool IgnoreMuffling { get; set; }

        /// <summary>
        /// How many instances of the same sound clip can be playing at the same time
        /// </summary>
        public int MaxSimultaneousInstances = 5;
        
        public float BaseGain;
        public float BaseNear;
        public float BaseFar;

        public Sound(SoundManager owner, string filename, bool stream, bool streamsReliably, XElement xElement=null)
        {
            Owner = owner;
            Filename = Path.GetFullPath(filename.CleanUpPath()).CleanUpPath();
            Stream = stream;
            StreamsReliably = streamsReliably;
            XElement = xElement;

            BaseGain = 1.0f;
            BaseNear = 100.0f;
            BaseFar = 200.0f;

            InitializeALBuffers();
        }

        public override string ToString()
        {
            return GetType().ToString() + " (" + Filename + ")";
        }

        public virtual bool IsPlaying()
        {
            return Owner.IsPlaying(this);
        }

        public virtual SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            return new SoundChannel(this, gain, new Vector3(position.X, position.Y, 0.0f), 1.0f, range * 0.4f, range, "default", muffle);
        }

        public virtual SoundChannel Play(float gain, float range, float freqMult, Vector2 position, bool muffle = false)
        {
            return new SoundChannel(this, gain, new Vector3(position.X, position.Y, 0.0f), freqMult, range * 0.4f, range, "default", muffle);
        }

        public virtual SoundChannel Play(Vector3? position, float gain, float freqMult = 1.0f, bool muffle = false)
        {
            return new SoundChannel(this, gain, position, freqMult, BaseNear, BaseFar, "default", muffle);
        }

        public virtual SoundChannel Play(float gain)
        {
            return Play(null, gain);
        }

        public virtual SoundChannel Play()
        {
            return Play(BaseGain);
        }

        public virtual SoundChannel Play(float? gain, string category)
        {
            if (Owner.CountPlayingInstances(this) >= MaxSimultaneousInstances) { return null; }
            return new SoundChannel(this, gain ?? BaseGain, null, 1.0f, BaseNear, BaseFar, category);
        }

        static protected void CastBuffer(float[] inBuffer, short[] outBuffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                outBuffer[i] = FloatToShort(inBuffer[i]);
            }
        }

        static protected short FloatToShort(float fVal)
        {
            int temp = (int)(32767 * fVal);
            if (temp > short.MaxValue) temp = short.MaxValue;
            else if (temp < short.MinValue) temp = short.MinValue;
            return (short)temp;
        }
        static protected float ShortToFloat(short shortVal)
        {
            return shortVal / 32767f;
        }

        public abstract int FillStreamBuffer(int samplePos, short[] buffer);

        public abstract float GetAmplitudeAtPlaybackPos(int playbackPos);

        public virtual void InitializeALBuffers()
        {
            if (!Stream)
            {
                Al.GenBuffer(out alBuffer);
                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + Al.GetErrorString(alError));
                }

                if (!Al.IsBuffer(alBuffer))
                {
                    throw new Exception("Generated OpenAL buffer is invalid!");
                }

                Al.GenBuffer(out alMuffledBuffer);
                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + Al.GetErrorString(alError));
                }

                if (!Al.IsBuffer(alMuffledBuffer))
                {
                    throw new Exception("Generated OpenAL buffer is invalid!");
                }
            }
            else
            {
                alBuffer = 0;
            }
        }

        public virtual void DeleteALBuffers()
        {
            Owner.KillChannels(this);
            if (alBuffer != 0)
            {
                if (!Al.IsBuffer(alBuffer))
                {
                    throw new Exception("Buffer to delete is invalid!");
                }

                Al.DeleteBuffer(alBuffer); alBuffer = 0;

                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to delete OpenAL buffer for non-streamed sound: " + Al.GetErrorString(alError));
                }
            }
            if (alMuffledBuffer != 0)
            {
                if (!Al.IsBuffer(alMuffledBuffer))
                {
                    throw new Exception("Buffer to delete is invalid!");
                }

                Al.DeleteBuffer(alMuffledBuffer); alMuffledBuffer = 0;

                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to delete OpenAL buffer for non-streamed sound: " + Al.GetErrorString(alError));
                }
            }
        }

        public virtual void Dispose()
        {
            if (disposed) { return; }

            DeleteALBuffers();

            Owner.RemoveSound(this);
            disposed = true;
        }
    }
}

