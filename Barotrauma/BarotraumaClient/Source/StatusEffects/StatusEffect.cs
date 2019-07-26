using System;
using System.Collections.Generic;
using System.Text;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class StatusEffect
    {
        private List<ParticleEmitter> particleEmitters;

        private static HashSet<StatusEffect> ActiveLoopingSounds = new HashSet<StatusEffect>();
        private static double LastMuffleCheckTime;
        private List<RoundSound> sounds = new List<RoundSound>();
        private SoundSelectionMode soundSelectionMode;
        private SoundChannel soundChannel;
        private Entity soundEmitter;
        private double loopStartTime;
        private bool loopSound;

        partial void InitProjSpecific(XElement element, string parentDebugName)
        {
            particleEmitters = new List<ParticleEmitter>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "sound":
                        var sound = Submarine.LoadRoundSound(subElement);
                        if (sound != null)
                        {
                            loopSound = subElement.GetAttributeBool("loop", false);
                            if (subElement.Attribute("selectionmode") != null)
                            {
                                if (Enum.TryParse(subElement.GetAttributeString("selectionmode", "Random"), out SoundSelectionMode selectionMode))
                                {
                                    soundSelectionMode = selectionMode;
                                }
                            }
                            sounds.Add(sound);
                        }
                        break;
                }
            }
        }

        partial void ApplyProjSpecific(float deltaTime, Entity entity, List<ISerializableEntity> targets, Hull hull)
        {
            if (entity == null) { return; }

            if (sounds.Count > 0)
            {
                if (soundChannel == null || !soundChannel.IsPlaying)
                {
                    if (soundSelectionMode == SoundSelectionMode.All)
                    {
                        foreach (RoundSound sound in sounds)
                        {
                            soundChannel = SoundPlayer.PlaySound(sound.Sound, entity.WorldPosition, sound.Volume, sound.Range, hull);
                            if (soundChannel != null) soundChannel.Looping = loopSound;
                        }
                    }
                    else
                    {
                        int selectedSoundIndex = 0;
                        if (soundSelectionMode == SoundSelectionMode.ItemSpecific && entity is Item item)
                        {
                            selectedSoundIndex = item.ID % sounds.Count;
                        }
                        else if (soundSelectionMode == SoundSelectionMode.CharacterSpecific && entity is Character user)
                        {
                            selectedSoundIndex = user.ID % sounds.Count;
                        }
                        else
                        {
                            selectedSoundIndex = Rand.Int(sounds.Count);
                        }
                        var selectedSound = sounds[selectedSoundIndex];
                        soundChannel = SoundPlayer.PlaySound(selectedSound.Sound, entity.WorldPosition, selectedSound.Volume, selectedSound.Range, hull);
                        if (soundChannel != null) soundChannel.Looping = loopSound;
                    }
                }

                if (soundChannel != null && soundChannel.Looping)
                {
                    ActiveLoopingSounds.Add(this);
                    soundEmitter = entity;
                    loopStartTime = Timing.TotalTime;
                }
            }

            foreach (ParticleEmitter emitter in particleEmitters)
            {
                float angle = 0.0f;
                if (emitter.Prefab.CopyEntityAngle)
                {
                    if (entity is Item item && item.body != null)
                    {
                        angle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                    }
                }

                emitter.Emit(deltaTime, entity.WorldPosition, hull, angle);
            }
            
        }

        static partial void UpdateAllProjSpecific(float deltaTime)
        {
            bool doMuffleCheck = Timing.TotalTime > LastMuffleCheckTime + 0.2;
            if (doMuffleCheck) { LastMuffleCheckTime = Timing.TotalTime; }
            foreach (StatusEffect statusEffect in ActiveLoopingSounds)
            {
                if (statusEffect.soundChannel == null) { continue; }

                //stop looping sounds if the statuseffect hasn't been applied in 0.1
                //= keeping the sound looping requires continuously applying the statuseffect
                if (Timing.TotalTime > statusEffect.loopStartTime + 0.1)
                {
                    statusEffect.soundChannel.FadeOutAndDispose();
                    statusEffect.soundChannel = null;
                }
                else
                {
                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);
                    if (doMuffleCheck)
                    {
                        statusEffect.soundChannel.Muffled = SoundPlayer.ShouldMuffleSound(
                            Character.Controlled, statusEffect.soundEmitter.WorldPosition, statusEffect.soundChannel.Far, Character.Controlled?.CurrentHull);
                    }
                }
            }
            ActiveLoopingSounds.RemoveWhere(s => s.soundChannel == null);
        }
    }
}
