using NVorbis;
using OpenAL;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Barotrauma.Sounds
{
    sealed class OggSound : Sound
    {


        private readonly VorbisReader streamReader;

        public long MaxStreamSamplePos => streamReader == null ? 0 : streamReader.TotalSamples * streamReader.Channels * sizeof(float);

        private List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        private float[] sampleBuffer = Array.Empty<float>();
        private float[] muffleBuffer = Array.Empty<float>();

        private readonly double durationSeconds;
        public override double? DurationSeconds => durationSeconds;

        public OggSound(SoundManager owner, string filename, bool stream, ContentXElement xElement) : base(owner, filename,
            stream, true, xElement)
        {
            var reader = new VorbisReader(Filename);
            durationSeconds = reader.TotalTime.TotalSeconds;

            ALFormat = reader.Channels == 1 ? Al.FormatMonoF32 : Al.FormatStereoF32;
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
            float[] SampleBuffer,
            float[] MuffleBuffer,
            List<float> PlaybackAmplitude);

        private static async Task<TaskResult> LoadSamples(VorbisReader reader)
        {
            reader.DecodedPosition = 0;

            int sampleCount = (int)reader.TotalSamples * reader.Channels;
            int bufferSize = sampleCount * sizeof(float);
            //by using ArrayPool for short lived buffers or larger then 1KB allocations we don't hammer the GC!
            float[] sampleBuffer = FloatArrayPool.RentZeroed(bufferSize);
            float[] muffledBuffer = FloatArrayPool.RentZeroed(bufferSize);
            int readSamples = await Task.Run(() =>  reader.ReadSamples(sampleBuffer, 0, bufferSize));
            Array.Copy(sampleBuffer, muffledBuffer, readSamples);
            var playbackAmplitude = new List<float>();
            int amplitudeWindowSize = reader.Channels * AMPLITUDE_SAMPLE_COUNT;
            for (int i = 0; i < sampleCount; i += amplitudeWindowSize)
            {
                float maxAmplitude = 0.0f;
                int end = Math.Min(i + amplitudeWindowSize, sampleCount);

                for (int j = i; j < end; j++)
                {
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sampleBuffer[j]));
                }

                playbackAmplitude.Add(maxAmplitude);
            }

            MuffleBuffer(muffledBuffer, reader.SampleRate);

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
        private int streamFloatBufferLength = 0;
        private float[] streamFloatBuffer = null;
        public override int FillStreamBuffer(int samplePos, float[] buffer)
        {
            if (!Stream) { throw new Exception("Called FillStreamBuffer on a non-streamed sound!"); }
            if (streamReader == null) { throw new Exception("Called FillStreamBuffer when the reader is null!"); }

            if (samplePos >= MaxStreamSamplePos) { return 0; }

            int framePos = samplePos / streamReader.Channels;
            streamReader.DecodedPosition = framePos;

            if (streamFloatBuffer is null || streamFloatBufferLength < buffer.Length)
            {
                if (streamFloatBuffer != null)
                {
                    FloatArrayPool.Return(streamFloatBuffer);
                }
                streamFloatBuffer = FloatArrayPool.RentZeroed(buffer.Length);
                streamFloatBufferLength = buffer.Length;
            }
            int readSamples = streamReader.ReadSamples(streamFloatBuffer, 0, buffer.Length);
            //MuffleBuffer(floatBuffer, reader.Channels);
            //CastBuffer(streamFloatBuffer, buffer, readSamples);
            Array.Copy(streamFloatBuffer, buffer, readSamples); 
            return readSamples;
        }

        static void MuffleBuffer(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, SoundPlayer.MuffleFilterFrequency);
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
                sampleBuffer.Length * 2, SampleRate);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }

            Al.BufferData(buffers.AlMuffledBuffer, ALFormat, muffleBuffer,
                muffleBuffer.Length * 2, SampleRate);

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
                FloatArrayPool.Return(sampleBuffer);
                FloatArrayPool.Return(muffleBuffer);
            }

            base.Dispose();
        }
    }
}
