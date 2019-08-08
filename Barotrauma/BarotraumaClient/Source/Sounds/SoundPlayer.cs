using Barotrauma.Extensions;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public struct DamageSound
    {
        //the range of inflicted damage where the sound can be played
        //(10.0f, 30.0f) would be played when the inflicted damage is between 10 and 30
        public readonly Vector2 damageRange;

        public readonly string damageType;

        public readonly Sound sound;

        public readonly string requiredTag;

        public DamageSound(Sound sound, Vector2 damageRange, string damageType, string requiredTag = "")
        {
            this.sound = sound;
            this.damageRange = damageRange;
            this.damageType = damageType;

            this.requiredTag = requiredTag;
        }
    }

    public class BackgroundMusic
    {
        public readonly string File;
        public readonly string Type;
        public readonly bool DuckVolume;

        public readonly Vector2 IntensityRange;
                
        public BackgroundMusic(XElement element)
        {
            this.File = Path.GetFullPath(element.GetAttributeString("file", ""));
            this.Type = element.GetAttributeString("type", "").ToLowerInvariant();
            this.IntensityRange = element.GetAttributeVector2("intensityrange", new Vector2(0.0f, 100.0f));
            this.DuckVolume = element.GetAttributeBool("duckvolume", false);
        }
    }

    static class SoundPlayer
    {
        private static ILookup<string, Sound> miscSounds;

        //music
        private const float MusicLerpSpeed = 1.0f;
        private const float UpdateMusicInterval = 5.0f;

        const int MaxMusicChannels = 6;

        private readonly static Sound[] currentMusic = new Sound[MaxMusicChannels];
        private readonly static SoundChannel[] musicChannel = new SoundChannel[MaxMusicChannels];
        private readonly static BackgroundMusic[] targetMusic = new BackgroundMusic[MaxMusicChannels];
        private static List<BackgroundMusic> musicClips;

        private static float updateMusicTimer;

        //ambience
        private static List<Sound> waterAmbiences = new List<Sound>();
        private static SoundChannel[] waterAmbienceChannels = new SoundChannel[2];

        private static float ambientSoundTimer;
        private static Vector2 ambientSoundInterval = new Vector2(20.0f, 40.0f); //x = min, y = max

        //misc
        public static List<Sound> FlowSounds = new List<Sound>();
        public static List<Sound> SplashSounds = new List<Sound>();
        private static SoundChannel[] flowSoundChannels;
        private static float[] flowVolumeLeft;
        private static float[] flowVolumeRight;

        const float FlowSoundRange = 1500.0f;
        const float MaxFlowStrength = 400.0f; //the heaviest water sound effect is played when the water flow is this strong

        private static SoundChannel[] fireSoundChannels;
        private static float[] fireVolumeLeft;
        private static float[] fireVolumeRight;

        const float FireSoundRange = 1000.0f;
        const float FireSoundLargeLimit = 200.0f; //switch to large fire sound when the size of a firesource is above this

        // TODO: could use a dictionary to split up the list into smaller lists of same type?
        private static List<DamageSound> damageSounds;

        private static Sound startUpSound;

        public static bool Initialized;

        public static string OverrideMusicType
        {
            get;
            set;
        }

        public static float? OverrideMusicDuration;

        public static int SoundCount;
        
        public static IEnumerable<object> Init()
        {
            OverrideMusicType = null;

            var soundFiles = GameMain.Instance.GetFilesOfType(ContentType.Sounds);

            List<XElement> soundElements = new List<XElement>();
            foreach (string soundFile in soundFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(soundFile);
                if (doc != null && doc.Root != null)
                {
                    soundElements.AddRange(doc.Root.Elements());
                }
            }
            
            SoundCount = 1 + soundElements.Count();

            var startUpSoundElement = soundElements.Find(e => e.Name.ToString().ToLowerInvariant() == "startupsound");
            if (startUpSoundElement != null)
            {
                startUpSound = GameMain.SoundManager.LoadSound(startUpSoundElement, false);
                startUpSound?.Play();
            }

            yield return CoroutineStatus.Running;
                                    
            List<KeyValuePair<string, Sound>> miscSoundList = new List<KeyValuePair<string, Sound>>();
            damageSounds = new List<DamageSound>();
            musicClips = new List<BackgroundMusic>();
            
            foreach (XElement soundElement in soundElements)
            {
                yield return CoroutineStatus.Running;

                try
                {
                    switch (soundElement.Name.ToString().ToLowerInvariant())
                    {
                        case "music":
                            musicClips.AddIfNotNull(new BackgroundMusic(soundElement));
                            break;
                        case "splash":
                            SplashSounds.AddIfNotNull(GameMain.SoundManager.LoadSound(soundElement, false));
                            break;
                        case "flow":
                            FlowSounds.AddIfNotNull(GameMain.SoundManager.LoadSound(soundElement, false));
                            break;
                        case "waterambience":
                            waterAmbiences.AddIfNotNull(GameMain.SoundManager.LoadSound(soundElement, false));
                            break;
                        case "damagesound":
                            Sound damageSound = GameMain.SoundManager.LoadSound(soundElement, false);
                            if (damageSound == null) { continue; }
                    
                            string damageSoundType = soundElement.GetAttributeString("damagesoundtype", "None");
                            damageSounds.Add(new DamageSound(
                                damageSound, 
                                soundElement.GetAttributeVector2("damagerange", Vector2.Zero), 
                                damageSoundType, 
                                soundElement.GetAttributeString("requiredtag", "")));

                            break;
                        default:
                            Sound sound = GameMain.SoundManager.LoadSound(soundElement, false);
                            if (sound != null)
                            {
                                miscSoundList.Add(new KeyValuePair<string, Sound>(soundElement.Name.ToString().ToLowerInvariant(), sound));
                            }
                            break;
                    }
                }
                catch (FileNotFoundException e)
                {
                    DebugConsole.ThrowError("Error while initializing SoundPlayer.", e);
                }                
            }

            flowSoundChannels = new SoundChannel[FlowSounds.Count];
            flowVolumeLeft = new float[FlowSounds.Count];
            flowVolumeRight = new float[FlowSounds.Count];

            fireSoundChannels = new SoundChannel[2];
            fireVolumeLeft = new float[2];
            fireVolumeRight = new float[2];

            miscSounds = miscSoundList.ToLookup(kvp => kvp.Key, kvp => kvp.Value);            

            Initialized = true;

            yield return CoroutineStatus.Success;

        }
        

        public static void Update(float deltaTime)
        {
            if (!Initialized) { return; }

            UpdateMusic(deltaTime);

            if (startUpSound != null && !GameMain.SoundManager.IsPlaying(startUpSound))
            {
                startUpSound.Dispose();
                startUpSound = null;                
            }

            //stop water sounds if no sub is loaded
            if (Submarine.MainSub == null || Screen.Selected != GameMain.GameScreen)  
            {
                for (int i = 0; i < waterAmbienceChannels.Length; i++)
                {
                    if (waterAmbienceChannels[i] == null) continue;
                    waterAmbienceChannels[i].FadeOutAndDispose();
                    waterAmbienceChannels[i] = null;
                }
                for (int i = 0; i < FlowSounds.Count; i++)
                {
                    if (flowSoundChannels[i] == null) continue;
                    flowSoundChannels[i].FadeOutAndDispose();
                    flowSoundChannels[i] = null;
                }
                for (int i = 0; i < fireSoundChannels.Length; i++)
                {
                    if (fireSoundChannels[i] == null) continue;
                    fireSoundChannels[i].FadeOutAndDispose();
                    fireSoundChannels[i] = null;
                }
                fireVolumeLeft[0] = 0.0f; fireVolumeLeft[1] = 0.0f;
                fireVolumeRight[0] = 0.0f; fireVolumeRight[1] = 0.0f;
                return;
            }

            float ambienceVolume = 0.8f;
            if (Character.Controlled != null && !Character.Controlled.Removed)
            {
                AnimController animController = Character.Controlled.AnimController;
                if (animController.HeadInWater)
                {
                    ambienceVolume = 1.0f;
                    ambienceVolume += animController.Limbs[0].LinearVelocity.Length();
                }
            }

            UpdateWaterAmbience(ambienceVolume);
            UpdateWaterFlowSounds(deltaTime);
            UpdateRandomAmbience(deltaTime);
            UpdateFireSounds(deltaTime);            
        }

        private static void UpdateWaterAmbience(float ambienceVolume)
        {
            //how fast the sub is moving, scaled to 0.0 -> 1.0
            float movementSoundVolume = 0.0f;

            foreach (Submarine sub in Submarine.Loaded)
            {
                float movementFactor = (sub.Velocity == Vector2.Zero) ? 0.0f : sub.Velocity.Length() / 10.0f;
                movementFactor = MathHelper.Clamp(movementFactor, 0.0f, 1.0f);

                if (Character.Controlled == null || Character.Controlled.Submarine != sub)
                {
                    float dist = Vector2.Distance(GameMain.GameScreen.Cam.WorldViewCenter, sub.WorldPosition);
                    movementFactor = movementFactor / Math.Max(dist / 1000.0f, 1.0f);
                }

                movementSoundVolume = Math.Max(movementSoundVolume, movementFactor);
                if (!MathUtils.IsValid(movementSoundVolume))
                {
                    string errorMsg = "Failed to update water ambience volume - submarine's movement value invalid (" + movementSoundVolume + ", sub velocity: " + sub.Velocity + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    movementSoundVolume = 0.0f;
                }
            }

            if (waterAmbiences.Count > 1)
            {
                if (waterAmbienceChannels[0] == null || !waterAmbienceChannels[0].IsPlaying)
                {
                    waterAmbienceChannels[0] = waterAmbiences[0].Play(ambienceVolume * (1.0f - movementSoundVolume),"waterambience");
                    //waterAmbiences[0].Loop(waterAmbienceIndexes[0], ambienceVolume * (1.0f - movementSoundVolume));
                    waterAmbienceChannels[0].Looping = true;
                }
                else
                {
                    waterAmbienceChannels[0].Gain = ambienceVolume * (1.0f - movementSoundVolume);
                }

                if (waterAmbienceChannels[1] == null || !waterAmbienceChannels[1].IsPlaying)
                {
                    waterAmbienceChannels[1] = waterAmbiences[1].Play(ambienceVolume * movementSoundVolume, "waterambience");
                    //waterAmbienceIndexes[1] = waterAmbiences[1].Loop(waterAmbienceIndexes[1], ambienceVolume * movementSoundVolume);
                    waterAmbienceChannels[1].Looping = true;
                }
                else
                {
                    waterAmbienceChannels[1].Gain = ambienceVolume * movementSoundVolume;
                }
            }
        }

        private static void UpdateWaterFlowSounds(float deltaTime)
        {
            if (FlowSounds.Count == 0) { return; }

            float[] targetFlowLeft = new float[FlowSounds.Count];
            float[] targetFlowRight = new float[FlowSounds.Count];

            Vector2 listenerPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Open < 0.01f) continue;
                float gapFlow = Math.Abs(gap.LerpedFlowForce.X) + Math.Abs(gap.LerpedFlowForce.Y) * 2.5f;

                if (gapFlow < 10.0f) continue;

                int flowSoundIndex = (int)Math.Floor(MathHelper.Clamp(gapFlow / MaxFlowStrength, 0, FlowSounds.Count));
                flowSoundIndex = Math.Min(flowSoundIndex, FlowSounds.Count - 1);

                Vector2 diff = gap.WorldPosition - listenerPos;
                if (Math.Abs(diff.X) < FlowSoundRange && Math.Abs(diff.Y) < FlowSoundRange)
                {
                    float dist = diff.Length();
                    float distFallOff = dist / FlowSoundRange;
                    if (distFallOff >= 0.99f) continue;

                    //flow at the left side
                    if (diff.X < 0)
                    {
                        targetFlowLeft[flowSoundIndex] = 1.0f - distFallOff;
                    }
                    else
                    {
                        targetFlowRight[flowSoundIndex] = 1.0f - distFallOff;
                    }
                }
            }

            for (int i = 0; i < FlowSounds.Count; i++)
            {
                flowVolumeLeft[i] = (targetFlowLeft[i] < flowVolumeLeft[i]) ?
                    Math.Max(targetFlowLeft[i], flowVolumeLeft[i] - deltaTime) :
                    Math.Min(targetFlowLeft[i], flowVolumeLeft[i] + deltaTime);
                flowVolumeRight[i] = (targetFlowRight[i] < flowVolumeRight[i]) ?
                     Math.Max(targetFlowRight[i], flowVolumeRight[i] - deltaTime) :
                     Math.Min(targetFlowRight[i], flowVolumeRight[i] + deltaTime);

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
                    Vector2 soundPos = new Vector2(GameMain.SoundManager.ListenerPosition.X + (flowVolumeRight[i] - flowVolumeLeft[i]) * 100, GameMain.SoundManager.ListenerPosition.Y);
                    if (flowSoundChannels[i] == null || !flowSoundChannels[i].IsPlaying)
                    {
                        flowSoundChannels[i] = FlowSounds[i].Play(1.0f, FlowSoundRange, soundPos);
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
            foreach (Hull hull in Hull.hullList)
            {
                foreach (FireSource fs in hull.FireSources)
                {
                    Vector2 diff = fs.WorldPosition + fs.Size / 2 - listenerPos;
                    if (Math.Abs(diff.X) < FireSoundRange && Math.Abs(diff.Y) < FireSoundRange)
                    {
                        Vector2 diffLeft = (fs.WorldPosition + new Vector2(fs.Size.X, fs.Size.Y / 2)) - listenerPos;
                        if (diff.X < fs.Size.X / 2.0f) diffLeft.X = 0.0f;
                        if (diffLeft.X <= 0)
                        {
                            float distFallOffLeft = diffLeft.Length() / FireSoundRange;
                            if (distFallOffLeft < 0.99f)
                            {
                                fireVolumeLeft[0] += (1.0f - distFallOffLeft) * (fs.Size.X / FireSoundLargeLimit);
                                if (fs.Size.X > FireSoundLargeLimit) fireVolumeLeft[1] += (1.0f - distFallOffLeft) * ((fs.Size.X - FireSoundLargeLimit) / FireSoundLargeLimit);
                            }
                        }

                        Vector2 diffRight = (fs.WorldPosition + new Vector2(0.0f, fs.Size.Y / 2)) - listenerPos;
                        if (diff.X < fs.Size.X / 2.0f) diffRight.X = 0.0f;
                        if (diffRight.X >= 0)
                        {
                            float distFallOffRight = diffRight.Length() / FireSoundRange;
                            if (distFallOffRight < 0.99f)
                            {
                                fireVolumeRight[0] += 1.0f - distFallOffRight;
                                if (fs.Size.X > FireSoundLargeLimit) fireVolumeRight[1] += (1.0f - distFallOffRight) * ((fs.Size.X - FireSoundLargeLimit) / FireSoundLargeLimit);
                            }
                        }
                    }
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
                        fireSoundChannels[i] = GetSound(i == 0 ? "fire" : "firelarge").Play(1.0f, FlowSoundRange, soundPos);
                        fireSoundChannels[i].Looping = true;
                    }
                    fireSoundChannels[i].Gain = Math.Max(fireVolumeRight[i], fireVolumeLeft[i]);
                    fireSoundChannels[i].Position = new Vector3(soundPos, 0.0f);
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

        public static Sound GetSound(string soundTag)
        {
            var matchingSounds = miscSounds[soundTag].ToList();
            if (matchingSounds.Count == 0) return null;

            return matchingSounds[Rand.Int(matchingSounds.Count)];
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
            if (sound == null) return null;
            return PlaySound(sound, position, volume ?? sound.BaseGain, range ?? sound.BaseFar, hullGuess);
        }

        public static SoundChannel PlaySound(Sound sound, Vector2 position, float? volume = null, float? range = null, Hull hullGuess = null)
        {
            float far = range ?? sound.BaseFar;

            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > far * far) return null;
            return sound.Play(volume ?? sound.BaseGain, far, position, muffle: ShouldMuffleSound(Character.Controlled, position, far, hullGuess));            
        }

        private static void UpdateMusic(float deltaTime)
        {
            if (musicClips == null || GameMain.SoundManager.Disabled) { return; }

            if (OverrideMusicType != null && OverrideMusicDuration.HasValue)
            {
                OverrideMusicDuration -= deltaTime;
                if (OverrideMusicDuration <= 0.0f)
                {
                    OverrideMusicType = null;
                    OverrideMusicDuration = null;
                }                
            }

            updateMusicTimer -= deltaTime;
            if (updateMusicTimer <= 0.0f)
            {
                //find appropriate music for the current situation
                string currentMusicType = GetCurrentMusicType();
                float currentIntensity = GameMain.GameSession?.EventManager != null ?
                    GameMain.GameSession.EventManager.CurrentIntensity * 100.0f : 0.0f;

                IEnumerable<BackgroundMusic> suitableMusic = GetSuitableMusicClips(currentMusicType, currentIntensity);

                if (suitableMusic.Count() == 0)
                {
                    targetMusic[0] = null;
                }
                //switch the music if nothing playing atm or the currently playing clip is not suitable anymore
                else if (targetMusic[0] == null || currentMusic[0] == null || !suitableMusic.Any(m => m.File == currentMusic[0].Filename))
                {
                    targetMusic[0] = suitableMusic.GetRandom();
                }
                                
                //get the appropriate intensity layers for current situation
                IEnumerable<BackgroundMusic> suitableIntensityMusic = Screen.Selected == GameMain.GameScreen ?
                    GetSuitableMusicClips("intensity", currentIntensity) :
                    Enumerable.Empty<BackgroundMusic>();

                for (int i = 1; i < MaxMusicChannels; i++)
                {
                    //disable targetmusics that aren't suitable anymore
                    if (targetMusic[i] != null && !suitableIntensityMusic.Any(m => m.File == targetMusic[i].File))
                    {
                        targetMusic[i] = null;
                    }
                }
                    
                foreach (BackgroundMusic intensityMusic in suitableIntensityMusic)
                {
                    //already playing, do nothing
                    if (targetMusic.Any(m => m != null && m.File == intensityMusic.File)) continue;

                    for (int i = 1; i < MaxMusicChannels; i++)
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
                        if (musicChannel[i].Gain < 0.01f) DisposeMusicChannel(i);                        
                    }
                }
                //something should be playing, but the channel is playing nothing or an incorrect clip
                else if (currentMusic[i] == null || targetMusic[i].File != currentMusic[i].Filename)
                {
                    //something playing -> mute it first
                    if (musicChannel[i] != null && musicChannel[i].IsPlaying)
                    {
                        musicChannel[i].Gain = MathHelper.Lerp(musicChannel[i].Gain, 0.0f, MusicLerpSpeed * deltaTime);
                        if (musicChannel[i].Gain < 0.01f) DisposeMusicChannel(i);                        
                    }
                    //channel free now, start playing the correct clip
                    if (currentMusic[i] == null || (musicChannel[i] == null || !musicChannel[i].IsPlaying))
                    {
                        DisposeMusicChannel(i);
                        currentMusic[i] = GameMain.SoundManager.LoadSound(targetMusic[i].File, true);
                        musicChannel[i] = currentMusic[i].Play(0.0f, "music");
                        musicChannel[i].Looping = true;
                    }
                }
                else
                {
                    //playing something, lerp volume up
                    if (musicChannel[i] == null || !musicChannel[i].IsPlaying)
                    {
                        musicChannel[i]?.Dispose();
                        musicChannel[i] = currentMusic[i].Play(0.0f, "music");
                        musicChannel[i].Looping = true;
                    }
                    float targetGain = 1.0f;
                    if (targetMusic[i].DuckVolume)
                    {
                        targetGain = (float)Math.Sqrt(1.0f / activeTrackCount);
                    }
                    musicChannel[i].Gain = MathHelper.Lerp(musicChannel[i].Gain, targetGain, MusicLerpSpeed * deltaTime);
                }
            } 
        }

        private static void DisposeMusicChannel(int index)
        {
            musicChannel[index]?.Dispose(); musicChannel[index] = null;
            currentMusic[index]?.Dispose(); currentMusic[index] = null;
        }
        
        private static IEnumerable<BackgroundMusic> GetSuitableMusicClips(string musicType, float currentIntensity)
        {
            return musicClips.Where(music => 
                music != null && 
                music.Type == musicType && 
                currentIntensity >= music.IntensityRange.X &&
                currentIntensity <= music.IntensityRange.Y);
        }

        private static string GetCurrentMusicType()
        {
            if (OverrideMusicType != null) return OverrideMusicType;

            if (Screen.Selected == null || Screen.Selected != GameMain.GameScreen)
            {
                return "menu";
            }

            if (Character.Controlled != null &&
                Level.Loaded != null && Level.Loaded.Ruins != null &&
                Level.Loaded.Ruins.Any(r => r.Area.Contains(Character.Controlled.WorldPosition)))
            {
                return "ruins";
            }

            Submarine targetSubmarine = Character.Controlled?.Submarine;

            if ((targetSubmarine != null && targetSubmarine.AtDamageDepth) ||
                (GameMain.GameScreen != null && Screen.Selected == GameMain.GameScreen && GameMain.GameScreen.Cam.Position.Y < SubmarineBody.DamageDepth))
            {
                return "deep";
            }

            if (targetSubmarine != null)
            {                
                float floodedArea = 0.0f;
                float totalArea = 0.0f;
                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine != targetSubmarine) continue;
                    floodedArea += hull.WaterVolume;
                    totalArea += hull.Volume;
                }

                if (totalArea > 0.0f && floodedArea / totalArea > 0.25f) return "flooded";             
            }
            
            float enemyDistThreshold = 5000.0f;

            if (targetSubmarine != null)
            {
                enemyDistThreshold = Math.Max(enemyDistThreshold, Math.Max(targetSubmarine.Borders.Width, targetSubmarine.Borders.Height) * 2.0f);
            }

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead || !character.Enabled) continue;
                if (!(character.AIController is EnemyAIController enemyAI) || (!enemyAI.AttackHumans && !enemyAI.AttackRooms)) continue;

                if (targetSubmarine != null)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, targetSubmarine.WorldPosition) < enemyDistThreshold * enemyDistThreshold)
                    {
                        return "monster";
                    }
                }
                else if (Character.Controlled != null)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, Character.Controlled.WorldPosition) < enemyDistThreshold * enemyDistThreshold)
                    {
                        return "monster";
                    }
                }
            }

            if (GameMain.GameSession != null)
            {
                if (Submarine.Loaded != null && Level.Loaded != null && Submarine.MainSub.AtEndPosition)
                {
                    return "levelend";
                }
                if (Timing.TotalTime < GameMain.GameSession.RoundStartTime + 120.0)
                {
                    return "start";
                }
            }
            
            return "default";
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
            int splashIndex = MathHelper.Clamp((int)(strength + Rand.Range(-2, 2)), 0, SplashSounds.Count - 1);
            float range = 800.0f;
            var channel = SplashSounds[splashIndex].Play(1.0f, range, worldPosition, muffle: ShouldMuffleSound(Character.Controlled, worldPosition, range, null));
        }

        public static void PlayDamageSound(string damageType, float damage, PhysicsBody body)
        {
            Vector2 bodyPosition = body.DrawPosition;
            PlayDamageSound(damageType, damage, bodyPosition, 800.0f);
        }

        public static void PlayDamageSound(string damageType, float damage, Vector2 position, float range = 2000.0f, IEnumerable<string> tags = null)
        {
            damage = MathHelper.Clamp(damage + Rand.Range(-10.0f, 10.0f), 0.0f, 100.0f);
            var sounds = damageSounds.FindAll(s =>
                (s.damageRange == Vector2.Zero ||
                (damage >= s.damageRange.X && damage <= s.damageRange.Y)) &&
                s.damageType == damageType &&
                (tags == null ? string.IsNullOrEmpty(s.requiredTag) : tags.Contains(s.requiredTag)));

            if (!sounds.Any()) return;

            int selectedSound = Rand.Int(sounds.Count);
            sounds[selectedSound].sound.Play(1.0f, range, position, muffle: ShouldMuffleSound(Character.Controlled, position, range, null));
        }
        
    }
}
