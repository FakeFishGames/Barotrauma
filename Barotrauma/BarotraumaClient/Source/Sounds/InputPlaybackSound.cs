using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;
using System.Threading;
using Concentus.Structs;
using Concentus.Enums;

namespace Barotrauma.Sounds
{
    public class InputPlaybackSound : Sound
    {
        private IntPtr captureDevice;

        OpusEncoder encoder;
        OpusDecoder decoder;
        byte[] compressedBuffer;

        private const int FREQUENCY = 48000; //not amazing, but not bad audio quality
        private const int BUFFER_SIZE = 2880; //60ms window, the max Opus seems to support
        private const int MAX_COMPRESSED_SIZE = 120; //amount of bytes we expect each 60ms of audio to fit in
        private const int REPEAT_MARGIN = 1440; //30ms window of audio to repeat in case not enough audio is available
        private short[] repeatBuffer;

        SoundChannel soundChannel;

        public InputPlaybackSound(SoundManager owner) : base(owner, "input", true, true)
        {
            //set up capture device
            captureDevice = Alc.CaptureOpenDevice(null, FREQUENCY, ALFormat.Mono16, BUFFER_SIZE * 5);
            repeatBuffer = new short[REPEAT_MARGIN];
            ALError alError = AL.GetError();
            AlcError alcError = Alc.GetError(captureDevice);
            if (alcError != AlcError.NoError)
            {
                throw new Exception("Failed to open capture device: " + alcError.ToString() + " (ALC)");
            }
            if (alError != ALError.NoError)
            {
                throw new Exception("Failed to open capture device: " + alError.ToString() + " (AL)");
            }

            ALFormat = ALFormat.Mono16;
            SampleRate = FREQUENCY;

            soundChannel = null;

            //set up Opus
            encoder = new OpusEncoder(FREQUENCY, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_AUTO;
            encoder.Bitrate = 8 * MAX_COMPRESSED_SIZE * FREQUENCY / BUFFER_SIZE;
            encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

            decoder = new OpusDecoder(FREQUENCY, 1);

            compressedBuffer = new byte[MAX_COMPRESSED_SIZE];

            Alc.CaptureStart(captureDevice);
            alcError = Alc.GetError(captureDevice);
            if (alcError != AlcError.NoError)
            {
                throw new Exception("Failed to start capturing: " + alcError.ToString());
            }
            SoundChannel chn = new SoundChannel(this, 1.0f, null, 0.4f, 1.0f, "inputPlayback", false);
            soundChannel = chn;
        }

        public override SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(Vector3? position, float gain, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(float gain)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play()
        {
            throw new InvalidOperationException();
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            int sampleCount = 0;
            AlcError alcError;
            Alc.GetInteger(captureDevice, AlcGetInteger.CaptureSamples, 1, out sampleCount);
            alcError = Alc.GetError(captureDevice);
            if (alcError != AlcError.NoError)
            {
                throw new Exception("Failed to determine sample count: " + alcError.ToString());
            }

            if (sampleCount < BUFFER_SIZE)
            {
                //Console.WriteLine("USING REPEAT MARGIN");
                repeatBuffer.CopyTo(buffer, 0);
                return REPEAT_MARGIN * 2;
            }

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Alc.CaptureSamples(captureDevice, handle.AddrOfPinnedObject(), BUFFER_SIZE);
            }
            finally
            {
                handle.Free();
            }

            alcError = Alc.GetError(captureDevice);
            if (alcError != AlcError.NoError)
            {
                throw new Exception("Failed to capture samples: " + alcError.ToString());
            }

            //encode and decode audio to test compression ratio and quality
            int compressedCount = encoder.Encode(buffer, 0, BUFFER_SIZE, compressedBuffer, 0, MAX_COMPRESSED_SIZE);
            Console.WriteLine("UNCOMPRESSED: " + BUFFER_SIZE.ToString() + "; COMPRESSED: " + compressedCount.ToString());
            decoder.Decode(compressedBuffer, 0, compressedCount, buffer, 0, BUFFER_SIZE);

            //copy audio into repeat buffer because this should always be up to date
            Array.Copy(buffer, BUFFER_SIZE - REPEAT_MARGIN, repeatBuffer, 0, REPEAT_MARGIN);

            return BUFFER_SIZE * 2;
        }

        public override void Dispose()
        {
            if (soundChannel != null)
            {
                soundChannel.Dispose();
                Alc.CaptureStop(captureDevice);
                AlcError alcError = Alc.GetError(captureDevice);
                if (alcError != AlcError.NoError)
                {
                    throw new Exception("Failed to stop capturing: " + alcError.ToString());
                }
            }
            base.Dispose();
        }
    }
}
