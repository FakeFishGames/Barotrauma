using Lidgren.Network;
using Microsoft.Xna.Framework;
using OpenAL;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

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
        
        public float Gain
        {
            get { return GameMain.Config?.MicrophoneVolume ?? 1.0f; }
        }

        public DateTime LastEnqueueAudio;

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

            var capture = new VoipCapture(deviceName)
            {
                LatestBufferID = storedBufferID ?? BUFFER_COUNT - 1
            };
            if (capture.captureDevice != IntPtr.Zero)
            {
                Instance = capture;
            }
        }

        private VoipCapture(string deviceName) : base(GameMain.Client?.ID ?? 0, true, false)
        {
            VoipConfig.SetupEncoding();

            //set up capture device
            captureDevice = Alc.CaptureOpenDevice(deviceName, VoipConfig.FREQUENCY, Al.FormatMono16, VoipConfig.BUFFER_SIZE * 5);

            if (captureDevice == IntPtr.Zero)
            {
                if (!GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "capturedevicenotfound"))
                {
                    GUI.SettingsMenuOpen = false;
                    new GUIMessageBox(TextManager.Get("Error"), 
                        TextManager.Get("VoipCaptureDeviceNotFound", returnNull: true) ?? "Could not start voice capture, suitable capture device not found.")
                    {
                        UserData = "capturedevicenotfound"
                    };
                }
                GameMain.Config.VoiceSetting = GameSettings.VoiceMode.Disabled;
                Instance?.Dispose();
                Instance = null;
                return;
            }

            int alError = Al.GetError();
            int alcError = Alc.GetError(captureDevice);
            if (alcError != Alc.NoError)
            {
                throw new Exception("Failed to open capture device: " + alcError.ToString() + " (ALC)");
            }
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to open capture device: " + alError.ToString() + " (AL)");
            }

            Alc.CaptureStart(captureDevice);
            alcError = Alc.GetError(captureDevice);
            if (alcError != Alc.NoError)
            {
                throw new Exception("Failed to start capturing: " + alcError.ToString());
            }

            capturing = true;
            captureThread = new Thread(UpdateCapture)
            {
                IsBackground = true,
                Name = "VoipCapture"
            };
            captureThread.Start();
        }

        public static void ChangeCaptureDevice(string deviceName)
        {
            GameMain.Config.VoiceCaptureDevice = deviceName;

            if (Instance != null)
            {
                UInt16 storedBufferID = Instance.LatestBufferID;
                Instance.Dispose();
                Create(GameMain.Config.VoiceCaptureDevice, storedBufferID);
            }
        }

        void UpdateCapture()
        {
            short[] uncompressedBuffer = new short[VoipConfig.BUFFER_SIZE];
            while (capturing)
            {
                int alcError;
                Alc.GetInteger(captureDevice, Alc.EnumCaptureSamples, out int sampleCount);

                alcError = Alc.GetError(captureDevice);
                if (alcError != Alc.NoError)
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
                if (alcError != Alc.NoError)
                {
                    throw new Exception("Failed to capture samples: " + alcError.ToString());
                }

                double maxAmplitude = 0.0f;
                for (int i = 0; i < VoipConfig.BUFFER_SIZE; i++)
                {
                    uncompressedBuffer[i] = (short)MathHelper.Clamp((uncompressedBuffer[i] * Gain), -short.MaxValue, short.MaxValue);
                    double sampleVal = uncompressedBuffer[i] / (double)short.MaxValue;
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sampleVal));                    
                }
                double dB = Math.Min(20 * Math.Log10(maxAmplitude), 0.0);

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
                        if (PlayerInput.KeyDown(InputType.Voice) && GUI.KeyboardDispatcher.Subscriber == null)
                        {
                            allowEnqueue = true;
                        }
                    }
                }

                if (allowEnqueue)
                {
                    LastEnqueueAudio = DateTime.Now;
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

                Thread.Sleep(10);
            }
        }

        public override void Write(NetBuffer msg)
        {
            lock (buffers)
            {
                base.Write(msg);
            }
        }

        public override void Dispose()
        {
            Instance = null;
            capturing = false;
            captureThread?.Join();
            captureThread = null;
        }
    }
}
