#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.IO;
using System.Text;
using Barotrauma.Networking;
using Concentus.Structs;

namespace Barotrauma
{
    internal sealed class VoipServerDecoder
    {
        private readonly OpusDecoder decoder;
        private readonly VoipQueue queue;
        private int lastRetrievedBufferID;

        public float Amplitude { get; private set; }

        private readonly Client ownerClient;

        public VoipServerDecoder(VoipQueue q, Client owner)
        {
            ownerClient = owner;
            decoder = VoipConfig.CreateDecoder();
            queue = q;
            lastRetrievedBufferID = q.LatestBufferID;
        }

        private static bool debugVoip;
        /// <summary>
        /// When set to true the server will write VOIP into an audio file for debugging purposes.
        /// Useful if you're modifying this part of the code and want to be able to hear what the server "hears"
        /// </summary>
        public static bool DebugVoip
        {
            get => debugVoip;
            set
            {
#if !DEBUG
                debugVoip = false;
                if (value)
                {
                    DebugConsole.ThrowError("DebugVoip is only available in debug builds of the game");
                }
#else

                debugVoip = value;

                if (!value)
                {
                    if (GameMain.Server is null) { return; }
                    foreach (var c in GameMain.Server.ConnectedClients)
                    {
                        c.VoipServerDecoder.ClearStoredDebugSamples();
                    }
                }
#endif
            }
        }

        private readonly List<short[]> debugStoredSamples = new();

        private float debugWriteTimerBacking;
        private float DebugWriteTimer
        {
            get => debugWriteTimerBacking;
            set => debugWriteTimerBacking = Math.Clamp(value, min: 0, max: DebugWriteTimeout);
        }

        private bool shouldWriteDebugFile;
        private const float DebugWriteTimeout = 3f; // 3 seconds of no data before writing to file

        public void OnNewVoiceReceived()
        {
            float amplitude = 0.0f;
            for (int i = lastRetrievedBufferID + 1; i <= queue.LatestBufferID; i++)
            {
                queue.RetrieveBuffer(i, out int compressedSize, out byte[] compressedBuffer);
                if (compressedSize <= 0) { continue; }

                short[] buffer = new short[VoipConfig.BUFFER_SIZE];
                decoder.Decode(compressedBuffer, 0, compressedSize, buffer, 0, VoipConfig.BUFFER_SIZE);
                amplitude = Math.Max(amplitude, GetAmplitude(buffer));
                lastRetrievedBufferID = i;

                if (!DebugVoip) { continue; }
                lock (debugStoredSamples) { debugStoredSamples.Add(buffer); }
            }

            Amplitude = amplitude;

            if (DebugVoip)
            {
                DebugWriteTimer = DebugWriteTimeout;
            }
        }

        public void DebugUpdate(float deltaTime)
        {
            if (!DebugVoip) { return; }

            if (DebugWriteTimer > 0)
            {
                DebugWriteTimer -= deltaTime;
                if (DebugWriteTimer <= 0)
                {
                    shouldWriteDebugFile = true;
                }
                return;
            }

            if (!shouldWriteDebugFile) { return; }

            lock (debugStoredSamples)
            {
#if DEBUG
                WriteSamplesToWaveFile(debugStoredSamples,
                    filename: $"voip_{ownerClient.Name}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.wav",
                    sampleRate: VoipConfig.FREQUENCY,
                    channels: 1);
#endif

                debugStoredSamples.Clear();
                shouldWriteDebugFile = false;
            }
        }

        private static float GetAmplitude(short[] values)
        {
            float max = 0;
            foreach (short v in values)
            {
                max = Math.Max(max, ToolBox.ShortAudioSampleToFloat(v));
            }
            return max;
        }

        /// <summary>
        /// Writes the given audio samples to a wave file.
        /// </summary>
        /// <param name="samples">The audio samples to write.</param>
        /// <param name="filename">The name of the wave file to create.</param>
        /// <param name="sampleRate">The sample rate of the audio.</param>
        /// <param name="channels">The number of channels in the audio.</param>
        private static void WriteSamplesToWaveFile(IReadOnlyList<short[]> samples, string filename, int sampleRate, short channels)
        {
            if (!samples.Any()) { return; }

            var path = Path.Combine(Path.GetFullPath("AudioDebug"));
            if (!Directory.Exists(path))
            {
                var dir = Directory.CreateDirectory(path);
                if (dir is not { Exists: true }) { return; }
            }

            using var outFile = File.Create(Path.Combine(path, ToolBox.RemoveInvalidFileNameChars(filename)));

            if (outFile is null)
            {
                DebugConsole.ThrowError("Failed to create audio debug file");
                return;
            }

            // wave file format: https://docs.fileformat.com/audio/wav/
            using var writer = new System.IO.BinaryWriter(outFile);

            const short pcmFormat = 1; // PCM
            const short bitsPerSample = 16; // 16 bits in a short
            int byteRate = sampleRate * bitsPerSample * channels / 8;
            short blockAlign = (short)(bitsPerSample * channels / 8);

            // === FILE INFO === //
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            long sizePos = outFile.Position;
            writer.Write(0); // size of file, will be written later

            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt ")); // trailing space is required, not a typo

            writer.Write(16); // length of format header

            // === AUDIO FORMAT === //
            writer.Write(pcmFormat);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);

            // === SAMPLE DATA === //
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Flush();

            long dataPos = outFile.Position;
            writer.Write(0); // temporary data size

            foreach (var sample in samples)
            {
                foreach (var s in sample)
                {
                    writer.Write(s);
                }
            }

            writer.Flush();

            // write the file size
            writer.Seek((int)sizePos, System.IO.SeekOrigin.Begin);
            writer.Write((int)(outFile.Length - 8)); // spec says to subtract 8 bytes from the file size

            // write the data size
            writer.Seek((int)dataPos, System.IO.SeekOrigin.Begin);
            writer.Write((int)(outFile.Length - dataPos)); // size of the data only

            writer.Flush();
        }

        private void ClearStoredDebugSamples()
        {
            lock (debugStoredSamples)
            {
                debugStoredSamples.Clear();
            }
            DebugWriteTimer = 0;
            shouldWriteDebugFile = false;
        }
    }
}
