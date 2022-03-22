using Barotrauma.Sounds;
using Concentus.Structs;
using Microsoft.Xna.Framework;
using OpenAL;
using System;
using System.Diagnostics;
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

        private OpusEncoder encoder;

        public double LastdB
        {
            get;
            private set;
        }

        public double LastAmplitude
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

        public readonly bool CanDetectDisconnect;

        public bool Disconnected { get; private set; }

        public static void Create(string deviceName, UInt16? storedBufferID = null)
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
            Disconnected = false;

            encoder = VoipConfig.CreateEncoder();

            //set up capture device
            captureDevice = Alc.CaptureOpenDevice(deviceName, VoipConfig.FREQUENCY, Al.FormatMono16, VoipConfig.BUFFER_SIZE * 5);

            if (captureDevice == IntPtr.Zero)
            {
                DebugConsole.NewMessage("Alc.CaptureOpenDevice attempt 1 failed: error code " + Alc.GetError(IntPtr.Zero).ToString(), Color.Orange);
                //attempt using a smaller buffer size
                captureDevice = Alc.CaptureOpenDevice(deviceName, VoipConfig.FREQUENCY, Al.FormatMono16, VoipConfig.BUFFER_SIZE * 2);
            }

            if (captureDevice == IntPtr.Zero)
            {
                DebugConsole.NewMessage("Alc.CaptureOpenDevice attempt 2 failed: error code " + Alc.GetError(IntPtr.Zero).ToString(), Color.Orange);
                //attempt using the default device
                captureDevice = Alc.CaptureOpenDevice("", VoipConfig.FREQUENCY, Al.FormatMono16, VoipConfig.BUFFER_SIZE * 2);
            }

            if (captureDevice == IntPtr.Zero)
            {
                string errorCode = Alc.GetError(IntPtr.Zero).ToString();
                if (!GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "capturedevicenotfound"))
                {
                    GUI.SettingsMenuOpen = false;
                    new GUIMessageBox(TextManager.Get("Error"),
                        (TextManager.Get("VoipCaptureDeviceNotFound", returnNull: true) ?? "Could not start voice capture, suitable capture device not found.") + " (" + errorCode + ")")
                    {
                        UserData = "capturedevicenotfound"
                    };
                }
                GameAnalyticsManager.AddErrorEventOnce("Alc.CaptureDeviceOpenFailed", GameAnalyticsManager.ErrorSeverity.Error,
                    "Alc.CaptureDeviceOpen(" + deviceName + ") failed. Error code: " + errorCode);
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

            CanDetectDisconnect = Alc.IsExtensionPresent(captureDevice, "ALC_EXT_disconnect");
            alcError = Alc.GetError(captureDevice);
            if (alcError != Alc.NoError)
            {
                throw new Exception("Error determining if disconnect can be detected: " + alcError.ToString());
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

        IntPtr nativeBuffer;
        short[] uncompressedBuffer = new short[VoipConfig.BUFFER_SIZE];
        short[] prevUncompressedBuffer = new short[VoipConfig.BUFFER_SIZE];
        bool prevCaptured = true;
        int captureTimer;

        private void UpdateCapture()
        {
            Array.Copy(uncompressedBuffer, 0, prevUncompressedBuffer, 0, VoipConfig.BUFFER_SIZE);
            Array.Clear(uncompressedBuffer, 0, VoipConfig.BUFFER_SIZE);
            nativeBuffer = Marshal.AllocHGlobal(VoipConfig.BUFFER_SIZE * 2);
            try
            {
                while (capturing)
                {
                    int alcError;

                    if (CanDetectDisconnect)
                    {
                        Alc.GetInteger(captureDevice, Alc.EnumConnected, out int isConnected);
                        alcError = Alc.GetError(captureDevice);
                        if (alcError != Alc.NoError)
                        {
                            throw new Exception("Failed to determine if capture device is connected: " + alcError.ToString());
                        }

                        if (isConnected == 0)
                        {
                            DebugConsole.ThrowError("Capture device has been disconnected. You can select another available device in the settings.");
                            Disconnected = true;
                            break;
                        }
                    }

                    FillBuffer();

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
                    LastAmplitude = maxAmplitude;

                    bool allowEnqueue = overrideSound != null;
                    if (GameMain.WindowActive)
                    {
                        ForceLocal = captureTimer > 0 ? ForceLocal : GameMain.Config.UseLocalVoiceByDefault;
                        bool pttDown = false;
                        if ((PlayerInput.KeyDown(InputType.Voice) || PlayerInput.KeyDown(InputType.LocalVoice)) &&
                                GUI.KeyboardDispatcher.Subscriber == null)
                        {
                            pttDown = true;
                            if (PlayerInput.KeyDown(InputType.LocalVoice))
                            {
                                ForceLocal = true;
                            }
                            else
                            {
                                ForceLocal = false;
                            }
                        }
                        if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Activity)
                        {
                            if (dB > GameMain.Config.NoiseGateThreshold)
                            {
                                allowEnqueue = true;
                            }
                        }
                        else if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.PushToTalk)
                        {
                            if (pttDown)
                            {
                                allowEnqueue = true;
                            }
                        }
                    }

                    if (allowEnqueue || captureTimer > 0)
                    {
                        LastEnqueueAudio = DateTime.Now;
                        if (GameMain.Client?.Character != null)
                        {
                            var messageType = !ForceLocal && ChatMessage.CanUseRadio(GameMain.Client.Character, out _) ? ChatMessageType.Radio : ChatMessageType.Default;
                            GameMain.Client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);
                        }
                        //encode audio and enqueue it
                        lock (buffers)
                        {
                            if (!prevCaptured) //enqueue the previous buffer if not sent to avoid cutoff
                            {
                                int compressedCountPrev = encoder.Encode(prevUncompressedBuffer, 0, VoipConfig.BUFFER_SIZE, BufferToQueue, 0, VoipConfig.MAX_COMPRESSED_SIZE);
                                EnqueueBuffer(compressedCountPrev);
                            }
                            int compressedCount = encoder.Encode(uncompressedBuffer, 0, VoipConfig.BUFFER_SIZE, BufferToQueue, 0, VoipConfig.MAX_COMPRESSED_SIZE);
                            EnqueueBuffer(compressedCount);
                        }
                        captureTimer -= (VoipConfig.BUFFER_SIZE * 1000) / VoipConfig.FREQUENCY;
                        if (allowEnqueue)
                        {
                            captureTimer = GameMain.Config.VoiceChatCutoffPrevention;
                        }
                        prevCaptured = true;
                    }
                    else
                    {
                        captureTimer = 0;
                        prevCaptured = false;
                        //enqueue silence
                        lock (buffers)
                        {
                            EnqueueBuffer(0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"VoipCapture threw an exception. Disabling capture...", e);
                capturing = false;
            }
            finally
            {
                Marshal.FreeHGlobal(nativeBuffer);
            }
        }

        private Sound overrideSound;
        private int overridePos;
        private short[] overrideBuf = new short[VoipConfig.BUFFER_SIZE];

        private void FillBuffer()
        {
            if (overrideSound != null)
            {
                int totalSampleCount = 0;
                while (totalSampleCount < VoipConfig.BUFFER_SIZE)
                {
                    int sampleCount = overrideSound.FillStreamBuffer(overridePos, overrideBuf);
                    overridePos += sampleCount * 2;
                    Array.Copy(overrideBuf, 0, uncompressedBuffer, totalSampleCount, sampleCount);
                    totalSampleCount += sampleCount;

                    if (sampleCount == 0)
                    {
                        overridePos = 0;
                    }
                }
                int sleepMs = VoipConfig.BUFFER_SIZE * 800 / VoipConfig.FREQUENCY;
                Thread.Sleep(sleepMs - 1);
            }
            else
            {
                int sampleCount = 0;

                while (sampleCount < VoipConfig.BUFFER_SIZE)
                {
                    Alc.GetInteger(captureDevice, Alc.EnumCaptureSamples, out sampleCount);

                    int alcError = Alc.GetError(captureDevice);
                    if (alcError != Alc.NoError)
                    {
                        throw new Exception("Failed to determine sample count: " + alcError.ToString());
                    }

                    if (sampleCount < VoipConfig.BUFFER_SIZE)
                    {
                        int sleepMs = (VoipConfig.BUFFER_SIZE - sampleCount) * 800 / VoipConfig.FREQUENCY;
                        if (sleepMs >= 1)
                        {
                            Thread.Sleep(sleepMs);
                        }
                    }

                    if (!capturing) { return; }
                }

                Alc.CaptureSamples(captureDevice, nativeBuffer, VoipConfig.BUFFER_SIZE);
                Marshal.Copy(nativeBuffer, uncompressedBuffer, 0, uncompressedBuffer.Length);
            }
        }

        public void SetOverrideSound(string fileName)
        {
            overrideSound?.Dispose();
            if (string.IsNullOrEmpty(fileName))
            {
                overrideSound = null;
            }
            else
            {
                overrideSound = GameMain.SoundManager.LoadSound(fileName, true);
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
