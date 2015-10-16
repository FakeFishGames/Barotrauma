using System;
using NVorbis;
using OpenTK.Audio.OpenAL;

namespace Barotrauma.Sounds
{
    class OggSound : IDisposable
    {
        //internal VorbisReader Reader { get; private set; }

        //const int DefaultBufferSize = 44100;

        //private VorbisReader reader; 
        //private SoundEffect effect; 
        //SoundEffectInstance instance; 

        public const int DefaultBufferCount = 3;

        private short[] castBuffer;

        private int sampleRate;
        private ALFormat format;

        private string file;

        int alBufferId;

        public int AlBufferId
        {
            get { return alBufferId; }
        }

        //public bool IsLooped { get; set; }

        public static OggSound Load(string oggFile, int bufferCount = DefaultBufferCount)
        {
            OggSound sound = new OggSound();
            sound.file = oggFile;

            using (VorbisReader reader = new VorbisReader(oggFile))
            {
                int bufferSize = (int)reader.TotalSamples;

                float[] buffer = new float[bufferSize];
                sound.castBuffer = new short[bufferSize];

                int readSamples = reader.ReadSamples(buffer, 0, bufferSize);
                CastBuffer(buffer, sound.castBuffer, readSamples);

                sound.alBufferId = AL.GenBuffer();

                sound.format = reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;
                sound.sampleRate = reader.SampleRate;

                //alSourceId = AL.GenSource();
                AL.BufferData(sound.alBufferId, reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16, sound.castBuffer,
                              readSamples * sizeof(short), reader.SampleRate);

                ALHelper.Check();
            }

            //AL.Source(alSourceId, ALSourcei.Buffer, alBufferId);

            //if (ALHelper.XRam.IsInitialized)
            //{
            //    ALHelper.XRam.SetBufferMode(bufferCount, ref alBufferId, XRamExtension.XRamStorage.Hardware);
            //    ALHelper.Check();
            //}

            //Volume = 1;

            //if (ALHelper.Efx.IsInitialized)
            //{
            //    alFilterId = ALHelper.Efx.GenFilter();
            //    ALHelper.Efx.Filter(alFilterId, EfxFilteri.FilterType, (int)EfxFilterType.Lowpass);
            //    ALHelper.Efx.Filter(alFilterId, EfxFilterf.LowpassGain, 1);
            //    LowPassHFGain = 1;
            //}
            
            return sound;

        }

        public void SetBufferData(int alBufferId)
        {
            AL.BufferData(alBufferId, format, castBuffer,
                castBuffer.Length * sizeof(short), sampleRate);
        }

        static void CastBuffer(float[] inBuffer, short[] outBuffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                int temp = (int)(32767f * inBuffer[i]);
                if (temp > short.MaxValue) temp = short.MaxValue;
                else if (temp < short.MinValue) temp = short.MinValue;
                outBuffer[i] = (short)temp;
            }
        }
 
        public void Dispose()
        {
            //var state = AL.GetSourceState(alSourceId);
            //if (state == ALSourceState.Playing || state == ALSourceState.Paused)
            //    Stop();
            System.Diagnostics.Debug.WriteLine(alBufferId);
            //AL.DeleteSource(alSourceId);
            AL.DeleteBuffer(alBufferId);
            
            //if (ALHelper.Efx.IsInitialized)
            //    ALHelper.Efx.DeleteFilter(alFilterId);

            ALHelper.Check();
        }
        
    }
}
