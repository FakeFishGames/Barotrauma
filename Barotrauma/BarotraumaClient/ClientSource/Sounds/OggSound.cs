using NVorbis;
using OpenAL;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Barotrauma.Sounds
{
    sealed class OggSound : Sound
    {
        private readonly VorbisReader streamReader;

        public long MaxStreamSamplePos => streamReader == null ? 0 : streamReader.TotalSamples * streamReader.Channels * 2;

        private List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        private short[] sampleBuffer = Array.Empty<short>();
        private short[] muffleBuffer = Array.Empty<short>();
        public OggSound(SoundManager owner, string filename, bool stream, ContentXElement xElement) : base(owner, filename,
            stream, true, xElement)
        {
            var reader = new VorbisReader(Filename);

            ALFormat = reader.Channels == 1 ? Al.FormatMono16 : Al.FormatStereo16;
            SampleRate = reader.SampleRate;

            if (stream)
            {
                streamReader = reader;
                return;
            }

            Loading = true;
            TaskPool.Add(
                $"LoadSamples {filename}",
                LoadSamples(reader),
                t =>
                {
                    reader.Dispose();
                    if (!t.TryGetResult(out TaskResult result))
                    {
                        return;
                    }
                    sampleBuffer = result.SampleBuffer;
                    muffleBuffer = result.MuffleBuffer;
                    playbackAmplitude = result.PlaybackAmplitude;
                    Owner.KillChannels(this); // prevents INVALID_OPERATION error
                    buffers?.Dispose(); buffers = null;
                    Loading = false;
                });
        }

        private readonly record struct TaskResult(
            short[] SampleBuffer,
            short[] MuffleBuffer,
            List<float> PlaybackAmplitude);

        private static async Task<TaskResult> LoadSamples(VorbisReader reader)
        {
            reader.DecodedPosition = 0;

            int bufferSize = (int)reader.TotalSamples * reader.Channels;

            float[] floatBuffer = new float[bufferSize];
            var sampleBuffer = new short[bufferSize];
            var muffledBuffer = new short[bufferSize];

            int readSamples = await Task.Run(() =>  reader.ReadSamples(floatBuffer, 0, bufferSize));

            var playbackAmplitude = new List<float>();
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

            CastBuffer(floatBuffer, sampleBuffer, readSamples);

            MuffleBuffer(floatBuffer, reader.SampleRate);

            CastBuffer(floatBuffer, muffledBuffer, readSamples);

            return new TaskResult(sampleBuffer, muffledBuffer, playbackAmplitude);
        }

        public override float GetAmplitudeAtPlaybackPos(int playbackPos)
        {
            if (playbackAmplitude == null || playbackAmplitude.Count == 0) { return 0.0f; }
            int index = playbackPos / AMPLITUDE_SAMPLE_COUNT;
            if (index < 0) { return 0.0f; }
            if (index >= playbackAmplitude.Count) { index = playbackAmplitude.Count - 1; }
            return playbackAmplitude[index];
        }

        private float[] streamFloatBuffer = null;
        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) { throw new Exception("Called FillStreamBuffer on a non-streamed sound!"); }
            if (streamReader == null) { throw new Exception("Called FillStreamBuffer when the reader is null!"); }

            if (samplePos >= MaxStreamSamplePos) { return 0; }

            samplePos /= streamReader.Channels * 2;
            streamReader.DecodedPosition = samplePos;

            if (streamFloatBuffer is null || streamFloatBuffer.Length < buffer.Length)
            {
                streamFloatBuffer = new float[buffer.Length];
            }
            int readSamples = streamReader.ReadSamples(streamFloatBuffer, 0, buffer.Length);
            //MuffleBuffer(floatBuffer, reader.Channels);
            CastBuffer(streamFloatBuffer, buffer, readSamples);

            return readSamples;
        }

        static void MuffleBuffer(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, 1600);
            filter.Process(buffer);
        }

        public override void InitializeAlBuffers()
        {
            if (buffers != null && SoundBuffers.BuffersGenerated < SoundBuffers.MaxBuffers)
            {
                FillAlBuffers();
            }
        }

        public override void FillAlBuffers()
        {
            if (Stream) { return; }
            if (sampleBuffer.Length == 0 || muffleBuffer.Length == 0) { return; }
            buffers ??= new SoundBuffers(this);
            if (!buffers.RequestAlBuffers()) { return; }

            Al.BufferData(buffers.AlBuffer, ALFormat, sampleBuffer,
                sampleBuffer.Length * sizeof(short), SampleRate);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }

            Al.BufferData(buffers.AlMuffledBuffer, ALFormat, muffleBuffer,
                muffleBuffer.Length * sizeof(short), SampleRate);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }
        }

        public override void Dispose()
        {
            if (Stream)
            {
                streamReader?.Dispose();
            }

            base.Dispose();
        }
    }
}
