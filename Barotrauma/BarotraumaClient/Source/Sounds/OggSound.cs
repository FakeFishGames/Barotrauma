using System;
using OpenTK.Audio.OpenAL;
using NVorbis;

namespace Barotrauma
{
    class OggSound : Sound
    {
        private VorbisReader reader;

        public OggSound(SoundManager owner,string filename,bool stream) : base(owner,filename,stream)
        {
            reader = new VorbisReader(filename);
            if (!stream)
            {
                int bufferSize = (int)reader.TotalSamples*reader.Channels;

                float[] floatBuffer = new float[bufferSize];
                short[] shortBuffer = new short[bufferSize];

                int readSamples = reader.ReadSamples(floatBuffer, 0, bufferSize);
                CastBuffer(floatBuffer, shortBuffer, readSamples);

                ALFormat = reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;
                SampleRate = reader.SampleRate;
                
                AL.BufferData(ALBuffer, reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16, shortBuffer,
                              readSamples * sizeof(short), reader.SampleRate);

                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set buffer data for non-streamed audio! "+AL.GetErrorString(alError));
                }

                reader.Dispose();
            }
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) throw new Exception("Called FillStreamBuffer on a non-streamed sound!");

            reader.DecodedPosition = samplePos;

            float[] floatBuffer = new float[buffer.Length];
            int readSamples = reader.ReadSamples(floatBuffer, 0, buffer.Length);
            CastBuffer(floatBuffer, buffer, readSamples);

            return readSamples;
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

        public override void Dispose()
        {
            if (Stream)
            {
                reader.Dispose();
            }

            base.Dispose();
        }
    }
}