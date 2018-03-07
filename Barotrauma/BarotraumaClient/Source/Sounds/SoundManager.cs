using System;
using System.Threading;
using System.Collections.Generic;
using System.Timers;
using OpenTK;
using OpenTK.Audio.OpenAL;

namespace Barotrauma
{
    class SoundManager : IDisposable
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
            Sound newSound = new OggSound(this, filename, stream);
            loadedSounds.Add(newSound);
            return newSound;
        }

        public uint GetSourceFromIndex(int ind)
        {
            return alSources[ind];
        }

        public int AssignFreeSourceToChannel(SoundChannel newChannel)
        {
            lock (playingChannels)
            {
                //remove a channel that has stopped
                for (int i = 0; i < playingChannels.Count; i++)
                {
                    if (!playingChannels[i].IsPlaying)
                    {
                        playingChannels[i].Dispose();
                        playingChannels[i] = newChannel;
                        if (newChannel.IsStream) InitStreamThread();
                        return i;
                    }
                }
                //all of the currently stored channels are playing
                //add a new channel to the list if we have available sources
                if (playingChannels.Count < SOURCE_COUNT)
                {
                    playingChannels.Add(newChannel);
                    if (newChannel.IsStream) InitStreamThread();
                    return playingChannels.Count - 1;
                }
                //we couldn't get a free source to assign to this channel!
                return -1;
            }
        }

        public void KillChannels(Sound sound)
        {
            lock (playingChannels)
            {
                for (int i = playingChannels.Count - 1; i >= 0; i--)
                {
                    if (playingChannels[i].Sound == sound)
                    {
                        playingChannels[i].Dispose();
                        playingChannels.RemoveAt(i);
                    }
                }
            }
        }

        void InitStreamThread()
        {
            if (streamingThread == null || streamingThread.ThreadState!=ThreadState.Running)
            {
                streamingThread = new Thread(UpdateStreaming);
                streamingThread.IsBackground = true; //this should kill the thread if the game crashes
                streamingThread.Start();
            }
        }

        void UpdateStreaming()
        {
            bool areStreamsPlaying = true;
            while (areStreamsPlaying)
            {
                areStreamsPlaying = false;
                lock (playingChannels)
                {
                    for (int i=0;i<playingChannels.Count;i++)
                    {
                        if (playingChannels[i].IsStream)
                        {
                            if (playingChannels[i].IsPlaying)
                            {
                                areStreamsPlaying = true;
                                playingChannels[i].UpdateStream();
                            }
                        }
                    }

                    if (playingChannels.Count == 0) break;
                }
                Thread.Sleep(300);
            }
        }

        public void Dispose()
        {
            lock (playingChannels)
            {
                for (int i=0;i<playingChannels.Count;i++)
                {
                    playingChannels[i].Dispose();
                }
                playingChannels.Clear();
            }
            if (streamingThread!=null && streamingThread.ThreadState==ThreadState.Running)
            {
                streamingThread.Join();
            }
        }
    }
}
