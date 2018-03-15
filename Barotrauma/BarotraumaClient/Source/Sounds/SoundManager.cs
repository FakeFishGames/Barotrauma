using System;
using System.Threading;
using System.Collections.Generic;
using System.Xml.Linq;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;

namespace Barotrauma.Sounds
{
    public class SoundManager : IDisposable
    {
        const int SOURCE_COUNT = 16;
        
        private IntPtr alcDevice;
        private OpenTK.ContextHandle alcContext;
        private uint[] alSources;

        private List<Sound> loadedSounds;
        private SoundChannel[] playingChannels;

        private Thread streamingThread;

        private Vector3 listenerPosition;
        public Vector3 ListenerPosition
        {
            get { return listenerPosition; }
            set
            {
                listenerPosition = value;
                AL.Listener(ALListener3f.Position,value.X,value.Y,value.Z);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set listener position: " + AL.GetErrorString(alError));
                }
            }
        }

        private float[] listenerOrientation;
        public Vector3 ListenerTargetVector
        {
            get { return new Vector3(listenerOrientation[0], listenerOrientation[1], listenerOrientation[2]); }
            set
            {
                listenerOrientation[0] = value.X; listenerOrientation[1] = value.Y; listenerOrientation[2] = value.Z;
                AL.Listener(ALListenerfv.Orientation, ref listenerOrientation);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set listener target vector: " + AL.GetErrorString(alError));
                }
            }
        }
        public Vector3 ListenerUpVector
        {
            get { return new Vector3(listenerOrientation[3], listenerOrientation[4], listenerOrientation[5]); }
            set
            {
                listenerOrientation[3] = value.X; listenerOrientation[4] = value.Y; listenerOrientation[5] = value.Z;
                AL.Listener(ALListenerfv.Orientation, ref listenerOrientation);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set listener up vector: " + AL.GetErrorString(alError));
                }
            }
        }

        private float listenerGain;
        public float ListenerGain
        {
            get { return listenerGain; }
            set
            {
                listenerGain = value;
                AL.Listener(ALListenerf.Gain, listenerGain);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set listener gain: " + AL.GetErrorString(alError));
                }
            }
        }

        public SoundManager()
        {
            loadedSounds = new List<Sound>();
            playingChannels = new SoundChannel[SOURCE_COUNT];

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

            listenerOrientation = new float[6];
            ListenerPosition = Vector3.Zero;
            ListenerTargetVector = new Vector3(0.0f, 0.0f, 1.0f);
            ListenerUpVector = new Vector3(0.0f, 1.0f, 0.0f);
        }

        public Sound LoadSound(string filename,bool stream=false)
        {
            if (!System.IO.File.Exists(filename))
            {
                throw new Exception("\"" + filename + "\" doesn't exist!");
            }

            Sound newSound = new OggSound(this, filename, stream);
            loadedSounds.Add(newSound);
            return newSound;
        }

        public Sound LoadSound(XElement element,bool stream=false)
        {
            string filePath = element.GetAttributeString("file", "");

            var newSound = new OggSound(this, filePath, stream);
            if (newSound != null)
            {
                newSound.BaseGain = element.GetAttributeFloat("volume", 1.0f);
                float range = element.GetAttributeFloat("range", 1000.0f);
                newSound.BaseNear = range * 0.7f;
                newSound.BaseFar = range * 1.3f;
            }

            loadedSounds.Add(newSound);
            return newSound;
        }

        public uint GetSourceFromIndex(int ind)
        {
            if (ind < 0 || ind >= SOURCE_COUNT) return 0;
            return alSources[ind];
        }

        public int AssignFreeSourceToChannel(SoundChannel newChannel)
        {
            lock (playingChannels)
            {
                //remove a channel that has stopped
                //or hasn't even been assigned
                for (int i = 0; i < SOURCE_COUNT; i++)
                {
                    if (playingChannels[i]==null || !playingChannels[i].IsPlaying)
                    {
                        if (playingChannels[i]!=null) playingChannels[i].Dispose();
                        playingChannels[i] = newChannel;
                        return i;
                    }
                }
                //we couldn't get a free source to assign to this channel!
                return -1;
            }
        }

        public bool IsPlaying(Sound sound)
        {
            lock (playingChannels)
            {
                for (int i = 0; i < SOURCE_COUNT - 1; i++)
                {
                    if (playingChannels[i] != null && playingChannels[i].Sound == sound)
                    {
                        if (playingChannels[i].IsPlaying) return true;
                    }
                }
            }
            return false;
        }

        public void KillChannels(Sound sound)
        {
            lock (playingChannels)
            {
                for (int i = 0; i < SOURCE_COUNT; i++)
                {
                    if (playingChannels[i]!=null && playingChannels[i].Sound == sound)
                    {
                        playingChannels[i].Dispose();
                        playingChannels[i] = null;
                    }
                }
            }
        }

        public void RemoveSound(Sound sound)
        {
            for (int i=0;i<loadedSounds.Count;i++)
            {
                if (loadedSounds[i]==sound)
                {
                    loadedSounds.RemoveAt(i);
                    return;
                }
            }
        }

        public void InitStreamThread()
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
                    for (int i=0;i<SOURCE_COUNT;i++)
                    {
                        if (playingChannels[i]!=null && playingChannels[i].IsStream)
                        {
                            if (playingChannels[i].IsPlaying)
                            {
                                areStreamsPlaying = true;
                                playingChannels[i].UpdateStream();
                            }
                            else
                            {
                                playingChannels[i].Dispose();
                            }
                        }
                    }
                }
                Thread.Sleep(300);
            }
        }

        public void Dispose()
        {
            lock (playingChannels)
            {
                for (int i=0;i<SOURCE_COUNT;i++)
                {
                    if (playingChannels[i]!=null) playingChannels[i].Dispose();
                }
            }
            if (streamingThread != null && streamingThread.ThreadState == ThreadState.Running)
            {
                streamingThread.Join();
            }
            for (int i = loadedSounds.Count - 1; i >= 0; i--)
            {
                loadedSounds[i].Dispose();
            }
            for (int i = 0; i < SOURCE_COUNT; i++)
            {
                AL.DeleteSource(ref alSources[i]);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to delete alSources[" + i.ToString() + "]: " + AL.GetErrorString(alError));
                }
            }
            
            if (!Alc.MakeContextCurrent(OpenTK.ContextHandle.Zero))
            {
                throw new Exception("Failed to detach the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + ")");
            }

            Alc.DestroyContext(alcContext);
            
            if (!Alc.CloseDevice(alcDevice))
            {
                throw new Exception("Failed to close ALC device!");
            }
        }
    }
}
