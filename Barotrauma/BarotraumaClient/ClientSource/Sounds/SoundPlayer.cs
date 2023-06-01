﻿using Barotrauma.Extensions;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    static class SoundPlayer
    {
        //music
        private const float MusicLerpSpeed = 1.0f;
        private const float UpdateMusicInterval = 5.0f;

        const int MaxMusicChannels = 6;

        private readonly static BackgroundMusic[] currentMusic = new BackgroundMusic[MaxMusicChannels];
        private readonly static SoundChannel[] musicChannel = new SoundChannel[MaxMusicChannels];
        private readonly static BackgroundMusic[] targetMusic = new BackgroundMusic[MaxMusicChannels];
        private static IEnumerable<BackgroundMusic> musicClips => BackgroundMusic.BackgroundMusicPrefabs;

        private static BackgroundMusic previousDefaultMusic;

        private static float updateMusicTimer;

        //ambience
        private static Sound waterAmbienceIn => SoundPrefab.WaterAmbienceIn.ActivePrefab.Sound;
        private static Sound waterAmbienceOut => SoundPrefab.WaterAmbienceOut.ActivePrefab.Sound;
        private static Sound waterAmbienceMoving => SoundPrefab.WaterAmbienceMoving.ActivePrefab.Sound;
        private static readonly HashSet<SoundChannel> waterAmbienceChannels = new HashSet<SoundChannel>();

        private static float ambientSoundTimer;
        private static Vector2 ambientSoundInterval = new Vector2(20.0f, 40.0f); //x = min, y = max

        private static SoundChannel hullSoundChannel;
        private static Hull hullSoundSource;
        private static float hullSoundTimer;
        private static Vector2 hullSoundInterval = new Vector2(45.0f, 90.0f); //x = min, y = max

        //misc
        private static float[] targetFlowLeft, targetFlowRight;
        public static IReadOnlyList<SoundPrefab> FlowSounds => SoundPrefab.FlowSounds;
        public static IReadOnlyList<SoundPrefab> SplashSounds => SoundPrefab.SplashSounds;
        private static SoundChannel[] flowSoundChannels;
        private static float[] flowVolumeLeft;
        private static float[] flowVolumeRight;

        const float FlowSoundRange = 1500.0f;
        const float MaxFlowStrength = 400.0f; //the heaviest water sound effect is played when the water flow is this strong

        private static SoundChannel[] fireSoundChannels;
        private static float[] fireVolumeLeft;
        private static float[] fireVolumeRight;

        const float FireSoundRange = 1000.0f;
        const float FireSoundMediumLimit = 100.0f;
        const float FireSoundLargeLimit = 200.0f; //switch to large fire sound when the size of a firesource is above this
        const int fireSizes = 3;
        private static string[] fireSoundTags = new string[fireSizes] { "fire", "firemedium", "firelarge" };

        // TODO: could use a dictionary to split up the list into smaller lists of same type?
        private static IEnumerable<DamageSound> damageSounds => DamageSound.DamageSoundPrefabs;

        private static bool firstTimeInMainMenu = true;

        private static Sound startUpSound => SoundPrefab.StartupSound.ActivePrefab.Sound;

        public static Identifier OverrideMusicType
        {
            get;
            set;
        }

        public static float? OverrideMusicDuration;

        public static void Update(float deltaTime)
        {
            UpdateMusic(deltaTime);
            if (flowSoundChannels == null || flowSoundChannels.Length != FlowSounds.Count)
            {
                flowSoundChannels = new SoundChannel[FlowSounds.Count];
                flowVolumeLeft = new float[FlowSounds.Count];
                flowVolumeRight = new float[FlowSounds.Count];
                targetFlowLeft = new float[FlowSounds.Count];
                targetFlowRight = new float[FlowSounds.Count];
            }
            if (fireSoundChannels == null || fireSoundChannels.Length != fireSizes)
            {
                fireSoundChannels = new SoundChannel[fireSizes];
                fireVolumeLeft = new float[fireSizes];
                fireVolumeRight = new float[fireSizes];
            }
            
            //stop water sounds if no sub is loaded
            if (Submarine.MainSub == null || Screen.Selected != GameMain.GameScreen)
            {
                foreach (var chn in waterAmbienceChannels.Concat(flowSoundChannels).Concat(fireSoundChannels))
                {
                    chn?.FadeOutAndDispose();
                }
                fireVolumeLeft[0] = 0.0f; fireVolumeLeft[1] = 0.0f;
                fireVolumeRight[0] = 0.0f; fireVolumeRight[1] = 0.0f;
                hullSoundChannel?.FadeOutAndDispose();
                hullSoundSource = null;
                return;
            }

            float ambienceVolume = 0.8f;
            if (Character.Controlled != null && !Character.Controlled.Removed)
            {
                AnimController animController = Character.Controlled.AnimController;
                if (animController.HeadInWater)
                {
                    ambienceVolume = 1.0f;
                    float limbSpeed = animController.Limbs[0].LinearVelocity.Length();
                    if (MathUtils.IsValid(limbSpeed))
                    {
                        ambienceVolume += limbSpeed;
                    }
                }
            }

            UpdateWaterAmbience(ambienceVolume, deltaTime);
            UpdateWaterFlowSounds(deltaTime);
            UpdateRandomAmbience(deltaTime);
            UpdateHullSounds(deltaTime);
            UpdateFireSounds(deltaTime);
        }

        private static void UpdateWaterAmbience(float ambienceVolume, float deltaTime)
        {
            if (GameMain.SoundManager.Disabled || GameMain.GameScreen?.Cam == null) { return; }

            //how fast the sub is moving, scaled to 0.0 -> 1.0
            float movementSoundVolume = 0.0f;

            float insideSubFactor = 0.0f;
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == null || sub.Removed) { continue; }
                float movementFactor = (sub.Velocity == Vector2.Zero) ? 0.0f : sub.Velocity.Length() / 10.0f;
                movementFactor = MathHelper.Clamp(movementFactor, 0.0f, 1.0f);

                if (Character.Controlled == null || Character.Controlled.Submarine != sub)
                {
                    float dist = Vector2.Distance(GameMain.GameScreen.Cam.WorldViewCenter, sub.WorldPosition);
                    movementFactor /= Math.Max(dist / 1000.0f, 1.0f);
                    insideSubFactor = Math.Max(1.0f / Math.Max(dist / 1000.0f, 1.0f), insideSubFactor);
                }
                else
                {
                    insideSubFactor = 1.0f;
                }

                movementSoundVolume = Math.Max(movementSoundVolume, movementFactor);
                if (!MathUtils.IsValid(movementSoundVolume))
                {
                    string errorMsg = "Failed to update water ambience volume - submarine's movement value invalid (" + movementSoundVolume + ", sub velocity: " + sub.Velocity + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    movementSoundVolume = 0.0f;
                }
                if (!MathUtils.IsValid(insideSubFactor))
                {
                    string errorMsg = "Failed to update water ambience volume - inside sub value invalid (" + insideSubFactor + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    insideSubFactor = 0.0f;
                }
            }

            void updateWaterAmbience(Sound sound, float volume)
            {
                SoundChannel chn = waterAmbienceChannels.FirstOrDefault(c => c.Sound == sound);
                if (Level.Loaded != null)
                {
                    volume *= Level.Loaded.GenerationParams.WaterAmbienceVolume;
                }
                if (chn is null || !chn.IsPlaying)
                {
                    if (volume < 0.01f) { return; }
                    if (!(chn is null)) { waterAmbienceChannels.Remove(chn); }
                    chn = sound.Play(volume, "waterambience");
                    chn.Looping = true;
                    waterAmbienceChannels.Add(chn);
                }
                else
                {
                    chn.Gain += deltaTime * Math.Sign(volume - chn.Gain);
                    if (chn.Gain < 0.01f)
                    {
                        chn.FadeOutAndDispose();
                    }
                }
            }

            updateWaterAmbience(waterAmbienceIn, ambienceVolume * (1.0f - movementSoundVolume) * insideSubFactor);
            updateWaterAmbience(waterAmbienceMoving, ambienceVolume * movementSoundVolume * insideSubFactor);
            updateWaterAmbience(waterAmbienceOut, 1.0f - insideSubFactor);
        }

        private static void UpdateWaterFlowSounds(float deltaTime)
        {
            if (FlowSounds.Count == 0) { return; }
            
            for (int i = 0; i < targetFlowLeft.Length; i++)
            {
                targetFlowLeft[i] = 0.0f;
                targetFlowRight[i] = 0.0f;
            }

            Vector2 listenerPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            foreach (Gap gap in Gap.GapList)
            {
                Vector2 diff = gap.WorldPosition - listenerPos;
                if (Math.Abs(diff.X) < FlowSoundRange && Math.Abs(diff.Y) < FlowSoundRange)
                {
                    if (gap.Open < 0.01f || gap.LerpedFlowForce.LengthSquared() < 100.0f) { continue; }
                    float gapFlow = Math.Abs(gap.LerpedFlowForce.X) + Math.Abs(gap.LerpedFlowForce.Y) * 2.5f;
                    if (!gap.IsRoomToRoom) { gapFlow *= 2.0f; }
                    if (gapFlow < 10.0f) { continue; }

                    if (gap.linkedTo.Count == 2 && gap.linkedTo[0] is Hull hull1 && gap.linkedTo[1] is Hull hull2)
                    {
                        //no flow sounds between linked hulls (= rooms consisting of multiple hulls)
                        if (hull1.linkedTo.Contains(hull2)) { continue; }
                        if (hull1.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { continue; }
                        if (hull2.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { continue; }
                    }

                    int flowSoundIndex = (int)Math.Floor(MathHelper.Clamp(gapFlow / MaxFlowStrength, 0, FlowSounds.Count));
                    flowSoundIndex = Math.Min(flowSoundIndex, FlowSounds.Count - 1);

                    float dist = diff.Length();
                    float distFallOff = dist / FlowSoundRange;
                    if (distFallOff >= 0.99f) { continue; }

                    float gain = MathHelper.Clamp(gapFlow / 100.0f, 0.0f, 1.0f);

                    //flow at the left side
                    if (diff.X < 0)
                    {
                        targetFlowLeft[flowSoundIndex] += gain - distFallOff;
                    }
                    else
                    {
                        targetFlowRight[flowSoundIndex] += gain - distFallOff;
                    }
                }
            }

            if (Character.Controlled?.CharacterHealth?.GetAffliction("psychosis") is AfflictionPsychosis psychosis)
            {
                if (psychosis.CurrentFloodType == AfflictionPsychosis.FloodType.Minor)
                {
                    targetFlowLeft[0] = Math.Max(targetFlowLeft[0], 1.0f);
                    targetFlowRight[0] = Math.Max(targetFlowRight[0], 1.0f);
                }
                else if (psychosis.CurrentFloodType == AfflictionPsychosis.FloodType.Major)
                {
                    targetFlowLeft[FlowSounds.Count - 1] = Math.Max(targetFlowLeft[FlowSounds.Count - 1], 1.0f);
                    targetFlowRight[FlowSounds.Count - 1] = Math.Max(targetFlowRight[FlowSounds.Count - 1], 1.0f);
                }
            }

            for (int i = 0; i < FlowSounds.Count; i++)
            {
                flowVolumeLeft[i] = (targetFlowLeft[i] < flowVolumeLeft[i]) ?
                    Math.Max(targetFlowLeft[i], flowVolumeLeft[i] - deltaTime) :
                    Math.Min(targetFlowLeft[i], flowVolumeLeft[i] + deltaTime * 10.0f);
                flowVolumeRight[i] = (targetFlowRight[i] < flowVolumeRight[i]) ?
                     Math.Max(targetFlowRight[i], flowVolumeRight[i] - deltaTime) :
                     Math.Min(targetFlowRight[i], flowVolumeRight[i] + deltaTime * 10.0f);

                if (flowVolumeLeft[i] < 0.05f && flowVolumeRight[i] < 0.05f)
                {
                    if (flowSoundChannels[i] != null)
                    {
                        flowSoundChannels[i].Dispose();
                        flowSoundChannels[i] = null;
                    }
                }
                else
                {
                    if (FlowSounds[i]?.Sound == null) { continue; }
                    Vector2 soundPos = new Vector2(GameMain.SoundManager.ListenerPosition.X + (flowVolumeRight[i] - flowVolumeLeft[i]) * 100, GameMain.SoundManager.ListenerPosition.Y);
                    if (flowSoundChannels[i] == null || !flowSoundChannels[i].IsPlaying)
                    {
                        flowSoundChannels[i] = FlowSounds[i].Sound.Play(1.0f, FlowSoundRange, soundPos);
                        flowSoundChannels[i].Looping = true;
                    }
                    flowSoundChannels[i].Gain = Math.Max(flowVolumeRight[i], flowVolumeLeft[i]);
                    flowSoundChannels[i].Position = new Vector3(soundPos, 0.0f);
                }
            }
        }

        private static void UpdateFireSounds(float deltaTime)
        {
            for (int i = 0; i < fireVolumeLeft.Length; i++)
            {
                fireVolumeLeft[i] = 0.0f;
                fireVolumeRight[i] = 0.0f;
            }

            Vector2 listenerPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            foreach (Hull hull in Hull.HullList)
            {
                foreach (FireSource fs in hull.FireSources)
                {
                    AddFireVolume(fs);
                }
                foreach (FireSource fs in hull.FakeFireSources)
                {
                    AddFireVolume(fs);
                }
            }

            for (int i = 0; i < fireVolumeLeft.Length; i++)
            {
                if (fireVolumeLeft[i] < 0.05f && fireVolumeRight[i] < 0.05f)
                {
                    if (fireSoundChannels[i] != null)
                    {
                        fireSoundChannels[i].FadeOutAndDispose();
                        fireSoundChannels[i] = null;
                    }
                }
                else
                {
                    Vector2 soundPos = new Vector2(GameMain.SoundManager.ListenerPosition.X + (fireVolumeRight[i] - fireVolumeLeft[i]) * 100, GameMain.SoundManager.ListenerPosition.Y);
                    if (fireSoundChannels[i] == null || !fireSoundChannels[i].IsPlaying)
                    {
                        fireSoundChannels[i] = GetSound(fireSoundTags[i])?.Play(1.0f, FlowSoundRange, soundPos);
                        if (fireSoundChannels[i] == null) { continue; }
                        fireSoundChannels[i].Looping = true;
                    }
                    fireSoundChannels[i].Gain = Math.Max(fireVolumeRight[i], fireVolumeLeft[i]);
                    fireSoundChannels[i].Position = new Vector3(soundPos, 0.0f);
                }
            }

            void AddFireVolume(FireSource fs)
            {
                Vector2 diff = fs.WorldPosition + fs.Size / 2 - listenerPos;
                if (Math.Abs(diff.X) < FireSoundRange && Math.Abs(diff.Y) < FireSoundRange)
                {
                    Vector2 diffLeft = (fs.WorldPosition + new Vector2(fs.Size.X, fs.Size.Y / 2)) - listenerPos;
                    if (Math.Abs(diff.X) < fs.Size.X / 2.0f) { diffLeft.X = 0.0f; }
                    if (diffLeft.X <= 0)
                    {
                        float distFallOffLeft = diffLeft.Length() / FireSoundRange;
                        if (distFallOffLeft < 0.99f)
                        {
                            fireVolumeLeft[0] += (1.0f - distFallOffLeft);
                            if (fs.Size.X > FireSoundLargeLimit)
                            {
                                fireVolumeLeft[2] += (1.0f - distFallOffLeft) * ((fs.Size.X - FireSoundLargeLimit) / FireSoundLargeLimit);
                            }
                            else if (fs.Size.X > FireSoundMediumLimit)
                            {
                                fireVolumeLeft[1] += (1.0f - distFallOffLeft) * ((fs.Size.X - FireSoundMediumLimit) / FireSoundMediumLimit);
                            }
                        }
                    }

                    Vector2 diffRight = (fs.WorldPosition + new Vector2(0.0f, fs.Size.Y / 2)) - listenerPos;
                    if (Math.Abs(diff.X) < fs.Size.X / 2.0f) { diffRight.X = 0.0f; }
                    if (diffRight.X >= 0)
                    {
                        float distFallOffRight = diffRight.Length() / FireSoundRange;
                        if (distFallOffRight < 0.99f)
                        {
                            fireVolumeRight[0] += 1.0f - distFallOffRight;
                            if (fs.Size.X > FireSoundLargeLimit)
                            {
                                fireVolumeRight[2] += (1.0f - distFallOffRight) * ((fs.Size.X - FireSoundLargeLimit) / FireSoundLargeLimit);
                            }
                            else if (fs.Size.X > FireSoundMediumLimit)
                            {
                                fireVolumeRight[1] += (1.0f - distFallOffRight) * ((fs.Size.X - FireSoundMediumLimit) / FireSoundMediumLimit);
                            }
                        }
                    }
                }
            }
        }

        private static void UpdateRandomAmbience(float deltaTime)
        {
            if (ambientSoundTimer > 0.0f)
            {
                ambientSoundTimer -= deltaTime;
            }
            else
            {
                PlaySound(
                    "ambient",
                    new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y) + Rand.Vector(100.0f),
                    Rand.Range(0.5f, 1.0f), 
                    1000.0f);

                ambientSoundTimer = Rand.Range(ambientSoundInterval.X, ambientSoundInterval.Y);
            }
        }

        private static void UpdateHullSounds(float deltaTime)
        {
            if (hullSoundChannel != null && hullSoundChannel.IsPlaying && hullSoundSource != null)
            {
                hullSoundChannel.Position = new Vector3(hullSoundSource.WorldPosition, 0.0f);
                hullSoundChannel.Gain = GetHullSoundVolume(hullSoundSource.Submarine);
            }

            if (hullSoundTimer > 0.0f)
            {
                hullSoundTimer -= deltaTime;
            }
            else
            {
                if (!Level.IsLoadedFriendlyOutpost && Character.Controlled?.CurrentHull?.Submarine is Submarine sub &&
                    sub.Info != null && !sub.Info.IsOutpost)
                {
                    hullSoundSource = Character.Controlled.CurrentHull;
                    hullSoundChannel = PlaySound("hull", hullSoundSource.WorldPosition, volume: GetHullSoundVolume(sub), range: 1500.0f);
                    hullSoundTimer = Rand.Range(hullSoundInterval.X, hullSoundInterval.Y);
                }
                else
                {
                    hullSoundTimer = 5.0f;
                }
            }

            static float GetHullSoundVolume(Submarine sub)
            {
                var depth = Level.Loaded == null ? 0.0f : Math.Abs(sub.Position.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                return Math.Clamp((depth - 800.0f) / 1500.0f, 0.4f, 1.0f);
            }
        }

        public static Sound GetSound(string soundTag)
        {
            var matchingSounds = SoundPrefab.Prefabs.Where(p => p.ElementName == soundTag);
            if (!matchingSounds.Any()) return null;

            return matchingSounds.GetRandomUnsynced().Sound;
        }

        /// <summary>
        /// Play a sound defined in a sound xml file without any positional effects.
        /// </summary>
        public static SoundChannel PlaySound(string soundTag, float volume = 1.0f)
        {
            var sound = GetSound(soundTag);            
            return sound?.Play(volume);
        }

        /// <summary>
        /// Play a sound defined in a sound xml file. If the volume or range parameters are omitted, the volume and range defined in the sound xml are used.
        /// </summary>
        public static SoundChannel PlaySound(string soundTag, Vector2 position, float? volume = null, float? range = null, Hull hullGuess = null)
        {
            var sound = GetSound(soundTag);
            if (sound == null) { return null; }
            return PlaySound(sound, position, volume ?? sound.BaseGain, range ?? sound.BaseFar, 1.0f, hullGuess);
        }

        public static SoundChannel PlaySound(Sound sound, Vector2 position, float? volume = null, float? range = null, float? freqMult = null, Hull hullGuess = null, bool ignoreMuffling = false)
        {
            if (sound == null)
            {
                string errorMsg = "Error in SoundPlayer.PlaySound (sound was null)\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.PlaySound:SoundNull" + Environment.StackTrace.CleanupStackTrace(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return null;
            }

            float far = range ?? sound.BaseFar;

            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > far * far)
            {
                return null;
            }
            bool muffle = !ignoreMuffling && ShouldMuffleSound(Character.Controlled, position, far, hullGuess);
            return sound.Play(volume ?? sound.BaseGain, far, freqMult ?? 1.0f, position, muffle: muffle);            
        }

        public static void DisposeDisabledMusic()
        {
            bool musicDisposed = false;
            for (int i = 0; i < currentMusic.Length; i++)
            {
                var music = currentMusic[i];
                if (music is null) { continue; }

                if (!SoundPrefab.Prefabs.Contains(music))
                {
                    musicChannel[i].Dispose();
                    musicDisposed = true;
                    currentMusic[i] = null;
                }
            }

            for (int i = 0; i < targetMusic.Length; i++)
            {
                var music = targetMusic[i];
                if (music is null) { continue; }

                if (!SoundPrefab.Prefabs.Contains(music))
                {
                    targetMusic[i] = null;
                }
            }
            
            if (musicDisposed) { Thread.Sleep(60); }
        }

        public static void ForceMusicUpdate()
        {
            updateMusicTimer = 0.0f;
        }
        
        private static void UpdateMusic(float deltaTime)
        {
            if (musicClips == null || (GameMain.SoundManager?.Disabled ?? true)) { return; }

            if (OverrideMusicType != null && OverrideMusicDuration.HasValue)
            {
                OverrideMusicDuration -= deltaTime;
                if (OverrideMusicDuration <= 0.0f)
                {
                    OverrideMusicType = Identifier.Empty;
                    OverrideMusicDuration = null;
                }                
            }

            int noiseLoopIndex = 1;

            updateMusicTimer -= deltaTime;
            if (updateMusicTimer <= 0.0f)
            {
                //find appropriate music for the current situation
                Identifier currentMusicType = GetCurrentMusicType();
                float currentIntensity = GameMain.GameSession?.EventManager != null ?
                    GameMain.GameSession.EventManager.MusicIntensity * 100.0f : 0.0f;

                IEnumerable<BackgroundMusic> suitableMusic = GetSuitableMusicClips(currentMusicType, currentIntensity);
                int mainTrackIndex = 0;
                if (suitableMusic.None())
                {
                    targetMusic[mainTrackIndex] = null;
                }
                //switch the music if nothing playing atm or the currently playing clip is not suitable anymore
                else if (targetMusic[mainTrackIndex] == null || currentMusic[mainTrackIndex] == null || !currentMusic[mainTrackIndex].IsPlaying() || !suitableMusic.Any(m => m == currentMusic[mainTrackIndex]))
                {
                    if (currentMusicType == "default")
                    {
                        if (previousDefaultMusic == null)
                        {
                            targetMusic[mainTrackIndex] = previousDefaultMusic = suitableMusic.GetRandomUnsynced();
                        }
                        else
                        {
                            targetMusic[mainTrackIndex] = previousDefaultMusic;
                        }
                    }
                    else
                    {
                        targetMusic[mainTrackIndex] = suitableMusic.GetRandomUnsynced();
                    }
                }

                if (Level.Loaded != null && (Level.Loaded.Type == LevelData.LevelType.LocationConnection || Level.Loaded.GenerationParams.PlayNoiseLoopInOutpostLevel))
                {
                    Identifier biome = Level.Loaded.LevelData.Biome.Identifier;
                    if (Level.Loaded.IsEndBiome && GameMain.GameSession?.Campaign is CampaignMode campaign)
                    {
                        //don't play end biome music in the path leading up to the end level(s)
                        if (!campaign.Map.EndLocations.Contains(Level.Loaded.StartLocation))
                        {
                            biome = Level.Loaded.StartLocation.Biome.Identifier;
                        }
                    }

                    // Find background noise loop for the current biome
                    IEnumerable<BackgroundMusic> suitableNoiseLoops = Screen.Selected == GameMain.GameScreen ?
                        GetSuitableMusicClips(biome, currentIntensity) :
                        Enumerable.Empty<BackgroundMusic>();
                    if (suitableNoiseLoops.Count() == 0)
                    {
                        targetMusic[noiseLoopIndex] = null;
                    }
                    // Switch the noise loop if nothing playing atm or the currently playing clip is not suitable anymore
                    else if (targetMusic[noiseLoopIndex] == null || currentMusic[noiseLoopIndex] == null || !suitableNoiseLoops.Any(m => m == currentMusic[noiseLoopIndex]))
                    {
                        targetMusic[noiseLoopIndex] = suitableNoiseLoops.GetRandomUnsynced();
                    }
                }
                else
                {
                    targetMusic[noiseLoopIndex] = null;
                }

                IEnumerable<BackgroundMusic> suitableTypeAmbiences = GetSuitableMusicClips($"{currentMusicType}ambience".ToIdentifier(), currentIntensity);
                int typeAmbienceTrackIndex = 2;
                if (suitableTypeAmbiences.None())
                {
                    targetMusic[typeAmbienceTrackIndex] = null;
                }
                // Switch the type ambience if nothing playing atm or the currently playing clip is not suitable anymore
                else if (targetMusic[typeAmbienceTrackIndex] == null || currentMusic[typeAmbienceTrackIndex] == null || !currentMusic[typeAmbienceTrackIndex].IsPlaying() || suitableTypeAmbiences.None(m => m == currentMusic[typeAmbienceTrackIndex]))
                {
                    targetMusic[typeAmbienceTrackIndex] = suitableTypeAmbiences.GetRandomUnsynced();
                }

                IEnumerable<BackgroundMusic> suitableIntensityMusic = Enumerable.Empty<BackgroundMusic>();
                if (targetMusic[mainTrackIndex] is { MuteIntensityTracks: false } mainTrack && Screen.Selected == GameMain.GameScreen)
                {
                    float intensity = currentIntensity;
                    if (mainTrack?.ForceIntensityTrack != null)
                    {
                        intensity = mainTrack.ForceIntensityTrack.Value;
                    }
                    suitableIntensityMusic = GetSuitableMusicClips("intensity".ToIdentifier(), intensity);
                }
                //get the appropriate intensity layers for current situation
                int intensityTrackStartIndex = 3;
                for (int i = intensityTrackStartIndex; i < MaxMusicChannels; i++)
                {
                    //disable targetmusics that aren't suitable anymore
                    if (targetMusic[i] != null && !suitableIntensityMusic.Any(m => m == targetMusic[i]))
                    {
                        targetMusic[i] = null;
                    }
                }

                foreach (BackgroundMusic intensityMusic in suitableIntensityMusic)
                {
                    //already playing, do nothing
                    if (targetMusic.Any(m => m != null && m == intensityMusic)) { continue; }

                    for (int i = intensityTrackStartIndex; i < MaxMusicChannels; i++)
                    {
                        if (targetMusic[i] == null)
                        {
                            targetMusic[i] = intensityMusic;
                            break;
                        }
                    }
                }

                updateMusicTimer = UpdateMusicInterval;
            }

            int activeTrackCount = targetMusic.Count(m => m != null);
            for (int i = 0; i < MaxMusicChannels; i++)
            {
                //nothing should be playing on this channel
                if (targetMusic[i] == null)
                {
                    if (musicChannel[i] != null && musicChannel[i].IsPlaying)
                    {
                        //mute the channel
                        musicChannel[i].Gain = MathHelper.Lerp(musicChannel[i].Gain, 0.0f, MusicLerpSpeed * deltaTime);
                        if (musicChannel[i].Gain < 0.01f) { DisposeMusicChannel(i); }                     
                    }
                }
                //something should be playing, but the targetMusic is invalid
                else if (!musicClips.Any(mc => mc == targetMusic[i]))
                {
                    targetMusic[i] = GetSuitableMusicClips(targetMusic[i].Type, 0.0f).GetRandomUnsynced();
                }
                //something should be playing, but the channel is playing nothing or an incorrect clip
                else if (currentMusic[i] == null || targetMusic[i] != currentMusic[i])
                {
                    //something playing -> mute it first
                    if (musicChannel[i] != null && musicChannel[i].IsPlaying)
                    {
                        musicChannel[i].Gain = MathHelper.Lerp(musicChannel[i].Gain, 0.0f, MusicLerpSpeed * deltaTime);
                        if (musicChannel[i].Gain < 0.01f) { DisposeMusicChannel(i); }                   
                    }
                    //channel free now, start playing the correct clip
                    if (currentMusic[i] == null || (musicChannel[i] == null || !musicChannel[i].IsPlaying))
                    {
                        DisposeMusicChannel(i);

                        currentMusic[i] = targetMusic[i];
                        musicChannel[i] = currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? "default" : "music");
                        if (targetMusic[i].ContinueFromPreviousTime)
                        {
                            musicChannel[i].StreamSeekPos = targetMusic[i].PreviousTime;
                        }
                        musicChannel[i].Looping = true;
                    }
                }
                else
                {
                    //playing something, lerp volume up
                    if (musicChannel[i] == null || !musicChannel[i].IsPlaying)
                    {
                        musicChannel[i]?.Dispose();
                        musicChannel[i] = currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? "default" : "music");
                        musicChannel[i].Looping = true;
                    }
                    float targetGain = targetMusic[i].Volume;
                    if (targetMusic[i].DuckVolume)
                    {
                        targetGain *= (float)Math.Sqrt(1.0f / activeTrackCount);
                    }
                    musicChannel[i].Gain = MathHelper.Lerp(musicChannel[i].Gain, targetGain, MusicLerpSpeed * deltaTime);
                }
            } 
        }

        private static void DisposeMusicChannel(int index)
        {
            var clip = musicClips.FirstOrDefault(m => m.Sound == musicChannel[index]?.Sound);
            if (clip != null)
            {
                if (clip.ContinueFromPreviousTime) { clip.PreviousTime = musicChannel[index].StreamSeekPos; }
            }

            musicChannel[index]?.Dispose(); musicChannel[index] = null;
            currentMusic[index] = null;
        }
        
        private static IEnumerable<BackgroundMusic> GetSuitableMusicClips(Identifier musicType, float currentIntensity)
        {
            return musicClips.Where(music => 
                music != null && 
                music.Type == musicType && 
                currentIntensity >= music.IntensityRange.X &&
                currentIntensity <= music.IntensityRange.Y);
        }

        private static Identifier GetCurrentMusicType()
        {
            if (OverrideMusicType != null) { return OverrideMusicType; }

            if (Screen.Selected == null) { return "menu".ToIdentifier(); }

            if (Screen.Selected is { IsEditor: true } || GameMain.GameSession?.GameMode is TestGameMode || Screen.Selected == GameMain.NetLobbyScreen)
            {
                return "editor".ToIdentifier();
            }

            if (Screen.Selected != GameMain.GameScreen) 
            {
                previousDefaultMusic = null;
                return (firstTimeInMainMenu ? "menu" : "default").ToIdentifier(); 
            }

            firstTimeInMainMenu = false;

            if (GameMain.GameSession != null)
            {
                foreach (var mission in GameMain.GameSession.Missions)
                {
                    var missionMusic = mission.GetOverrideMusicType();
                    if (!missionMusic.IsEmpty) { return missionMusic; }
                }
            }

            if (Character.Controlled != null)
            {
                if (Level.Loaded != null && Level.Loaded.Ruins != null &&
                    Level.Loaded.Ruins.Any(r => r.Area.Contains(Character.Controlled.WorldPosition)))
                {
                    return "ruins".ToIdentifier();
                }

                if (Character.Controlled.Submarine?.Info?.IsWreck ?? false)
                {
                    return "wreck".ToIdentifier();
                }

                if (Level.IsLoadedOutpost)
                {
                    // Only return music type for location types which have music tracks defined
                    var locationType = Level.Loaded.StartLocation?.Type?.Identifier;
                    if (locationType.HasValue && locationType != Identifier.Empty && musicClips.Any(c => c.Type == locationType))
                    {
                        return locationType.Value;
                    }
                }
            }

            if (Level.Loaded is { IsEndBiome: true })
            {
                return "endlevel".ToIdentifier();
            }

            Submarine targetSubmarine = Character.Controlled?.Submarine;
            if (targetSubmarine != null && targetSubmarine.AtDamageDepth)
            {
                return "deep".ToIdentifier();
            }
            if (GameMain.GameScreen != null && Screen.Selected == GameMain.GameScreen && Submarine.MainSub != null &&
                Level.Loaded != null && Level.Loaded.GetRealWorldDepth(GameMain.GameScreen.Cam.Position.Y) > Submarine.MainSub.RealWorldCrushDepth)
            {
                return "deep".ToIdentifier();
            }

            if (targetSubmarine != null)
            {
                float floodedArea = 0.0f;
                float totalArea = 0.0f;
                foreach (Hull hull in Hull.HullList)
                {
                    if (hull.Submarine != targetSubmarine) { continue; }
                    floodedArea += hull.WaterVolume;
                    totalArea += hull.Volume;
                }

                if (totalArea > 0.0f && floodedArea / totalArea > 0.25f) { return "flooded".ToIdentifier(); }        
            }
            
            float enemyDistThreshold = 5000.0f;

            if (targetSubmarine != null)
            {
                enemyDistThreshold = Math.Max(enemyDistThreshold, Math.Max(targetSubmarine.Borders.Width, targetSubmarine.Borders.Height) * 2.0f);
            }

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead || !character.Enabled) continue;
                if (!(character.AIController is EnemyAIController enemyAI) || !enemyAI.Enabled || (!enemyAI.AttackHumans && !enemyAI.AttackRooms)) { continue; }

                if (targetSubmarine != null)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, targetSubmarine.WorldPosition) < enemyDistThreshold * enemyDistThreshold)
                    {
                        return "monster".ToIdentifier();
                    }
                }
                else if (Character.Controlled != null)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, Character.Controlled.WorldPosition) < enemyDistThreshold * enemyDistThreshold)
                    {
                        return "monster".ToIdentifier();
                    }
                }
            }

            if (GameMain.GameSession != null)
            {
                if (Submarine.Loaded != null && Level.Loaded != null && Submarine.MainSub != null && Submarine.MainSub.AtEndExit)
                {
                    return "levelend".ToIdentifier();
                }
                if (GameMain.GameSession.RoundDuration < 120.0 && 
                    Level.Loaded?.Type == LevelData.LevelType.LocationConnection)
                {
                    return "start".ToIdentifier();
                }
            }
            
            return "default".ToIdentifier();
        }

        public static bool ShouldMuffleSound(Character listener, Vector2 soundWorldPos, float range, Hull hullGuess)
        {
            if (listener == null) return false;

            float lowpassHFGain = 1.0f;
            AnimController animController = listener.AnimController;
            if (animController.HeadInWater)
            {
                lowpassHFGain = 0.2f;
            }
            lowpassHFGain *= Character.Controlled.LowPassMultiplier;
            if (lowpassHFGain < 0.5f) return true;
            
            Hull targetHull = Hull.FindHull(soundWorldPos, hullGuess, true);
            if (listener.CurrentHull == null || targetHull == null)
            {
                return listener.CurrentHull != targetHull;
            }
            Vector2 soundPos = soundWorldPos;
            if (targetHull.Submarine != null)
            {
                soundPos += -targetHull.Submarine.WorldPosition + targetHull.Submarine.HiddenSubPosition;
            }
            return listener.CurrentHull.GetApproximateDistance(listener.Position, soundPos, targetHull, range) > range;
        }

        public static void PlaySplashSound(Vector2 worldPosition, float strength)
        {
            if (SplashSounds.Count == 0) { return; }
            int splashIndex = MathHelper.Clamp((int)(strength + Rand.Range(-2.0f, 2.0f)), 0, SplashSounds.Count - 1);
            float range = 800.0f;
            SplashSounds[splashIndex].Sound?.Play(1.0f, range, worldPosition, muffle: ShouldMuffleSound(Character.Controlled, worldPosition, range, null));
        }

        public static void PlayDamageSound(string damageType, float damage, PhysicsBody body)
        {
            Vector2 bodyPosition = body.DrawPosition;
            PlayDamageSound(damageType, damage, bodyPosition, 800.0f);
        }

        public static void PlayDamageSound(string damageType, float damage, Vector2 position, float range = 2000.0f, IEnumerable<Identifier> tags = null)
        {
            var suitableSounds = damageSounds.Where(s =>
                s.DamageType == damageType &&
                (s.RequiredTag.IsEmpty || (tags == null ? s.RequiredTag.IsEmpty : tags.Contains(s.RequiredTag))));

            //if the damage is too low for any sound, don't play anything
            if (suitableSounds.All(d => damage < d.DamageRange.X)) { return; }

            //allow the damage to differ by 10 from the configured damage range,
            //so the same amount of damage doesn't always play the same sound
            float randomizedDamage = MathHelper.Clamp(damage + Rand.Range(-10.0f, 10.0f), 0.0f, 100.0f);
            suitableSounds = suitableSounds.Where(s => 
                s.DamageRange == Vector2.Zero || (randomizedDamage >= s.DamageRange.X && randomizedDamage <= s.DamageRange.Y));

            var damageSound = suitableSounds.GetRandomUnsynced();
            damageSound?.Sound?.Play(1.0f, range, position, muffle: !damageSound.IgnoreMuffling && ShouldMuffleSound(Character.Controlled, position, range, null));
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            GUISound.GUISoundPrefabs
                .Where(s => s.Type == soundType)
                .GetRandomUnsynced()?.Sound?.Play(null, "ui");
        }

        public static void PlayUISound(GUISoundType? soundType)
        {
            if (soundType.HasValue)
            {
                PlayUISound(soundType.Value);
            }
        }
    }
}
