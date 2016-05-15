using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Barotrauma.Sounds;
using System.Collections.Generic;

namespace Barotrauma
{
    public enum DamageSoundType 
    { 
        None, 
        StructureBlunt, StructureSlash, 
        LimbBlunt, LimbSlash, LimbArmor,
        Implode, Pressure 
    }

    public struct DamageSound
    {
        //the range of inflicted damage where the sound can be played
        //(10.0f, 30.0f) would be played when the inflicted damage is between 10 and 30
        public readonly Vector2 damageRange;

        public readonly DamageSoundType damageType;

        public readonly Sound sound;

        public DamageSound(Sound sound, Vector2 damageRange, DamageSoundType damageType)
        {
            this.sound = sound;
            this.damageRange = damageRange;
            this.damageType = damageType;
        }
    }

    public class BackgroundMusic
    {
        public readonly string file;
        public readonly string type;

        public readonly Vector2 priorityRange;

        public BackgroundMusic(string file, string type, Vector2 priorityRange)
        {
            this.file = file;
            this.type = type;
            this.priorityRange = priorityRange;
        }
    }

    static class SoundPlayer
    {
        public static Sound[] flowSounds = new Sound[3];

        public static Sound[] SplashSounds = new Sound[10];

        public static float MusicVolume = 1.0f;

        private const float MusicLerpSpeed = 0.1f;

        private static Sound[] waterAmbiences = new Sound[2];
        private static int[] waterAmbienceIndexes = new int[2];


        private static DamageSound[] damageSounds;

        private static BackgroundMusic currentMusic;
        private static BackgroundMusic targetMusic;
        private static BackgroundMusic[] musicClips;
        private static float currMusicVolume;

        private static Sound startDrone;

        public static bool Initialized;

        public static string OverrideMusicType
        {
            get;
            set;
        }
        
        public static IEnumerable<object> Init()
        {
            OverrideMusicType = null;

            startDrone = Sound.Load("Content/Sounds/startDrone.ogg", false);
            startDrone.Play();

            yield return CoroutineStatus.Running;

            waterAmbiences[0] = Sound.Load("Content/Sounds/Water/WaterAmbience1.ogg", false);
            yield return CoroutineStatus.Running;
            waterAmbiences[1] = Sound.Load("Content/Sounds/Water/WaterAmbience2.ogg", false);
            yield return CoroutineStatus.Running;
            flowSounds[0] = Sound.Load("Content/Sounds/Water/FlowSmall.ogg", false);
            yield return CoroutineStatus.Running;
            flowSounds[1] = Sound.Load("Content/Sounds/Water/FlowMedium.ogg", false);
            yield return CoroutineStatus.Running;
            flowSounds[2] = Sound.Load("Content/Sounds/Water/FlowLarge.ogg", false);
            yield return CoroutineStatus.Running;

            for (int i = 0; i < 10; i++ )
            {
                SplashSounds[i] = Sound.Load("Content/Sounds/Water/Splash"+(i)+".ogg", false);
                yield return CoroutineStatus.Running;
            }

            XDocument doc = ToolBox.TryLoadXml("Content/Sounds/sounds.xml");
            if (doc == null) yield return CoroutineStatus.Failure;

            yield return CoroutineStatus.Running;

            var xMusic = doc.Root.Elements("music").ToList();

            if (xMusic.Any())
            {
                musicClips = new BackgroundMusic[xMusic.Count];
                int i = 0;
                foreach (XElement element in xMusic)
                {
                    string file = ToolBox.GetAttributeString(element, "file", "");
                    string type = ToolBox.GetAttributeString(element, "type", "").ToLowerInvariant();
                    Vector2 priority = ToolBox.GetAttributeVector2(element, "priorityrange", new Vector2(0.0f, 100.0f));

                    musicClips[i] = new BackgroundMusic(file, type, priority);

                    yield return CoroutineStatus.Running;

                    i++;
                }
            }
            
            var xDamageSounds = doc.Root.Elements("damagesound").ToList();
            
            if (xDamageSounds.Any())
            {
                damageSounds = new DamageSound[xDamageSounds.Count()];
                int i = 0;
                foreach (XElement element in xDamageSounds)
                {
                    yield return CoroutineStatus.Running;

                    Sound sound = Sound.Load(ToolBox.GetAttributeString(element, "file", ""), false);
                    if (sound == null) continue;
                    
                    DamageSoundType damageSoundType = DamageSoundType.None;

                    try
                    {
                       damageSoundType =  (DamageSoundType)Enum.Parse(typeof(DamageSoundType), 
                        ToolBox.GetAttributeString(element, "damagesoundtype", "None"));
                    }
                    catch
                    {
                        damageSoundType = DamageSoundType.None;
                    }


                    damageSounds[i] = new DamageSound(
                        sound, ToolBox.GetAttributeVector2(element, "damagerange", new Vector2(0.0f,100.0f)), damageSoundType);
                    i++;
                }
            }

            Initialized = true;

            yield return CoroutineStatus.Success;

        }
        

        public static void Update()
        {
            UpdateMusic();

            if (startDrone!=null && !startDrone.IsPlaying)
            {
                startDrone.Remove();
                startDrone = null;                
            }

            if (Submarine.Loaded==null)  
            {
                for (int i = 0; i < waterAmbienceIndexes.Length; i++)
                {
                    if (waterAmbienceIndexes[i] <= 0) continue;

                    SoundManager.Stop(waterAmbienceIndexes[i]);
                    waterAmbienceIndexes[i] = 0;
                }  
                return;
            }

            float ambienceVolume = 0.8f;
            float lowpassHFGain = 1.0f;
            if (Character.Controlled != null)
            {
                AnimController animController = Character.Controlled.AnimController;
                if (animController.HeadInWater)
                {
                    ambienceVolume = 1.0f;
                    ambienceVolume += animController.Limbs[0].LinearVelocity.Length();

                    lowpassHFGain = 0.2f;
                }

                lowpassHFGain *= Character.Controlled.LowPassMultiplier;
            }

            //how fast the sub is moving, scaled to 0.0 -> 1.0
            float movementFactor = 0.0f;
            if (Submarine.Loaded != null)
            {
                movementFactor = (Submarine.Loaded.Velocity == Vector2.Zero) ? 0.0f : Submarine.Loaded.Velocity.Length() / 5.0f;

                movementFactor = MathHelper.Clamp(movementFactor, 0.0f, 1.0f);
            }

            SoundManager.LowPassHFGain = lowpassHFGain;
            waterAmbienceIndexes[0] = waterAmbiences[0].Loop(waterAmbienceIndexes[0], ambienceVolume * (1.0f-movementFactor));
            waterAmbienceIndexes[1] = waterAmbiences[1].Loop(waterAmbienceIndexes[1], ambienceVolume * movementFactor);

        }

        private static void UpdateMusic()
        {
            if (musicClips == null) return;
            
            List<BackgroundMusic> suitableMusic = GetSuitableMusicClips();

            if (suitableMusic.Count > 0 && !suitableMusic.Contains(currentMusic))
            {
                int index = Rand.Int(suitableMusic.Count());

                if (currentMusic == null || suitableMusic[index].file != currentMusic.file)
                {
                    targetMusic = suitableMusic[index];
                }
            }

            if (targetMusic == null || currentMusic == null || targetMusic.file != currentMusic.file)
            {
                currMusicVolume = MathHelper.Lerp(currMusicVolume, 0.0f, MusicLerpSpeed);
                if (currentMusic != null) Sound.StreamVolume(currMusicVolume);

                if (currMusicVolume < 0.01f)
                {
                    Sound.StopStream();
                    if (targetMusic != null) Sound.StartStream(targetMusic.file, currMusicVolume);
                    currentMusic = targetMusic;
                }
            }
            else
            {
                currMusicVolume = MathHelper.Lerp(currMusicVolume, MusicVolume, MusicLerpSpeed);
                Sound.StreamVolume(currMusicVolume);
            }
        }

        public static void SwitchMusic()
        {
            var suitableMusic = GetSuitableMusicClips();

            if (suitableMusic.Count > 1)
            {
                targetMusic = suitableMusic.Find(m => m != currentMusic);
            }
        }

        private static List<BackgroundMusic> GetSuitableMusicClips()
        {
            Task criticalTask = null;
            if (GameMain.GameSession != null && GameMain.GameSession.TaskManager != null)
            {
                foreach (Task task in GameMain.GameSession.TaskManager.Tasks)
                {
                    if (!task.IsStarted) continue;
                    if (criticalTask == null || task.Priority > criticalTask.Priority)
                    {
                        criticalTask = task;
                    }
                }
            }

            if (OverrideMusicType != null)
            {
                return musicClips.Where(x => x != null && x.type == OverrideMusicType).ToList();
            }
            else if (Character.Controlled != null && Level.Loaded != null && Level.Loaded.Ruins.Any(r => r.Area.Contains(Character.Controlled.WorldPosition)))
            {
                return musicClips.Where(x => x != null && x.type == "ruins").ToList();
            }
            else if (Submarine.Loaded != null && Submarine.Loaded.AtDamageDepth)
            {
                return musicClips.Where(x => x != null && x.type == "deep").ToList();
            }
            else if (criticalTask == null)
            {
                return musicClips.Where(x => x != null && x.type == "default").ToList();
            }

            return musicClips.Where(x =>
                x != null &&
                x.type == criticalTask.MusicType &&
                x.priorityRange.X < criticalTask.Priority &&
                x.priorityRange.Y > criticalTask.Priority).ToList();
            
        }

        public static void PlaySplashSound(Vector2 worldPosition, float strength)
        {
            int splashIndex = MathHelper.Clamp((int)(strength + Rand.Range(-2,2)), 0, SplashSounds.Length-1);

            SplashSounds[splashIndex].Play(1.0f, 800.0f, worldPosition);
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, PhysicsBody body)
        {
            Vector2 bodyPosition = body.DrawPosition;

            PlayDamageSound(damageType, damage, bodyPosition);
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, Vector2 position, float range = 2000.0f)
        {
            damage = MathHelper.Clamp(damage+Rand.Range(-10.0f, 10.0f), 0.0f, 100.0f);
            var sounds = damageSounds.Where(x => damage >= x.damageRange.X && damage <= x.damageRange.Y && x.damageType == damageType).ToList();
            if (!sounds.Any()) return;

            int selectedSound = Rand.Int(sounds.Count());

            sounds[selectedSound].sound.Play(1.0f, range, position);
            Debug.WriteLine("playing: " + sounds[selectedSound].sound);
        }
        
    }
}
