using System;
using OpenAL;
using Microsoft.Xna.Framework;
using Barotrauma.IO;
using System.Xml.Linq;

namespace Barotrauma.Sounds
{
    abstract class Sound : IDisposable
    {
        protected bool disposed;
        public bool Disposed
        {
            get { return disposed; }
        }

        public readonly SoundManager Owner;

        public readonly string Filename;

        public readonly XElement XElement;

        public readonly bool Stream;

        public readonly bool StreamsReliably;

        public virtual SoundManager.SourcePoolIndex SourcePoolIndex
        {
            get
            {
                return SoundManager.SourcePoolIndex.Default;
            }
        }

        private SoundBuffers buffers;
        public SoundBuffers Buffers
        {
            get { return !Stream ? buffers : null; }
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

        /// <summary>
        /// How many instances of the same sound clip can be playing at the same time
        /// </summary>
        public int MaxSimultaneousInstances = 5;
        
        public float BaseGain;
        public float BaseNear;
        public float BaseFar;

        public Sound(SoundManager owner, string filename, bool stream, bool streamsReliably, XElement xElement=null, bool getFullPath=true)
        {
            Owner = owner;
            Filename = getFullPath ? Path.GetFullPath(filename.CleanUpPath()).CleanUpPath() : filename;
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
                buffers = new SoundBuffers(this);
            }
            else
            {
                buffers = null;
            }
        }

        public virtual void FillBuffers() { }

        public virtual void DeleteALBuffers()
        {
            Owner.KillChannels(this);
            buffers?.Dispose();
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

