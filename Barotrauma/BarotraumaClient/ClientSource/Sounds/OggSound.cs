using System;
using OpenAL;
using NVorbis;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Sounds
{
    public class OggSound : Sound
    {
        private VorbisReader reader;

        //key = sample rate, value = filter
        private static Dictionary<int, BiQuad> muffleFilters = new Dictionary<int, BiQuad>();

        private static List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        public OggSound(SoundManager owner, string filename, bool stream, XElement xElement) : base(owner, filename, stream, true, xElement) { }

        public override float GetAmplitudeAtPlaybackPos(int playbackPos)
        {
            if (playbackAmplitude == null || playbackAmplitude.Count == 0) { return 0.0f; }
            int index = playbackPos / AMPLITUDE_SAMPLE_COUNT;
            if (index < 0) { return 0.0f; }
            if (index >= playbackAmplitude.Count) { index = playbackAmplitude.Count - 1; }
            return playbackAmplitude[index];
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) throw new Exception("Called FillStreamBuffer on a non-streamed sound!");
            if (reader == null) throw new Exception("Called FillStreamBuffer when the reader is null!");

            if (samplePos >= reader.TotalSamples * reader.Channels * 2) return 0;

            samplePos /= reader.Channels * 2;
            reader.DecodedPosition = samplePos;

            float[] floatBuffer = new float[buffer.Length];
            int readSamples = reader.ReadSamples(floatBuffer, 0, buffer.Length / 2);
            //MuffleBuffer(floatBuffer, reader.Channels);
            CastBuffer(floatBuffer, buffer, readSamples);

            return readSamples;
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

        public override void InitializeALBuffers()
        {
            base.InitializeALBuffers();

            reader ??= new VorbisReader(Filename);

            ALFormat = reader.Channels == 1 ? Al.FormatMono16 : Al.FormatStereo16;
            SampleRate = reader.SampleRate;

            if (Buffers != null && SoundBuffers.BuffersGenerated < SoundBuffers.MaxBuffers)
            {
                Buffers.RequestAlBuffers(); FillBuffers();
            }
        }

        public override void FillBuffers()
        {
            if (!Stream)
            {
                reader ??= new VorbisReader(Filename);

                reader.DecodedPosition = 0;

                int bufferSize = (int)reader.TotalSamples * reader.Channels;

                float[] floatBuffer = new float[bufferSize];
                short[] shortBuffer = new short[bufferSize];

                int readSamples = reader.ReadSamples(floatBuffer, 0, bufferSize);

                playbackAmplitude = new List<float>();
                for (int i = 0; i < bufferSize; i += reader.Channels * AMPLITUDE_SAMPLE_COUNT)
                {
                    float maxAmplitude = 0.0f;
                    for (int j = i; j < i + reader.Channels * AMPLITUDE_SAMPLE_COUNT; j++)
                    {
                        if (j >= bufferSize) { break; }
                        maxAmplitude = Math.Max(maxAmplitude, Math.Abs(floatBuffer[j]));
                    }
                    playbackAmplitude.Add(maxAmplitude);
                }

                CastBuffer(floatBuffer, shortBuffer, readSamples);

                Al.BufferData(Buffers.AlBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                int alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError));
                }

                MuffleBuffer(floatBuffer, SampleRate, reader.Channels);

                CastBuffer(floatBuffer, shortBuffer, readSamples);

                Al.BufferData(Buffers.AlMuffledBuffer, ALFormat, shortBuffer,
                                readSamples * sizeof(short), SampleRate);

                alError = Al.GetError();
                if (alError != Al.NoError)
                {
                    throw new Exception("Failed to set muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError));
                }

                reader.Dispose(); reader = null;
            }
        }

        public override void Dispose()
        {
            if (Stream)
            {
                reader?.Dispose();
            }

            base.Dispose();
        }
    }
}
