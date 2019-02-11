using System;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;
using System.IO;

namespace Barotrauma.Sounds
{
    public abstract class Sound : IDisposable
    {
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

        public bool Stream
        {
            get;
            protected set;
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

        public ALFormat ALFormat
        {
            get;
            protected set;
        }

        public int SampleRate
        {
            get;
            protected set;
        }

        public bool StreamsReliably
        {
            get;
            protected set;
        }

        public float BaseGain;
        public float BaseNear;
        public float BaseFar;
        
        public Sound(SoundManager owner,string filename,bool stream,bool streamsReliably)
        {
            Owner = owner;
            Filename = Path.GetFullPath(filename);
            Stream = stream;
            StreamsReliably = streamsReliably;

            BaseGain = 1.0f;
            BaseNear = 100.0f;
            BaseFar = 200.0f;
            
            if (!stream)
            {
                AL.GenBuffer(out alBuffer);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + AL.GetErrorString(alError));
                }

                if (!AL.IsBuffer(alBuffer))
                {
                    throw new Exception("Generated OpenAL buffer is invalid!");
                }

                AL.GenBuffer(out alMuffledBuffer);
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + AL.GetErrorString(alError));
                }
                
                if (!AL.IsBuffer(alMuffledBuffer))
                {
                    throw new Exception("Generated OpenAL buffer is invalid!");
                }
            }
            else
            {
                alBuffer = 0;
            }
        }

        public override string ToString()
        {
            return GetType().ToString() + " (" + Filename + ")";
        }

        public bool IsPlaying()
        {
            return Owner.IsPlaying(this);
        }

        public virtual SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            return new SoundChannel(this, gain, new Vector3(position.X, position.Y, 0.0f), range * 0.4f, range, "default", muffle);
        }

        public virtual SoundChannel Play(Vector3? position, float gain, bool muffle = false)
        {
            return new SoundChannel(this, gain, position, BaseNear, BaseFar, "default", muffle);
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
            return new SoundChannel(this, gain ?? BaseGain, null, BaseNear, BaseFar, category);
        }

        static protected void CastBuffer(float[] inBuffer, short[] outBuffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                float fval = Math.Max(Math.Min(inBuffer[i], 1.0f), -1.0f);
                int temp = (int)(32767f * fval);
                if (temp > short.MaxValue) temp = short.MaxValue;
                else if (temp < short.MinValue) temp = short.MinValue;
                outBuffer[i] = (short)temp;
            }
        }

        public abstract int FillStreamBuffer(int samplePos, short[] buffer);

        public virtual void Dispose()
        {
            Owner.KillChannels(this);
            if (alBuffer != 0)
            {
                if (!AL.IsBuffer(alBuffer))
                {
                    throw new Exception("Buffer to delete is invalid!");
                }
            
                AL.DeleteBuffer(ref alBuffer); alBuffer = 0;

                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to delete OpenAL buffer for non-streamed sound: " + AL.GetErrorString(alError));
                }
            }
            Owner.RemoveSound(this);
        }
    }
}

