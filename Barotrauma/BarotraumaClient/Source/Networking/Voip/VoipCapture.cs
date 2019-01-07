using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidgren.Network;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    class VoipCapture : VoipQueue, IDisposable
    {
        public static VoipCapture Instance
        {
            get;
            private set;
        }

        private IntPtr captureDevice;

        private Thread captureThread;

        private bool capturing;

        public double LastdB
        {
            get;
            private set;
        }

        public override byte QueueID
        {
            get
            {
                return GameMain.Client?.ID ?? 0;
            }
            protected set
            {
                //do nothing
            }
        }

        public static void Create(string deviceName, UInt16? storedBufferID=null)
        {
            if (Instance != null)
            {
                throw new Exception("Tried to instance more than one VoipCapture object");
            }

            Instance = new VoipCapture(deviceName);
            Instance.LatestBufferID = storedBufferID??BUFFER_COUNT-1;
        }

        private VoipCapture(string deviceName) : base(GameMain.Client?.ID ?? 0,true,false) {
            //set up capture device
            captureDevice = Alc.CaptureOpenDevice(deviceName, VoipConfig.FREQUENCY, ALFormat.Mono16, VoipConfig.BUFFER_SIZE * 5);

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

            Alc.CaptureStart(captureDevice);
            alcError = Alc.GetError(captureDevice);
            if (alcError != AlcError.NoError)
            {
                throw new Exception("Failed to start capturing: " + alcError.ToString());
            }

            VoipConfig.SetupEncoding();

            capturing = true;
            captureThread = new Thread(UpdateCapture);
            captureThread.IsBackground = true;
            captureThread.Start();
        }

        void UpdateCapture()
        {
            short[] uncompressedBuffer = new short[VoipConfig.BUFFER_SIZE];
            while (capturing)
            {
                int sampleCount = 0;
                AlcError alcError;
                Alc.GetInteger(captureDevice, AlcGetInteger.CaptureSamples, 1, out sampleCount);
                alcError = Alc.GetError(captureDevice);
                if (alcError != AlcError.NoError)
                {
                    throw new Exception("Failed to determine sample count: " + alcError.ToString());
                }

                if (sampleCount < VoipConfig.BUFFER_SIZE)
                {
                    int sleepMs = (VoipConfig.BUFFER_SIZE - sampleCount) * 800 / VoipConfig.FREQUENCY;
                    if (sleepMs < 5) sleepMs = 5;
                    Thread.Sleep(sleepMs);
                    continue;
                }

                GCHandle handle = GCHandle.Alloc(uncompressedBuffer, GCHandleType.Pinned);
                try
                {
                    Alc.CaptureSamples(captureDevice, handle.AddrOfPinnedObject(), VoipConfig.BUFFER_SIZE);
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

                double maxAmplitude = 0.0f;
                for (int i=0;i<VoipConfig.BUFFER_SIZE;i++)
                {
                    double sampleVal = (double)uncompressedBuffer[i] / (double)short.MaxValue;
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sampleVal));
                }
                double dB = Math.Min(20*Math.Log10(maxAmplitude),0.0);

                LastdB = dB;

                bool allowEnqueue = false;
                if (GameMain.WindowActive)
                {
                    if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Activity)
                    {
                        if (dB > GameMain.Config.NoiseGateThreshold)
                        {
                            allowEnqueue = true;
                        }
                    }
                    else if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.PushToTalk)
                    {
                        if (PlayerInput.KeyDown(InputType.Voice))
                        {
                            allowEnqueue = true;
                        }
                    }
                }

                if (allowEnqueue)
                {
                    //encode audio and enqueue it
                    lock (buffers)
                    {
                        int compressedCount = VoipConfig.Encoder.Encode(uncompressedBuffer, 0, VoipConfig.BUFFER_SIZE, BufferToQueue, 0, VoipConfig.MAX_COMPRESSED_SIZE);
                        EnqueueBuffer(compressedCount);
                    }
                }
                else
                {
                    //enqueue silence
                    lock (buffers)
                    {
                        EnqueueBuffer(0);
                    }
                }

                Thread.Sleep(VoipConfig.BUFFER_SIZE * 800 / VoipConfig.FREQUENCY);
            }
        }

        public override void Write(NetBuffer msg)
        {
            lock (buffers)
            {
                base.Write(msg);
            }
        }

        public override void Read(NetBuffer msg)
        {
            throw new Exception("Called Read on a VoipCapture object");
        }

        public override void Dispose()
        {
            Instance = null;
            capturing = false;
            captureThread.Join();
            captureThread = null;
        }
    }
}
