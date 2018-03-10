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
        
        public Sound(SoundManager owner,string filename,bool stream)
        {
            Owner = owner;
            Filename = filename;
            Stream = stream;
            
            if (!stream)
            {
                alBuffer = AL.GenBuffer();
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to create OpenAL buffer for non-streamed sound: "+AL.GetErrorString(alError));
                }
            }
            else
            {
                alBuffer = 0;
            }
        }

        public SoundChannel Play(Vector3? position, float gain)
        {
            return new SoundChannel(this, position, gain);
        }

        public SoundChannel Play(float gain)
        {
            return Play(null, gain);
        }

        public SoundChannel Play()
        {
            return Play(1.0f);
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

