using System;
using OpenAL;
using NVorbis;
using System.Collections.Generic;

namespace Barotrauma.Sounds
{
    public class OggSound : Sound
    {
        private VorbisReader reader;

        //key = sample rate, value = filter
        private static Dictionary<int, BiQuad> muffleFilters = new Dictionary<int, BiQuad>();

        public OggSound(SoundManager owner, string filename, bool stream) : base(owner, filename, stream, true)
        {
            if (!ToolBox.IsProperFilenameCase(filename))
            {
                DebugConsole.ThrowError("Sound file \"" + filename + "\" has incorrect case!");
            }

            reader = new VorbisReader(filename);

            ALFormat = reader.Channels == 1 ? Al.FormatMono16 : Al.FormatStereo16;
            SampleRate = reader.SampleRate;

            if (!stream)
            {
                int bufferSize = (int)reader.TotalSamples * reader.Channels;

                float[] floatBuffer = new float[bufferSize];
                short[] shortBuffer = new short[bufferSize];

                int readSamples = reader.ReadSamples(floatBuffer, 0, bufferSize);
                
                CastBuffer(floatBuffer, shortBuffer, readSamples);
                
                Al.BufferData(ALBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set buffer data for non-streamed audio! "+Al.GetErrorString(alError));
                }

                MuffleBuffer(floatBuffer, SampleRate, reader.Channels);

                CastBuffer(floatBuffer, shortBuffer, readSamples);

                Al.BufferData(ALMuffledBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set buffer data for non-streamed audio! " + Al.GetErrorString(alError));
                }

                reader.Dispose();
            }
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) throw new Exception("Called FillStreamBuffer on a non-streamed sound!");

            if (samplePos >= reader.TotalSamples * reader.Channels * 2) return 0;

            samplePos /= reader.Channels * 2;
            reader.DecodedPosition = samplePos;

            float[] floatBuffer = new float[buffer.Length];
            int readSamples = reader.ReadSamples(floatBuffer, 0, buffer.Length / 2);
            //MuffleBuffer(floatBuffer, reader.Channels);
            CastBuffer(floatBuffer, buffer, readSamples);

            return readSamples * 2;
        }

        static void MuffleBuffer(float[] buffer, int sampleRate, int channelCount)
        {
            if (!muffleFilters.TryGetValue(sampleRate, out BiQuad filter))
            {
                filter = new LowpassFilter(sampleRate, 800);
                muffleFilters.Add(sampleRate, filter);
            }
            filter.Process(buffer);
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
