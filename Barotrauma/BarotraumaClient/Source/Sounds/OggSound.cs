using System;
using OpenTK.Audio.OpenAL;
using NVorbis;

namespace Barotrauma.Sounds
{
    public class OggSound : Sound
    {
        private VorbisReader reader;

        public OggSound(SoundManager owner, string filename, bool stream) : base(owner, filename, stream, true)
        {
            if (!ToolBox.IsProperFilenameCase(filename))
            {
                DebugConsole.ThrowError("Sound file \"" + filename + "\" has incorrect case!");
            }

            reader = new VorbisReader(filename);

            ALFormat = reader.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;
            SampleRate = reader.SampleRate;

            if (!stream)
            {
                int bufferSize = (int)reader.TotalSamples*reader.Channels;

                float[] floatBuffer = new float[bufferSize];
                short[] shortBuffer = new short[bufferSize];

                int readSamples = reader.ReadSamples(floatBuffer, 0, bufferSize);
                
                CastBuffer(floatBuffer, shortBuffer, readSamples);
                
                AL.BufferData((int)ALBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set buffer data for non-streamed audio! "+AL.GetErrorString(alError));
                }

                MuffleBuffer(floatBuffer, reader.Channels);

                CastBuffer(floatBuffer, shortBuffer, readSamples);

                AL.BufferData((int)ALMuffledBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set buffer data for non-streamed audio! " + AL.GetErrorString(alError));
                }

                reader.Dispose();
            }
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) throw new Exception("Called FillStreamBuffer on a non-streamed sound!");
            
            if (samplePos >= reader.TotalSamples * reader.Channels * 2) return 0;

            samplePos /= reader.Channels*2;
            reader.DecodedPosition = samplePos;

            float[] floatBuffer = new float[buffer.Length];
            int readSamples = reader.ReadSamples(floatBuffer, 0, buffer.Length/2);
            //MuffleBuffer(floatBuffer, reader.Channels);
            CastBuffer(floatBuffer, buffer, readSamples);
            
            return readSamples*2;
        }

        static void MuffleBuffer(float[] buffer,int channelCount)
        {
            //this function will probably have to replace EFX on OSX
            float[] avgvals = new float[channelCount];
            for (int j = 0; j < channelCount; j++)
            {
                avgvals[j] = buffer[j];
            }
            for (int i = 0; i < buffer.Length; i+=channelCount)
            {
                for (int j = 0; j < channelCount; j++)
                {
                    float fval = buffer[i + j];
                    float weight = 0.7f;
                    weight = 1.0f - weight;
                    weight *= weight * weight;
                    avgvals[j] = (avgvals[j] * (1.0f - weight) + fval * weight);
                    fval = avgvals[j]*1.7f;
                    buffer[i + j] = fval;
                }
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