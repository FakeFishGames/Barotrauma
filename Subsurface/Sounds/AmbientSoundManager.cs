using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Subsurface.Sounds;

namespace Subsurface
{
    public enum DamageSoundType { None, StructureBlunt, StructureSlash, LimbBlunt, LimbSlash, Implode }

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

    static class AmbientSoundManager
    {
        private static Sound[] music = new Sound[4];

        public static Sound[] flowSounds = new Sound[3];

        private static Sound waterAmbience;
        private static int waterAmbienceIndex;

        private static DamageSound[] damageSounds;
        
        public static void Init(string filePath)
        {
            //Sound.Loop(music[0]);

            waterAmbience = Sound.Load("Content/Sounds/Water/WaterAmbience.ogg");

            flowSounds[0] = Sound.Load("Content/Sounds/Water/FlowSmall.ogg");
            flowSounds[1] = Sound.Load("Content/Sounds/Water/FlowMedium.ogg");
            flowSounds[2] = Sound.Load("Content/Sounds/Water/FlowLarge.ogg");

            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;
            
            var xDamageSounds = doc.Root.Elements("damagesound");

            if (xDamageSounds.Count()>0)
            {
                damageSounds = new DamageSound[xDamageSounds.Count()];
                int i = 0;
                foreach (XElement element in xDamageSounds)
                {
                    Sound sound = Sound.Load(ToolBox.GetAttributeString(element, "file", ""));
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

            //Sound.StartStream("Content/Sounds/Music/Simplex.ogg", 0.3f);

        }
        

        public static void Update()
        {
            float ambienceVolume = 0.5f;
            float lowpassHFGain = 1.0f;
            if (Character.Controlled != null)
            {
                AnimController animController = Character.Controlled.animController;
                if (animController.HeadInWater)
                {
                    ambienceVolume = 0.5f;
                    ambienceVolume += animController.limbs[0].LinearVelocity.Length();

                    lowpassHFGain = 0.2f;
                }
            }

            SoundManager.LowPassHFGain = lowpassHFGain;
            waterAmbienceIndex = waterAmbience.Loop(waterAmbienceIndex, ambienceVolume);
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, Body body)
        {
            Vector2 bodyPosition = ConvertUnits.ToDisplayUnits(body.Position);
            bodyPosition.Y = -bodyPosition.Y;

            PlayDamageSound(damageType, damage, bodyPosition);
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, Vector2 position)
        {
            damage = MathHelper.Clamp(damage, 0.0f, 100.0f);
            var sounds = damageSounds.Where(x => damage >= x.damageRange.X && damage <= x.damageRange.Y && x.damageType == damageType).ToList();
            if (sounds.Count() == 0) return;

            int selectedSound = Game1.localRandom.Next(sounds.Count());

            int i = 0;
            foreach (var s in sounds)
            {
                if (i == selectedSound)
                {
                    Debug.WriteLine(s.sound.Play(1.0f, 2000.0f, position));
                    Debug.WriteLine("playing: " + s.sound);
                    return;
                }
                i++;
            }
        }
        
    }
}
