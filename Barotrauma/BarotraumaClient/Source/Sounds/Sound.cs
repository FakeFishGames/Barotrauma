using System;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;

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

        private int alBuffer;
        public int ALBuffer
        {
            get { return !Stream ? alBuffer : 0; }
        }

        private int alMuffledBuffer;
        public int ALMuffledBuffer
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

        public float BaseGain;
        public float BaseNear;
        public float BaseFar;
        
        public Sound(SoundManager owner,string filename,bool stream)
        {
            Owner = owner;
            Filename = filename;
            Stream = stream;

            BaseGain = 1.0f;
            BaseNear = 100.0f;
            BaseFar = 200.0f;
            
            if (!stream)
            {
                alBuffer = AL.GenBuffer();
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + AL.GetErrorString(alError));
                }

                alMuffledBuffer = AL.GenBuffer();
                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: " + AL.GetErrorString(alError));
                }
            }
            else
            {
                alBuffer = 0;
            }
        }

        public bool IsPlaying()
        {
            return Owner.IsPlaying(this);
        }

        public SoundChannel Play(float gain, float range, Vector2 position)
        {
            return new SoundChannel(this, gain, new Vector3(position.X,position.Y,0.0f), range * 0.4f, range, "default");
        }

        public SoundChannel Play(Vector2 position)
        {
            return new SoundChannel(this, BaseGain, new Vector3(position.X, position.Y, 0.0f), BaseNear, BaseFar, "default");
        }

        public SoundChannel Play(Vector3? position, float gain)
        {
            return new SoundChannel(this, gain, position, BaseNear, BaseFar, "default");
        }

        public SoundChannel Play(float gain)
        {
            return Play(null, gain);
        }

        public SoundChannel Play()
        {
            return Play(BaseGain);
        }

        public SoundChannel Play(float gain,string category)
        {
            return new SoundChannel(this, gain, null, BaseNear, BaseFar, category);
        }

        public abstract int FillStreamBuffer(int samplePos, short[] buffer);

        public virtual void Dispose()
        {
            Owner.KillChannels(this);
            if (alBuffer != 0)
            {
                AL.DeleteBuffer(alBuffer);

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

