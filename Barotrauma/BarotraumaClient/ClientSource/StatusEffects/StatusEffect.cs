using Barotrauma.Items.Components;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class StatusEffect
    {
        private List<ParticleEmitter> particleEmitters;

        private readonly static HashSet<StatusEffect> ActiveLoopingSounds = new HashSet<StatusEffect>();
        private static double LastMuffleCheckTime;
        private readonly List<RoundSound> sounds = new List<RoundSound>();
        public IEnumerable<RoundSound> Sounds { get { return sounds; } }
        private SoundSelectionMode soundSelectionMode;
        private SoundChannel soundChannel;
        private Entity soundEmitter;
        private double loopStartTime;
        private bool loopSound;
        /// <summary>
        /// Each new sound overrides the existing sounds that were launched with this status effect, meaning the old sound will be faded out and disposed and the new sound will be played instead of the old.
        /// Normally the call to play the sound is ignored if there's an existing sound playing when the effect triggers.
        /// Used for example for ensuring that rapid playing sounds restart playing even when the previous clip(s) have not yet stopped.
        /// Use with caution.
        /// </summary>
        private bool forcePlaySounds;

        partial void InitProjSpecific(ContentXElement element, string parentDebugName)
        {
            particleEmitters = new List<ParticleEmitter>();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "sound":
                        var sound = RoundSound.Load(subElement);
                        if (sound?.Sound != null)
                        {
                            loopSound = subElement.GetAttributeBool("loop", false);
                            if (subElement.GetAttribute("selectionmode") != null)
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
            forcePlaySounds = element.GetAttributeBool(nameof(forcePlaySounds), false);
        }

        partial void ApplyProjSpecific(float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Hull hull, Vector2 worldPosition, bool playSound)
        {
            if (playSound)
            {
                PlaySound(entity, hull, worldPosition);
            }

            foreach (ParticleEmitter emitter in particleEmitters)
            {
                float angle = 0.0f;
                float particleRotation = 0.0f;
                bool mirrorAngle = false;
                if (emitter.Prefab.Properties.CopyEntityAngle)
                {
                    Limb targetLimb = null;
                    if (entity is Item item && item.body != null)
                    {
                        angle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                        particleRotation = -item.body.Rotation;
                        if (emitter.Prefab.Properties.CopyEntityDir && item.body.Dir < 0.0f)
                        {
                            particleRotation += MathHelper.Pi;
                            mirrorAngle = true;
                        }
                    }
                    else if (entity is Character c && !c.Removed && targetLimbs?.FirstOrDefault(l => l != LimbType.None) is LimbType l)
                    {
                        targetLimb = c.AnimController.GetLimb(l);
                    }
                    else
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            if (targets[i] is Limb limb)
                            {
                                targetLimb = limb;
                                break;
                            }
                        }
                    }
                    if (targetLimb != null && !targetLimb.Removed)
                    {
                        angle = targetLimb.body.Rotation + ((targetLimb.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                        particleRotation = -targetLimb.body.Rotation;
                        float offset = targetLimb.Params.GetSpriteOrientation() - MathHelper.PiOver2;
                        particleRotation += offset;
                        if (emitter.Prefab.Properties.CopyEntityDir && targetLimb.body.Dir < 0.0f)
                        {
                            particleRotation += MathHelper.Pi;
                            mirrorAngle = true;
                        }
                    }
                }

                emitter.Emit(deltaTime, worldPosition, hull, angle: angle, particleRotation: particleRotation, mirrorAngle: mirrorAngle);
            }            
        }

        private bool ignoreMuffling;

        private void PlaySound(Entity entity, Hull hull, Vector2 worldPosition)
        {
            if (sounds.Count == 0) { return; }

            if (soundChannel == null || !soundChannel.IsPlaying || forcePlaySounds)
            {
                if (soundChannel != null && soundChannel.IsPlaying)
                {
                    soundChannel.FadeOutAndDispose();
                }
                if (soundSelectionMode == SoundSelectionMode.All)
                {
                    foreach (RoundSound sound in sounds)
                    {
                        if (sound?.Sound == null)
                        {
                            string errorMsg = $"Error in StatusEffect.ApplyProjSpecific1 (sound \"{sound?.Filename ?? "unknown"}\" was null)\n" + Environment.StackTrace.CleanupStackTrace();
                            GameAnalyticsManager.AddErrorEventOnce("StatusEffect.ApplyProjSpecific:SoundNull1" + Environment.StackTrace.CleanupStackTrace(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            return;
                        }
                        soundChannel = SoundPlayer.PlaySound(sound.Sound, worldPosition, sound.Volume, sound.Range, hullGuess: hull, ignoreMuffling: sound.IgnoreMuffling, freqMult: sound.GetRandomFrequencyMultiplier());
                        ignoreMuffling = sound.IgnoreMuffling;
                        if (soundChannel != null) { soundChannel.Looping = loopSound; }
                    }
                }
                else
                {
                    int selectedSoundIndex;
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
                    if (selectedSound?.Sound == null)
                    {
                        string errorMsg = $"Error in StatusEffect.ApplyProjSpecific2 (sound \"{selectedSound?.Filename ?? "unknown"}\" was null)\n" + Environment.StackTrace.CleanupStackTrace();
                        GameAnalyticsManager.AddErrorEventOnce("StatusEffect.ApplyProjSpecific:SoundNull2" + Environment.StackTrace.CleanupStackTrace(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        return;
                    }
                    soundChannel = SoundPlayer.PlaySound(selectedSound.Sound, worldPosition, selectedSound.Volume, selectedSound.Range, hullGuess: hull, ignoreMuffling: selectedSound.IgnoreMuffling, freqMult: selectedSound.GetRandomFrequencyMultiplier());
                    ignoreMuffling = selectedSound.IgnoreMuffling;
                    if (soundChannel != null) { soundChannel.Looping = loopSound; }
                }
            }
            else
            {
                soundChannel.Position = new Vector3(worldPosition, 0.0f);
            }

            if (soundChannel != null && soundChannel.Looping)
            {
                ActiveLoopingSounds.Add(this);
                soundEmitter = entity;
                loopStartTime = Timing.TotalTime;
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
                if (Timing.TotalTime > statusEffect.loopStartTime + 0.1 && !DurationList.Any(e => e.Parent == statusEffect))
                {
                    statusEffect.soundChannel.FadeOutAndDispose();
                    statusEffect.soundChannel = null;
                }
                else
                {
                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);
                    if (doMuffleCheck && !statusEffect.ignoreMuffling)
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
