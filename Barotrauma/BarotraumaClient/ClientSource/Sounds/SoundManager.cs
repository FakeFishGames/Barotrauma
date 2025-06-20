﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Xml.Linq;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Linq;
using Barotrauma.IO;

namespace Barotrauma.Sounds
{
    class SoundManager : IDisposable
    {
        public const int SourceCount = 32;
        public static readonly Identifier SoundCategoryDefault = "default".ToIdentifier();
        public static readonly Identifier SoundCategoryUi = "ui".ToIdentifier();
        public static readonly Identifier SoundCategoryWaterAmbience = "waterambience".ToIdentifier();
        public static readonly Identifier SoundCategoryMusic = "music".ToIdentifier();
        public static readonly Identifier SoundCategoryVoip = "voip".ToIdentifier();

        public bool Disabled
        {
            get;
            private set;
        }
        
        private IntPtr alcDevice;
        private IntPtr alcContext;
        
        public enum SourcePoolIndex
        {
            Default = 0,
            Voice = 1
        }
        private readonly SoundSourcePool[] sourcePools;
        
        private readonly List<Sound> loadedSounds;
        public IReadOnlyList<Sound> LoadedSounds => loadedSounds;

        private readonly SoundChannel[][] playingChannels = new SoundChannel[2][];
        private readonly object threadDeathMutex = new object();

        public bool CanDetectDisconnect { get; private set; }

        public bool Disconnected { get; private set; }

        private Thread updateChannelsThread;

        private Vector3 listenerPosition;
        public Vector3 ListenerPosition
        {
            get { return listenerPosition; }
            set
            {
                if (Disabled) { return; }
                listenerPosition = value;
                Al.Listener3f(Al.Position,value.X,value.Y,value.Z);
                int alError = Al.GetError();
                if (alError != Al.NoError && !GameMain.IsExiting)
                {
                    throw new Exception("Failed to set listener position: " + Al.GetErrorString(alError));
                }
            }
        }

        private readonly float[] listenerOrientation = new float[6];
        public Vector3 ListenerTargetVector
        {
            get { return new Vector3(listenerOrientation[0], listenerOrientation[1], listenerOrientation[2]); }
            set
            {
                if (Disabled) { return; }
                listenerOrientation[0] = value.X; listenerOrientation[1] = value.Y; listenerOrientation[2] = value.Z;
                Al.Listenerfv(Al.Orientation, listenerOrientation);
                int alError = Al.GetError();
                if (alError != Al.NoError && !GameMain.IsExiting)
                {
                    throw new Exception("Failed to set listener target vector: " + Al.GetErrorString(alError));
                }
            }
        }
        public Vector3 ListenerUpVector
        {
            get { return new Vector3(listenerOrientation[3], listenerOrientation[4], listenerOrientation[5]); }
            set
            {
                if (Disabled) { return; }
                listenerOrientation[3] = value.X; listenerOrientation[4] = value.Y; listenerOrientation[5] = value.Z;
                Al.Listenerfv(Al.Orientation, listenerOrientation);
                int alError = Al.GetError();
                if (alError != Al.NoError && !GameMain.IsExiting)
                {
                    throw new Exception("Failed to set listener up vector: " + Al.GetErrorString(alError));
                }
            }
        }

        private float listenerGain;
        public float ListenerGain
        {
            get { return listenerGain; }
            set
            {
                if (Disabled) { return; }
                if (Math.Abs(ListenerGain - value) < 0.001f) { return; }
                listenerGain = value;
                Al.Listenerf(Al.Gain, listenerGain);
                int alError = Al.GetError();
                if (alError != Al.NoError && !GameMain.IsExiting)
                {
                    throw new Exception("Failed to set listener gain: " + Al.GetErrorString(alError));
                }
            }
        }
        
        public float PlaybackAmplitude
        {
            get
            {
                if (Disabled) { return 0.0f; }
                float aggregateAmplitude = 0.0f;
                //NOTE: this is obviously not entirely accurate;
                //It assumes a linear falloff model, and assumes that audio
                //is simply added together to produce the final result.
                //Adjustments may be needed under certain scenarios.
                for (int i = 0; i < 2; i++)
                {
                    foreach (SoundChannel soundChannel in playingChannels[i].Where(ch => ch != null))
                    {
                        float amplitude = soundChannel.CurrentAmplitude;
                        amplitude *= soundChannel.Gain;
                        float dist = Vector3.Distance(ListenerPosition, soundChannel.Position ?? ListenerPosition);
                        if (dist > soundChannel.Near)
                        {
                            amplitude *= 1.0f - Math.Min(1.0f, (dist - soundChannel.Near) / (soundChannel.Far - soundChannel.Near));
                        }
                        aggregateAmplitude += amplitude;
                    }
                }
                return aggregateAmplitude;
            }
        }

        public float CompressionDynamicRangeGain { get; private set; }

        private float voipAttenuatedGain;
        private double lastAttenuationTime;
        public float VoipAttenuatedGain
        {
            get { return voipAttenuatedGain; }
            set
            {
                lastAttenuationTime = Timing.TotalTime;
                voipAttenuatedGain = value;
            }
        }

        public int LoadedSoundCount
        {
            get { return loadedSounds.Count; }
        }
        public int UniqueLoadedSoundCount
        {
            get { return loadedSounds.Select(s => s.Filename).Distinct().Count(); }
        }

        private class CategoryModifier
        {
            public float[] GainMultipliers;
            public bool Muffle;

            public CategoryModifier(int gainMultiplierIndex, float gain, bool muffle)
            {
                Muffle = muffle;
                GainMultipliers = new float[gainMultiplierIndex + 1];
                for (int i = 0; i < GainMultipliers.Length; i++)
                {
                    if (i == gainMultiplierIndex)
                    {
                        GainMultipliers[i] = gain;
                    }
                    else
                    {
                        GainMultipliers[i] = 1.0f;
                    }
                }
            }

            public void SetGainMultiplier(int index, float gain)
            {
                if (GainMultipliers.Length < index + 1)
                {
                    int oldLength = GainMultipliers.Length;
                    Array.Resize(ref GainMultipliers, index + 1);
                    for (int i = oldLength; i < GainMultipliers.Length; i++)
                    {
                        GainMultipliers[i] = 1.0f;
                    }
                }
                GainMultipliers[index] = gain;
            }
        }

        private readonly Dictionary<Identifier, CategoryModifier> categoryModifiers = new Dictionary<Identifier, CategoryModifier>();

        public SoundManager()
        {
            loadedSounds = new List<Sound>();
            updateChannelsThread = null;

            sourcePools = new SoundSourcePool[2];
            playingChannels[(int)SourcePoolIndex.Default] = new SoundChannel[SourceCount];
            playingChannels[(int)SourcePoolIndex.Voice] = new SoundChannel[16];

            string deviceName = GameSettings.CurrentConfig.Audio.AudioOutputDevice;

            if (string.IsNullOrEmpty(deviceName))
            {
                deviceName = Alc.GetString((IntPtr)null, Alc.DefaultDeviceSpecifier);
            }

#if (!OSX)
            var audioDeviceNames = Alc.GetStringList((IntPtr)null, Alc.AllDevicesSpecifier);
            if (audioDeviceNames.Any() && !audioDeviceNames.Any(n => n.Equals(deviceName, StringComparison.OrdinalIgnoreCase)))
            {
                deviceName = audioDeviceNames[0];
            }
#endif
            if (GameSettings.CurrentConfig.Audio.AudioOutputDevice != deviceName)
            {
                SetAudioOutputDevice(deviceName);
            }

            InitializeAlcDevice(deviceName);

            ListenerPosition = Vector3.Zero;
            ListenerTargetVector = new Vector3(0.0f, 0.0f, 1.0f);
            ListenerUpVector = new Vector3(0.0f, -1.0f, 0.0f);

            CompressionDynamicRangeGain = 1.0f;
        }

        private static void SetAudioOutputDevice(string deviceName)
        {
            var config = GameSettings.CurrentConfig;
            config.Audio.AudioOutputDevice = deviceName;
            GameSettings.SetCurrentConfig(config);
        }
        
        public bool InitializeAlcDevice(string deviceName)
        {
            ReleaseResources(true);

            DebugConsole.NewMessage($"Attempting to open ALC device \"{deviceName}\"");

            alcDevice = IntPtr.Zero;
            int alcError;
            for (int i = 0; i < 3; i++)
            {
                alcDevice = Alc.OpenDevice(deviceName);
                if (alcDevice == IntPtr.Zero)
                {
                    alcError = Alc.GetError(IntPtr.Zero);
                    DebugConsole.NewMessage($"ALC device initialization attempt #{i + 1} failed: device is null (error code {Alc.GetErrorString(alcError)})");
                    if (!string.IsNullOrEmpty(deviceName))
                    {
                        deviceName = null;
                        DebugConsole.NewMessage($"Switching to default device...");
                    }
                }
                else
                {
                    alcError = Alc.GetError(alcDevice);
                    if (alcError != Alc.NoError)
                    {
                        DebugConsole.NewMessage($"ALC device initialization attempt #{i + 1} failed: error code {Alc.GetErrorString(alcError)}");
                        bool closed = Alc.CloseDevice(alcDevice);
                        if (!closed)
                        {
                            DebugConsole.NewMessage($"Failed to close ALC device");
                        }
                        alcDevice = IntPtr.Zero;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            if (alcDevice == IntPtr.Zero)
            {
                DebugConsole.ThrowError("ALC device creation failed too many times!");
                Disabled = true;
                return false;
            }

            CanDetectDisconnect = Alc.IsExtensionPresent(alcDevice, "ALC_EXT_disconnect");
            alcError = Alc.GetError(alcDevice);
            if (alcError != Alc.NoError)
            {
                DebugConsole.ThrowError("Error determining if disconnect can be detected: " + alcError.ToString() + ". Disabling audio playback...");
                Disabled = true;
                return false;
            }

            Disconnected = false;

            int[] alcContextAttrs = new int[] { };
            alcContext = Alc.CreateContext(alcDevice, alcContextAttrs);
            if (alcContext == null)
            {
                DebugConsole.ThrowError("Failed to create an ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + "). Disabling audio playback...");
                Disabled = true;
                return false;
            }

            if (!Alc.MakeContextCurrent(alcContext))
            {
                DebugConsole.ThrowError("Failed to assign the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + "). Disabling audio playback...");
                Disabled = true;
                return false;
            }

            alcError = Alc.GetError(alcDevice);
            if (alcError != Alc.NoError)
            {
                DebugConsole.ThrowError("Error after assigning ALC context: " + Alc.GetErrorString(alcError) + ". Disabling audio playback...");
                Disabled = true;
                return false;
            }

            Al.DistanceModel(Al.LinearDistanceClamped);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Error setting distance model: " + Al.GetErrorString(alError) + ". Disabling audio playback...");
                Disabled = true;
                return false;
            }

            sourcePools[(int)SourcePoolIndex.Default] = new SoundSourcePool(SourceCount);
            sourcePools[(int)SourcePoolIndex.Voice] = new SoundSourcePool(16);

            ReloadSounds();

            Disabled = false;

            return true;
        }

        public Sound LoadSound(string filename, bool stream = false)
        {
            if (Disabled) { return null; }

            if (!File.Exists(filename))
            {
                throw new System.IO.FileNotFoundException("Sound file \"" + filename + "\" doesn't exist!");
            }

#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            Sound newSound = new OggSound(this, filename, stream, null);
            lock (loadedSounds)
            {
                loadedSounds.Add(newSound);
            }
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"Loaded sound \"{filename}\" ({sw.ElapsedMilliseconds} ms).");
#endif
            return newSound;
        }

        public Sound LoadSound(ContentXElement element, bool stream = false, string overrideFilePath = null)
        {
            if (Disabled) { return null; }

            string filePath = overrideFilePath ?? element.GetAttributeContentPath("file")?.Value ?? "";
            if (!File.Exists(filePath))
            {
                throw new System.IO.FileNotFoundException($"Sound file \"{filePath}\" doesn't exist! Content package \"{(element.ContentPackage?.Name ?? "Unknown")}\".");
            }

            var newSound = new OggSound(this, filePath, stream, xElement: element)
            {
                BaseGain = element.GetAttributeFloat("volume", 1.0f)
            };
            float range = element.GetAttributeFloat("range", 1000.0f);
            newSound.BaseNear = range * 0.4f;
            newSound.BaseFar = range;

            lock (loadedSounds)
            {
                loadedSounds.Add(newSound);
            }
            return newSound;
        }

        public SoundChannel GetSoundChannelFromIndex(SourcePoolIndex poolIndex, int ind)
        {
            if (Disabled || ind < 0 || ind >= playingChannels[(int)poolIndex].Length) return null;
            return playingChannels[(int)poolIndex][ind];
        }

        public uint GetSourceFromIndex(SourcePoolIndex poolIndex, int srcInd)
        {
            if (Disabled || srcInd < 0 || srcInd >= sourcePools[(int)poolIndex].ALSources.Length) return 0;

            if (!Al.IsSource(sourcePools[(int)poolIndex].ALSources[srcInd]))
            {
                throw new Exception("alSources[" + srcInd.ToString() + "] is invalid!");
            }

            return sourcePools[(int)poolIndex].ALSources[srcInd];
        }

        public int AssignFreeSourceToChannel(SoundChannel newChannel)
        {
            if (Disabled) { return -1; }

            //remove a channel that has stopped
            //or hasn't even been assigned
            int poolIndex = (int)newChannel.Sound.SourcePoolIndex;

            lock (playingChannels[poolIndex])
            {
                for (int i = 0; i < playingChannels[poolIndex].Length; i++)
                {
                    if (playingChannels[poolIndex][i] == null || !playingChannels[poolIndex][i].IsPlaying)
                    {
                        if (playingChannels[poolIndex][i] != null) { playingChannels[poolIndex][i].Dispose(); }
                        playingChannels[poolIndex][i] = newChannel;
                        return i;
                    }
                }
            }

            //we couldn't get a free source to assign to this channel!
            return -1;
        }

#if DEBUG
        public void DebugSource(int ind)
        {
            for (int i = 0; i < sourcePools[0].ALSources.Length; i++)
            {
                Al.Sourcef(sourcePools[0].ALSources[i], Al.MaxGain, i == ind ? 1.0f : 0.0f);
                Al.Sourcef(sourcePools[0].ALSources[i], Al.MinGain, 0.0f);
            }
        }
#endif

        public bool IsPlaying(Sound sound)
        {
            if (Disabled) { return false; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) return true;
                    }
                }
            }
            return false;
        }

        public int CountPlayingInstances(Sound sound)
        {
            if (Disabled) { return 0; }
            int count = 0;
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound.Filename == sound.Filename)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) { count++; };
                    }
                }
            }
            return count;
        }

        public SoundChannel GetChannelFromSound(Sound sound)
        {
            if (Disabled) { return null; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i] != null && 
                        playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        if (playingChannels[(int)sound.SourcePoolIndex][i].IsPlaying) return playingChannels[(int)sound.SourcePoolIndex][i];
                    }
                }
            }
            return null;
        }

        public void KillChannels(Sound sound)
        {
            if (Disabled) { return; }
            lock (playingChannels[(int)sound.SourcePoolIndex])
            {
                for (int i = 0; i < playingChannels[(int)sound.SourcePoolIndex].Length; i++)
                {
                    if (playingChannels[(int)sound.SourcePoolIndex][i]!=null && playingChannels[(int)sound.SourcePoolIndex][i].Sound == sound)
                    {
                        playingChannels[(int)sound.SourcePoolIndex][i]?.Dispose();
                        playingChannels[(int)sound.SourcePoolIndex][i] = null;
                    }
                }
            }
        }

        public void RemoveSound(Sound sound)
        {
            lock (loadedSounds)
            {
                for (int i = 0; i < loadedSounds.Count; i++)
                {
                    if (loadedSounds[i] == sound)
                    {
                        loadedSounds.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        public void MoveSoundToPosition(Sound sound, int pos)
        {
            lock (loadedSounds)
            {
                int index = loadedSounds.IndexOf(sound);
                if (index >= 0)
                {
                    loadedSounds.SiftElement(index, pos);
                }
            }
        }

        public void SetCategoryGainMultiplier(Identifier category, float gain, int index=0)
        {
            if (Disabled) { return; }
            lock (categoryModifiers)
            {
                if (!categoryModifiers.ContainsKey(category))
                {
                    categoryModifiers.Add(category, new CategoryModifier(index, gain, false));
                }
                else
                {
                    categoryModifiers[category].SetGainMultiplier(index, gain);
                }
            }

            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        if (playingChannels[i][j] != null && playingChannels[i][j].IsPlaying)
                        {
                            playingChannels[i][j].Gain = playingChannels[i][j].Gain; //force all channels to recalculate their gain
                        }
                    }
                }
            }
        }

        public float GetCategoryGainMultiplier(Identifier category, int index = -1)
        {
            if (Disabled) { return 0.0f; }
            lock (categoryModifiers)
            {
                if (categoryModifiers == null || !categoryModifiers.TryGetValue(category, out CategoryModifier categoryModifier)) { return 1.0f; }
                if (index < 0)
                {
                    float accumulatedMultipliers = 1.0f;
                    for (int i = 0; i < categoryModifier.GainMultipliers.Length; i++)
                    {
                        accumulatedMultipliers *= categoryModifier.GainMultipliers[i];
                    }
                    return accumulatedMultipliers;
                }
                else
                {
                    return categoryModifier.GainMultipliers[index];
                }
            }
        }

        public void SetCategoryMuffle(Identifier category, bool muffle)
        {
            if (Disabled) { return; }

            lock (categoryModifiers)
            {
                if (!categoryModifiers.ContainsKey(category))
                {
                    categoryModifiers.Add(category, new CategoryModifier(0, 1.0f, muffle));
                }
                else
                {
                    categoryModifiers[category].Muffle = muffle;
                }
            }

            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        if (playingChannels[i][j] != null && playingChannels[i][j].IsPlaying)
                        {
                            if (playingChannels[i][j]?.Category == category) { playingChannels[i][j].Muffled = muffle; }
                        }
                    }
                }
            }
        }

        public bool GetCategoryMuffle(Identifier category)
        {
            if (Disabled) { return false; }

            lock (categoryModifiers)
            {
                if (categoryModifiers == null || !categoryModifiers.TryGetValue(category, out CategoryModifier categoryModifier)) { return false; }
                return categoryModifier.Muffle;
            }
        }

        public void Update()
        {
            if (Disconnected || Disabled) { return; }

            if (CanDetectDisconnect)
            {
                Alc.GetInteger(alcDevice, Alc.EnumConnected, out int isConnected);
                int alcError = Alc.GetError(alcDevice);
                if (alcError != Alc.NoError)
                {
                    throw new Exception("Failed to determine if device is connected: " + alcError.ToString());
                }

                if (isConnected == 0)
                {
                    if (!GameMain.Instance.HasLoaded)
                    {
                        //wait for loading to finish so we don't start releasing and reloading sounds when they're being loaded,
                        //or throw an error mid-loading that'd prevent the content package from being enabled
                        return;
                    }
                    DebugConsole.ThrowError("Playback device has been disconnected. You can select another available device in the settings.");
                    SetAudioOutputDevice("<disconnected>");
                    Disconnected = true;
                    TryRefreshDevice();
                    return;
                }
            }

            if (GameMain.Client != null && GameSettings.CurrentConfig.Audio.VoipAttenuationEnabled)
            {
                if (Timing.TotalTime > lastAttenuationTime+0.2)
                {
                    voipAttenuatedGain = voipAttenuatedGain * 0.9f + 0.1f;
                }
            }
            else
            {
                voipAttenuatedGain = 1.0f;
            }
            SetCategoryGainMultiplier(SoundCategoryDefault, VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier(SoundCategoryUi, VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier(SoundCategoryWaterAmbience, VoipAttenuatedGain, 1);
            SetCategoryGainMultiplier(SoundCategoryMusic, VoipAttenuatedGain, 1);

            if (GameSettings.CurrentConfig.Audio.DynamicRangeCompressionEnabled)
            {
                float targetGain = (Math.Min(1.0f, 1.0f / PlaybackAmplitude) - 1.0f) * 0.5f + 1.0f;
                if (targetGain < CompressionDynamicRangeGain)
                {
                    //if the target gain is lower than the current gain, lower the current gain immediately to prevent clipping
                    CompressionDynamicRangeGain = targetGain;
                }
                else
                {
                    //otherwise, let it rise back smoothly
                    CompressionDynamicRangeGain = (targetGain) * 0.05f + CompressionDynamicRangeGain * 0.95f;
                }
            }
            else
            {
                CompressionDynamicRangeGain = 1.0f;
            }

            if (updateChannelsThread == null || updateChannelsThread.ThreadState.HasFlag(ThreadState.Stopped))
            {
                bool startedStreamThread = false;
                for (int i = 0; i < playingChannels.Length; i++)
                {
                    lock (playingChannels[i])
                    {
                        for (int j = 0; j < playingChannels[i].Length; j++)
                        {
                            if (playingChannels[i][j] == null) { continue; }
                            if (playingChannels[i][j].IsStream && playingChannels[i][j].IsPlaying)
                            {
                                InitUpdateChannelThread();
                                startedStreamThread = true;
                            }
                            if (startedStreamThread) { break; }
                        }
                    }
                    if (startedStreamThread) { break; }
                }
            }
        }

        public void ApplySettings()
        {
            SetCategoryGainMultiplier(SoundCategoryDefault, GameSettings.CurrentConfig.Audio.SoundVolume, 0);
            SetCategoryGainMultiplier(SoundCategoryUi, GameSettings.CurrentConfig.Audio.UiVolume, 0);
            SetCategoryGainMultiplier(SoundCategoryWaterAmbience, GameSettings.CurrentConfig.Audio.SoundVolume, 0);
            SetCategoryGainMultiplier(SoundCategoryMusic, GameSettings.CurrentConfig.Audio.MusicVolume, 0);
            SetCategoryGainMultiplier(SoundCategoryVoip, Math.Min(GameSettings.CurrentConfig.Audio.VoiceChatVolume, 1.0f), 0);
        }

        /// <summary>
        /// Initializes the thread that handles streaming audio and fading out and disposing channels that are no longer needed.
        /// </summary>
        public void InitUpdateChannelThread()
        {
            if (Disabled) { return; }
            bool isUpdateChannelsThreadDying;
            lock (threadDeathMutex)
            {
                isUpdateChannelsThreadDying = !needsUpdateChannels;
            }
            if (updateChannelsThread == null || updateChannelsThread.ThreadState.HasFlag(ThreadState.Stopped) || isUpdateChannelsThreadDying)
            {
                if (updateChannelsThread != null && !updateChannelsThread.Join(1000))
                {
                    DebugConsole.ThrowError("SoundManager.UpdateChannels thread join timed out!");
                }
                needsUpdateChannels = true;
                updateChannelsThread = new Thread(UpdateChannels)
                {
                    Name = "SoundManager.UpdateChannels Thread",
                    IsBackground = true //this should kill the thread if the game crashes
                };
                updateChannelsThread.Start();
            }
        }

        private bool needsUpdateChannels = false;
        private ManualResetEvent updateChannelsMre = null;

        /// <summary>
        /// Handles streaming audio and fading out and disposing channels that are no longer needed.
        /// </summary>
        private void UpdateChannels()
        {
            updateChannelsMre = new ManualResetEvent(false);
            bool killThread = false;
            while (!killThread)
            {
                killThread = true;
                for (int sourcePoolIndex = 0; sourcePoolIndex < playingChannels.Length; sourcePoolIndex++)
                {
                    lock (playingChannels[sourcePoolIndex])
                    {
                        for (int channelIndex = 0; channelIndex < playingChannels[sourcePoolIndex].Length; channelIndex++)
                        {
                            var channel = playingChannels[sourcePoolIndex][channelIndex];

                            if (channel == null) { continue; }
                            if (channel.FadingOutAndDisposing)
                            {
                                killThread = false;
                                channel.Gain -= 0.1f;
                                if (channel.Gain <= 0.0f)
                                {
                                    channel.Dispose();
                                    playingChannels[sourcePoolIndex][channelIndex] = null;
                                }
                            }
                            else if (channel.IsStream)
                            {
                                if (channel.IsPlaying)
                                {
                                    killThread = false;
                                    channel.UpdateStream();
                                }
                                else
                                {
                                    channel.Dispose();
                                    playingChannels[sourcePoolIndex][channelIndex] = null;
                                }
                            }
                        }
                    }
                }
                updateChannelsMre.WaitOne(10);
                updateChannelsMre.Reset();
                lock (threadDeathMutex)
                {
                    needsUpdateChannels = !killThread;
                }
            }
        }

        public void ForceStreamUpdate()
        {
            updateChannelsMre?.Set();
        }

        private void ReloadSounds()
        {
            for (int i = loadedSounds.Count - 1; i >= 0; i--)
            {
                loadedSounds[i].InitializeAlBuffers();
            }
        }

        private void ReleaseResources(bool keepSounds)
        {
            for (int i = 0; i < playingChannels.Length; i++)
            {
                lock (playingChannels[i])
                {
                    for (int j = 0; j < playingChannels[i].Length; j++)
                    {
                        playingChannels[i][j]?.Dispose();
                    }
                }
            }

            updateChannelsThread?.Join();
            for (int i = loadedSounds.Count - 1; i >= 0; i--)
            {
                if (keepSounds)
                {
                    loadedSounds[i].DeleteAlBuffers();
                }
                else
                {
                    loadedSounds[i].Dispose();
                }
            }
            sourcePools[(int)SourcePoolIndex.Default]?.Dispose();
            sourcePools[(int)SourcePoolIndex.Voice]?.Dispose();

            SoundBuffers.ClearPool();
        }

        public void Dispose()
        {
            if (Disabled) { return; }

            ReleaseResources(false);

            if (!Alc.MakeContextCurrent(IntPtr.Zero) && !GameMain.IsExiting)
            {
                throw new Exception("Failed to detach the current ALC context! (error code: " + Alc.GetError(alcDevice).ToString() + ")");
            }

            Alc.DestroyContext(alcContext);
            
            if (!Alc.CloseDevice(alcDevice) && !GameMain.IsExiting)
            {
                throw new Exception("Failed to close ALC device!");
            }
        }
        
        public static void TryRefreshDevice()
        {
            DebugConsole.NewMessage("Refreshing audio playback device");
            
            List<string> deviceList = Alc.GetStringList(IntPtr.Zero, Alc.OutputDevicesSpecifier).ToList();
            int alcError = Alc.GetError(IntPtr.Zero);
            if (alcError != Alc.NoError)
            {
                DebugConsole.ThrowError("Failed to list available audio playback devices: " + alcError.ToString());
                return;
            }
            
            if (deviceList.Any())
            {
                string device;
                
                if (deviceList.Find(n => n.Equals(GameSettings.CurrentConfig.Audio.AudioOutputDevice, StringComparison.OrdinalIgnoreCase))
                    is string availablePreviousDevice)
                {
                    DebugConsole.NewMessage($" Previous device choice available: {availablePreviousDevice}");
                    device = availablePreviousDevice;
                }
                else
                {
                    device = Alc.GetString(IntPtr.Zero, Alc.DefaultDeviceSpecifier);
                    DebugConsole.NewMessage($" Reverting to default device: {device}");
                }
                
                if (string.IsNullOrEmpty(device))
                {
                    device = deviceList[0];
                    DebugConsole.NewMessage($" No default device found, resorting to first available device: {device}");
                }
                
                // Save the new device choice and initialize it
                var currentConfig = GameSettings.CurrentConfig;
                currentConfig.Audio.AudioOutputDevice = device;
                GameSettings.SetCurrentConfig(currentConfig);
                GameMain.SoundManager.InitializeAlcDevice(device);
            }
            
            if (GUI.SettingsMenuOpen)
            {
                SettingsMenu.Instance?.CreateAudioAndVCTab(true);
            }
        }
    }
}
