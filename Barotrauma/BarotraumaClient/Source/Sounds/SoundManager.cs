using System;
using System.Threading;
using System.Collections.Generic;
using System.Timers;
using OpenTK;
using OpenTK.Audio.OpenAL;

namespace Barotrauma
{
    class SoundManager
    {
        const int SOURCE_COUNT = 16;

        private bool working;
        private IntPtr alcDevice;
        private ContextHandle alcContext;
        private uint[] alSources;

        private List<Sound> loadedSounds;
        private List<SoundChannel> playingChannels;

        private Thread streamingThread;

        public SoundManager()
        {
            working = false;

            loadedSounds = new List<Sound>();
            playingChannels = new List<SoundChannel>();

            streamingThread = null;

            alcDevice = Alc.OpenDevice(null);
            if (alcDevice == null)
            {
                throw new Exception("Failed to open an ALC device!");
            }

            int[] alcContextAttrs = new int[] { };
            alcContext = Alc.CreateContext(alcDevice, alcContextAttrs);
            if (alcContext == null)
            {
                throw new Exception("Failed to create an ALC context! (error code: "+Alc.GetError(alcDevice).ToString()+")");
            }

            if (!Alc.MakeContextCurrent(alcContext))
            {
                throw new Exception("Failed to assign the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + ")");
            }

            ALError alError = AL.GetError();
            if (alError != ALError.NoError)
            {
                throw new Exception("OpenAL error after initializing ALC context: " + AL.GetErrorString(alError));
            }

            alSources = new uint[SOURCE_COUNT];
            for (int i=0;i<SOURCE_COUNT;i++)
            {
                AL.GenSource(out alSources[i]);
                AL.SourceStop(alSources[i]);
                alError = AL.GetError();
                if (alError!=ALError.NoError)
                {
                    throw new Exception("Error generating alSource["+i.ToString()+"]: " + AL.GetErrorString(alError));
                }
            }

            working = true;
        }

        public Sound LoadSound(string filename,bool stream)
        {
            return new OggSound(this, filename, stream);
        }

        public uint GetSourceFromIndex(int ind)
        {
            return alSources[ind];
        }

        public int AssignFreeSourceToChannel(SoundChannel newChannel)
        {
            //remove a channel that has stopped
            for (int i=0;i<playingChannels.Count;i++)
            {
                if (!playingChannels[i].IsPlaying)
                {
                    playingChannels[i].Dispose();
                    playingChannels[i] = newChannel;
                    return i;
                }
            }
            //all of the currently stored channels are playing
            //add a new channel to the list if we have available sources
            if (playingChannels.Count < SOURCE_COUNT)
            {
                playingChannels.Add(newChannel);
                return playingChannels.Count-1;
            }
            //we couldn't get a free source to assign to this channel!
            return -1;
        }

        public void KillChannels(Sound sound)
        {
            for (int i = playingChannels.Count-1; i >= 0; i--)
            {
                if (playingChannels[i].Sound == sound)
                {
                    playingChannels[i].Dispose();
                    playingChannels.RemoveAt(i);
                }
            }
        }

        void UpdateStreaming()
        {
            while (true)
            {
                lock (playingChannels)
                {
                    for (int i=0;i<playingChannels.Count;i++)
                    {
                        if (playingChannels[i].IsStream)
                        {
                            playingChannels[i].UpdateStream();
                        }
                    }

                    if (playingChannels.Count == 0) break;
                }
                Thread.Sleep(30);
            }
        }
    }
}
