using Barotrauma.Extensions;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
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

        public readonly bool ContinueFromPreviousTime;
        public int PreviousTime;

        public readonly XElement Element;
                
        public BackgroundMusic(XElement element)
        {
            this.File = Path.GetFullPath(element.GetAttributeString("file", "")).CleanUpPath();
            this.Type = element.GetAttributeString("type", "").ToLowerInvariant();
            this.IntensityRange = element.GetAttributeVector2("intensityrange", new Vector2(0.0f, 100.0f));
            this.DuckVolume = element.GetAttributeBool("duckvolume", false);
            this.ContinueFromPreviousTime = element.GetAttributeBool("continuefromprevioustime", false);
            this.Element = element;
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
        private static Sound waterAmbienceIn, waterAmbienceOut, waterAmbienceMoving;
        private static readonly SoundChannel[] waterAmbienceChannels = new SoundChannel[3];

        private static float ambientSoundTimer;
        private static Vector2 ambientSoundInterval = new Vector2(20.0f, 40.0f); //x = min, y = max

        private static SoundChannel hullSoundChannel;
        private static Hull hullSoundSource;
        private static float hullSoundTimer;
        private static Vector2 hullSoundInterval = new Vector2(45.0f, 90.0f); //x = min, y = max

        //misc
        private static float[] targetFlowLeft, targetFlowRight;
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
        const float FireSoundMediumLimit = 100.0f;
        const float FireSoundLargeLimit = 200.0f; //switch to large fire sound when the size of a firesource is above this
        const int fireSizes = 3;
        private static string[] fireSoundTags = new string[fireSizes] { "fire", "firemedium", "firelarge" };

        // TODO: could use a dictionary to split up the list into smaller lists of same type?
        private static List<DamageSound> damageSounds;

        private static Dictionary<GUISoundType, List<Sound>> guiSounds;

        private static bool firstTimeInMainMenu = true;

        private static Sound startUpSound;

        public static bool Initialized;

        public static string OverrideMusicType
        {
            get;
            set;
        }

        public static float? OverrideMusicDuration;

        public static int SoundCount;

        private static List<XElement> loadedSoundElements;

        private static bool SoundElementsEquivalent(XElement a, XElement b)
        {
            string filePathA = a.GetAttributeString("file", "").CleanUpPath();
            float baseGainA = a.GetAttributeFloat("volume", 1.0f);
            float rangeA = a.GetAttributeFloat("range", 1000.0f);
            string filePathB = b.GetAttributeString("file", "").CleanUpPath();
            float baseGainB = b.GetAttributeFloat("volume", 1.0f);
            float rangeB = b.GetAttributeFloat("range", 1000.0f);
            return a.Name.ToString().Equals(b.Name.ToString(), StringComparison.OrdinalIgnoreCase) &&
                   filePathA == filePathB && MathUtils.NearlyEqual(baseGainA, baseGainB) &&
                   MathUtils.NearlyEqual(rangeA, rangeB);
        }

        public static IEnumerable<object> Init()
        {
            OverrideMusicType = null;

            var soundFiles = GameMain.Instance.GetFilesOfType(ContentType.Sounds);

            List<XElement> soundElements = new List<XElement>();
            foreach (ContentFile soundFile in soundFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(soundFile.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    DebugConsole.NewMessage($"Overriding all sounds with {soundFile.Path}", Color.Yellow);
                    soundElements.Clear();
                }
                soundElements.AddRange(mainElement.Elements());
            }

            SoundCount = 1 + soundElements.Count();

            var startUpSoundElement = soundElements.Find(e => e.Name.ToString().Equals("startupsound", StringComparison.OrdinalIgnoreCase));
            if (startUpSoundElement != null)
            {
                startUpSound = GameMain.SoundManager.LoadSound(startUpSoundElement, false);
                startUpSound?.Play();
            }

            yield return CoroutineStatus.Running;

            List<KeyValuePair<string, Sound>> miscSoundList = new List<KeyValuePair<string, Sound>>();
            damageSounds ??= new List<DamageSound>();
            musicClips ??= new List<BackgroundMusic>();
            guiSounds ??= new Dictionary<GUISoundType, List<Sound>>();

            bool firstWaterAmbienceLoaded = false;

            foreach (XElement soundElement in soundElements)
            {
                yield return CoroutineStatus.Running;

                if (loadedSoundElements != null && loadedSoundElements.Any(e => SoundElementsEquivalent(e, soundElement)))
                {
                    continue;
                }

                try
                {
                    switch (soundElement.Name.ToString().ToLowerInvariant())
                    {
                        case "music":
                            var newMusicClip = new BackgroundMusic(soundElement);
                            if (File.Exists(newMusicClip.File))
                            {
                                musicClips.AddIfNotNull(newMusicClip);
                                if (loadedSoundElements != null)
                                {
                                    if (newMusicClip.Type.Equals("menu", StringComparison.OrdinalIgnoreCase))
                                    {
                                        targetMusic[0] = newMusicClip;
                                    }
                                }
                            }
                            else
                            {
                                DebugConsole.NewMessage($"Music file \"{newMusicClip.File}\" not found.");
                            }
                            break;
                        case "splash":
                            SplashSounds.AddIfNotNull(GameMain.SoundManager.LoadSound(soundElement, false));
                            break;
                        case "flow":
                            FlowSounds.AddIfNotNull(GameMain.SoundManager.LoadSound(soundElement, false));
                            break;
                        case "waterambience":
                            //backwards compatibility (1st waterambience used to be played both inside and outside, 2nd when moving)
                            if (!firstWaterAmbienceLoaded)
                            {
                                waterAmbienceIn?.Dispose();
                                waterAmbienceOut?.Dispose();
                                if (File.Exists(soundElement.GetAttributeString("file", "")))
                                {
                                    waterAmbienceIn = GameMain.SoundManager.LoadSound(soundElement, false);
                                    waterAmbienceOut = GameMain.SoundManager.LoadSound(soundElement, false);
                                }
                                else
                                {
                                    waterAmbienceIn = GameMain.SoundManager.LoadSound(soundElement, false, "Content/Sounds/Water/WaterAmbienceIn.ogg");
                                    waterAmbienceOut = GameMain.SoundManager.LoadSound(soundElement, false, "Content/Sounds/Water/WaterAmbienceOut.ogg");
                                }
                                firstWaterAmbienceLoaded = true;
                            }
                            else
                            {
                                waterAmbienceMoving?.Dispose();
                                if (File.Exists(soundElement.GetAttributeString("file", "")))
                                {
                                    waterAmbienceMoving = GameMain.SoundManager.LoadSound(soundElement, false);
                                }
                                else
                                {
                                    waterAmbienceMoving = GameMain.SoundManager.LoadSound(soundElement, false, "Content/Sounds/Water/WaterAmbienceMoving.ogg");
                                }
                            }
                            break;
                        case "waterambiencein":
                            waterAmbienceIn?.Dispose();
                            waterAmbienceIn = GameMain.SoundManager.LoadSound(soundElement, false);
                            break;
                        case "waterambienceout":
                            waterAmbienceOut?.Dispose();
                            waterAmbienceOut = GameMain.SoundManager.LoadSound(soundElement, false);
                            break;
                        case "waterambiencemoving":
                            waterAmbienceMoving?.Dispose();
                            waterAmbienceMoving = GameMain.SoundManager.LoadSound(soundElement, false);
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
                        case "guisound":
                            Sound guiSound = GameMain.SoundManager.LoadSound(soundElement, stream: false);
                            if (guiSound == null) { continue; }
                            if (Enum.TryParse(soundElement.GetAttributeString("guisoundtype", null), true, out GUISoundType soundType))
                            {
                                if (guiSounds.ContainsKey(soundType))
                                {
                                    guiSounds[soundType].Add(guiSound);
                                }
                                else
                                {
                                    guiSounds.Add(soundType, new List<Sound>() { guiSound });
                                }
                            }
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
                catch (System.IO.FileNotFoundException e)
                {
                    DebugConsole.ThrowError("Error while initializing SoundPlayer.", e);
                }
            }

            musicClips.RemoveAll(mc => !soundElements.Any(e => SoundElementsEquivalent(mc.Element, e)));

            for (int i = 0; i < currentMusic.Length; i++)
            {
                if (currentMusic[i] != null && !musicClips.Any(mc => mc.File == currentMusic[i].Filename))
                {
                    DisposeMusicChannel(i);
                }
            }

            SplashSounds.ForEach(s =>
            {
                if (!soundElements.Any(e => SoundElementsEquivalent(s.XElement, e))) { s.Dispose(); }
            });
            SplashSounds.RemoveAll(s => s.Disposed);

            FlowSounds.ForEach(s =>
            {
                if (!soundElements.Any(e => SoundElementsEquivalent(s.XElement, e))) { s.Dispose(); }
            });
            FlowSounds.RemoveAll(s => s.Disposed);

            damageSounds.ForEach(s =>
            {
                if (!soundElements.Any(e => SoundElementsEquivalent(s.sound.XElement, e))) { s.sound.Dispose(); }
            });
            damageSounds.RemoveAll(s => s.sound.Disposed);

            guiSounds.ForEach(kvp =>
            {
                kvp.Value?.ForEach(s =>
                {
                    if (!soundElements.Any(e => SoundElementsEquivalent(s.XElement, e))) { s.Dispose(); }
                });
            });
            guiSounds.ForEach(kvp => kvp.Value?.RemoveAll(s => s.Disposed));

            miscSounds?.ForEach(g => g.ForEach(s =>
            {
                if (!soundElements.Any(e => SoundElementsEquivalent(s.XElement, e))) { s.Dispose(); }
                else { miscSoundList.Add(new KeyValuePair<string, Sound>(g.Key, s)); }
            }));

            flowSoundChannels?.ForEach(ch => ch?.Dispose());
            flowSoundChannels = new SoundChannel[FlowSounds.Count];
            flowVolumeLeft = new float[FlowSounds.Count];
            flowVolumeRight = new float[FlowSounds.Count];
            targetFlowLeft = new float[FlowSounds.Count];
            targetFlowRight = new float[FlowSounds.Count];

            fireSoundChannels?.ForEach(ch => ch?.Dispose());
            fireSoundChannels = new SoundChannel[fireSizes];
            fireVolumeLeft = new float[fireSizes];
            fireVolumeRight = new float[fireSizes];

            miscSounds = miscSoundList.ToLookup(kvp => kvp.Key, kvp => kvp.Value);

            Initialized = true;

            loadedSoundElements = soundElements;

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
                    if (waterAmbienceChannels[i] == null) { continue; }
                    waterAmbienceChannels[i].FadeOutAndDispose();
                    waterAmbienceChannels[i] = null;
                }
                for (int i = 0; i < FlowSounds.Count; i++)
                {
                    if (flowSoundChannels[i] == null) { continue; }
                    flowSoundChannels[i].FadeOutAndDispose();
                    flowSoundChannels[i] = null;
                }
                for (int i = 0; i < fireSoundChannels.Length; i++)
                {
                    if (fireSoundChannels[i] == null) { continue; }
                    fireSoundChannels[i].FadeOutAndDispose();
                    fireSoundChannels[i] = null;
                }
                fireVolumeLeft[0] = 0.0f; fireVolumeLeft[1] = 0.0f;
                fireVolumeRight[0] = 0.0f; fireVolumeRight[1] = 0.0f;
                if (hullSoundChannel != null)
                {
                    hullSoundChannel.FadeOutAndDispose();
                    hullSoundChannel = null;
                    hullSoundSource = null;
                }
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
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    movementSoundVolume = 0.0f;
                }
                if (!MathUtils.IsValid(insideSubFactor))
                {
                    string errorMsg = "Failed to update water ambience volume - inside sub value invalid (" + insideSubFactor + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    insideSubFactor = 0.0f;
                }
            }

            for (int i = 0; i < 3; i++)
            {
                float volume = 0.0f;
                Sound sound = null;
                switch (i)
                {
                    case 0:
                        volume = ambienceVolume * (1.0f - movementSoundVolume) * insideSubFactor;
                        sound = waterAmbienceIn;
                        break;
                    case 1:
                        volume = ambienceVolume * movementSoundVolume * insideSubFactor;
                        sound = waterAmbienceMoving;
                        break;
                    case 2:
                        volume = 1.0f - insideSubFactor;
                        sound = waterAmbienceOut;
                        break;
                }

                if (sound == null) { continue; }

                // Consider the volume set in sounds.xml
                volume *= sound.BaseGain;
                if ((waterAmbienceChannels[i] == null || !waterAmbienceChannels[i].IsPlaying) && volume > 0.01f)
                {
                    waterAmbienceChannels[i] = sound.Play(volume, "waterambience");
                    waterAmbienceChannels[i].Looping = true;
                }
                else if (waterAmbienceChannels[i] != null)
                {
                    waterAmbienceChannels[i].Gain += deltaTime * Math.Sign(volume - waterAmbienceChannels[i].Gain);
                    if (waterAmbienceChannels[i].Gain < 0.01f)
                    {
                        waterAmbienceChannels[i].FadeOutAndDispose();
                    }
                }
            }
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
                    if (gap.Open < 0.01f) { continue; }
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
                    if (distFallOff >= 0.99f) continue;

                    //flow at the left side
                    if (diff.X < 0)
                    {
                        targetFlowLeft[flowSoundIndex] += 1.0f - distFallOff;
                    }
                    else
                    {
                        targetFlowRight[flowSoundIndex] += 1.0f - distFallOff;
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
                if (!Level.IsLoadedOutpost && Character.Controlled?.CurrentHull?.Submarine is Submarine sub &&
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
            if (sound == null) { return null; }
            return PlaySound(sound, position, volume ?? sound.BaseGain, range ?? sound.BaseFar, 1.0f, hullGuess);
        }

        public static SoundChannel PlaySound(Sound sound, Vector2 position, float? volume = null, float? range = null, float? freqMult = null, Hull hullGuess = null, bool ignoreMuffling = false)
        {
            if (sound == null)
            {
                string errorMsg = "Error in SoundPlayer.PlaySound (sound was null)\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.PlaySound:SoundNull" + Environment.StackTrace.CleanupStackTrace(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
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
                //something should be playing, but the targetMusic is invalid
                else if (!musicClips.Any(mc => mc.File == targetMusic[i].File))
                {
                    targetMusic[i] = GetSuitableMusicClips(targetMusic[i].Type, 0.0f).GetRandom();
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
                        try
                        {
                            currentMusic[i] = GameMain.SoundManager.LoadSound(targetMusic[i].File, true);
                        }
                        catch (System.IO.InvalidDataException e)
                        {
                            DebugConsole.ThrowError($"Failed to load the music clip \"{targetMusic[i].File}\".", e);
                            musicClips.Remove(targetMusic[i]);
                            targetMusic[i] = null;
                            break;
                        }
                        musicChannel[i] = currentMusic[i].Play(0.0f, "music");
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
            var clip = musicClips.Find(m => m.File == musicChannel[index]?.Sound?.Filename);
            if (clip != null)
            {
                if (clip.ContinueFromPreviousTime) { clip.PreviousTime = musicChannel[index].StreamSeekPos; }
            }

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
            if (OverrideMusicType != null) { return OverrideMusicType; }

            if (Screen.Selected == null) { return "menu"; }

            if (Screen.Selected == GameMain.CharacterEditorScreen ||
                Screen.Selected == GameMain.LevelEditorScreen ||
                Screen.Selected == GameMain.ParticleEditorScreen ||
                Screen.Selected == GameMain.SpriteEditorScreen ||
                Screen.Selected == GameMain.SubEditorScreen ||
                Screen.Selected == GameMain.EventEditorScreen ||
                (Screen.Selected == GameMain.GameScreen && GameMain.GameSession?.GameMode is TestGameMode))
            {
                return "editor";
            }

            if (Screen.Selected != GameMain.GameScreen) { return firstTimeInMainMenu ? "menu" : "default"; }

            firstTimeInMainMenu = false;


            if (Character.Controlled != null)
            {
                if (Level.Loaded != null && Level.Loaded.Ruins != null &&
                    Level.Loaded.Ruins.Any(r => r.Area.Contains(Character.Controlled.WorldPosition)))
                {
                    return "ruins";
                }

                if (Character.Controlled.Submarine?.Info?.IsWreck ?? false)
                {
                    return "wreck";
                }

                if (Level.IsLoadedOutpost)
                {
                    // Only return music type for location types which have music tracks defined
                    var locationType = Level.Loaded.StartLocation?.Type?.Identifier?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(locationType) && musicClips.Any(c => c.Type == locationType))
                    {
                        return locationType;
                    }
                }
            }

            Submarine targetSubmarine = Character.Controlled?.Submarine;
            if (targetSubmarine != null && targetSubmarine.AtDamageDepth)
            {
                return "deep";
            }
            if (GameMain.GameScreen != null && Screen.Selected == GameMain.GameScreen && Submarine.MainSub != null &&
                Level.Loaded != null && Level.Loaded.GetRealWorldDepth(GameMain.GameScreen.Cam.Position.Y) > Submarine.MainSub.RealWorldCrushDepth)
            {
                return "deep";
            }

                if (targetSubmarine != null)
            {                
                float floodedArea = 0.0f;
                float totalArea = 0.0f;
                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine != targetSubmarine) { continue; }
                    floodedArea += hull.WaterVolume;
                    totalArea += hull.Volume;
                }

                if (totalArea > 0.0f && floodedArea / totalArea > 0.25f) { return "flooded"; }        
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
                if (Submarine.Loaded != null && Level.Loaded != null && Submarine.MainSub != null && Submarine.MainSub.AtEndExit)
                {
                    return "levelend";
                }
                if (Timing.TotalTime < GameMain.GameSession.RoundStartTime + 120.0 && 
                    Level.Loaded?.Type == LevelData.LevelType.LocationConnection)
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

        private static readonly List<DamageSound> tempList = new List<DamageSound>();
        public static void PlayDamageSound(string damageType, float damage, Vector2 position, float range = 2000.0f, IEnumerable<string> tags = null)
        {
            damage = MathHelper.Clamp(damage + Rand.Range(-10.0f, 10.0f), 0.0f, 100.0f);
            tempList.Clear();
            foreach (var s in damageSounds)
            {
                if ((s.damageRange == Vector2.Zero ||
                    (damage >= s.damageRange.X && damage <= s.damageRange.Y)) &&
                    string.Equals(s.damageType, damageType, StringComparison.OrdinalIgnoreCase) &&
                    (tags == null ? string.IsNullOrEmpty(s.requiredTag) : tags.Contains(s.requiredTag)))
                {
                    tempList.Add(s);
                }
            }
            tempList.GetRandom().sound?.Play(1.0f, range, position, muffle: ShouldMuffleSound(Character.Controlled, position, range, null));
        }

        public static void PlayUISound(GUISoundType soundType)
        {
            if (guiSounds == null || guiSounds.Count < 1) { return; }
            if (guiSounds.TryGetValue(soundType, out List<Sound> sounds))
            {
                if (sounds == null || sounds.Count < 1) { return; }
                sounds.GetRandom()?.Play(null, "ui");
            }
        }
    }
}
