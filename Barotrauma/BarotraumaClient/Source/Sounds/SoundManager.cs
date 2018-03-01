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

        private Thread streamingThread;
        private List<Sound> sounds;
        private List<SoundChannel> soundChannels;

        SoundManager()
        {
            working = false;

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

        void UpdateStreaming()
        {
            while (true)
            {
                lock (soundChannels)
                {
                    for (int i=0;i<soundChannels.Count;i++)
                    {
                        if (soundChannels[i].Stream)
                        {

                        }
                    }

                    if (soundChannels.Count == 0) break;
                }
                Thread.Sleep(30);
            }
        }
    }
}
