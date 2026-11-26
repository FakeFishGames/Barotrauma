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
        public IEnumerable<RoundSound> Sounds => sounds;

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

        private CoroutineHandle playSoundAfterLoadedCoroutine;

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
                        if (!IsOverride)
                        {
                            if (subElement.GetAttributeEnum("type", def: Type) != Type)
                            {
                                DebugConsole.AddWarning($"Potential error in {parentDebugName} ({subElement}): the sound has a different type than the status effect. Sounds in status effects cannot have specific types anyway, they depend on the type of the effect and play when the effect executes.");
                            }
                        }
                        break;
                }
            }
            forcePlaySounds = element.GetAttributeBool(nameof(forcePlaySounds), false);
        }

        partial void ApplyProjSpecific(float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Hull hull, Vector2 worldPosition, bool playSound)
        {
            if (steamTimeLineEventToTrigger != default)
            {
                SteamTimelineManager.AddTimelineEvent(
                    steamTimeLineEventToTrigger.title,
                    steamTimeLineEventToTrigger.description,
                    steamTimeLineEventToTrigger.icon,
                    priority: 1,
                    submarine: entity?.Submarine);
            }

            if (playSound)
            {
                PlaySound(entity, hull, worldPosition);
            }

            foreach (ParticleEmitter emitter in particleEmitters)
            {
                float angle = 0.0f;
                float particleRotation = 0.0f;
                bool mirrorAngle = false;
                if (emitter.Prefab.Properties.CopyEntityAngle || emitter.Prefab.Properties.CopyTargetAngle)
                {
                    bool entityAngleAssigned = false;
                    Limb targetLimb = null;
                    if (entity is Item item)
                    {
                        if (item.body != null)
                        {
                            angle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                            particleRotation = -item.body.Rotation;
                            if (emitter.Prefab.Properties.CopyEntityDir && item.body.Dir < 0.0f)
                            {
                                particleRotation += MathHelper.Pi;
                                mirrorAngle = true;
                            }
                        }
                        else
                        {
                            angle = -item.RotationRad;
                            if (item.FlippedX) { angle += MathHelper.Pi; }
                            particleRotation = item.RotationRad;
                        }
                        entityAngleAssigned = true;
                    }
                    if (emitter.Prefab.Properties.CopyTargetAngle || !entityAngleAssigned)
                    {
                        if (entity is Character c && !c.Removed && targetLimbs?.FirstOrDefault(l => l != LimbType.None) is LimbType l)
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
                                else if (targets[i] is Character character && GetFirstTargetLimb(character) is Limb characterLimb)
                                {
                                    targetLimb = characterLimb;
                                    break;
                                }
                            }
                        }
                    }
                    if (targetLimb != null && !targetLimb.Removed)
                    {
                        //emission direction, in world space (counter-clockwise). When facing left, Pi turns it to point the other way.
                        angle = targetLimb.body.Rotation + ((targetLimb.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                        //particle sprite rotation is in display space (y-down, clockwise), hence the negation.
                        //offset accounts for the limb's sprite orientation in the spritesheet.
                        particleRotation = -targetLimb.body.Rotation;
                        float offset = targetLimb.Params.GetSpriteOrientation() - MathHelper.PiOver2;
                        particleRotation += offset;
                        if (emitter.Prefab.Properties.CopyEntityDir && targetLimb.body.Dir < 0.0f)
                        {
                            //Mirror the facing-right rotation horizontally: mirrored = Pi - (-rot + offset).
                            //The ragdoll already mirrors the limb when flipping (rot facing left == -rot facing right),
                            //so written in terms of the current rotation this becomes Pi - rot - offset.
                            particleRotation = -targetLimb.body.Rotation + MathHelper.Pi - offset;
                            // Also mirror the emitter's AngleMin/AngleMax spread
                            mirrorAngle = true;
                        }
                    }
                }

                // Entities in a moving submarine layer give the particles the velocity of the layer, otherwise they would lag behind as the layer moves
                Vector2 baseVelocity = 
                    entity is MapEntity { Submarine: Submarine sub } mapEntity && !string.IsNullOrEmpty(mapEntity.Layer) ?
                    sub.GetLayerVelocity(mapEntity.Layer.ToIdentifier()) :
                    Vector2.Zero;

                emitter.Emit(deltaTime, worldPosition, hull, angle: angle, particleRotation: particleRotation, mirrorAngle: mirrorAngle, baseVelocity: baseVelocity);
            }
        }

        private bool ignoreMuffling;

        private RoundSound lastPlayingSound;

        private void PlaySound(Entity entity, Hull hull, Vector2 worldPosition)
        {
            if (sounds.Count == 0) { return; }
            if (entity is { Submarine.Loading: true }) { return; }

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
                        PlaySoundOrDelayIfNotLoaded(sound);
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
                    PlaySoundOrDelayIfNotLoaded(selectedSound);
                }
            }
            else
            {
                soundChannel.Position = new Vector3(worldPosition, 0.0f);
                if (lastPlayingSound != null && lastPlayingSound.Stream) { lastPlayingSound.LastStreamSeekPos = soundChannel.StreamSeekPos; }
            }

            KeepLoopingSoundAlive(soundChannel);

            void PlaySoundOrDelayIfNotLoaded(RoundSound selectedSound)
            {
                if (playSoundAfterLoadedCoroutine != null) { return; }
                if (selectedSound.Sound.Loading)
                {
                    playSoundAfterLoadedCoroutine = CoroutineManager.StartCoroutine(PlaySoundAfterLoaded(selectedSound));                    
                }
                else
                {
                    PlaySound(selectedSound);
                }
            }

            IEnumerable<CoroutineStatus> PlaySoundAfterLoaded(RoundSound selectedSound)
            {
                float maxWaitTimer = 1.0f;
                while (selectedSound.Sound.Loading && maxWaitTimer > 0.0f)
                {
                    maxWaitTimer -= CoroutineManager.DeltaTime;
                    yield return CoroutineStatus.Running;
                }
                if (!selectedSound.Sound.Loading)
                {
                    PlaySound(selectedSound);
                }
                playSoundAfterLoadedCoroutine = null;
                yield return CoroutineStatus.Success;
            }

            void PlaySound(RoundSound selectedSound)
            {
                //if the sound loops, we must make sure the existing channel has been stopped first before attempting to play a new one
                System.Diagnostics.Debug.Assert(
                    soundChannel == null || !soundChannel.IsPlaying || soundChannel.FadingOutAndDisposing || !soundChannel.Looping,
                    "A StatusEffect attempted to play a sound, but an looping sound is already playing. The looping sound should be stopped before playing a new one, or it will keep looping indefinitely.");
                
                soundChannel = SoundPlayer.PlaySound(selectedSound.Sound, worldPosition, selectedSound.GetSoundVolume(entity as ISerializableEntity), selectedSound.Range, hullGuess: hull, ignoreMuffling: selectedSound.IgnoreMuffling, freqMult: selectedSound.GetRandomFrequencyMultiplier());
                ignoreMuffling = selectedSound.IgnoreMuffling;
                if (soundChannel != null)
                {
                    if (soundChannel.IsStream && lastPlayingSound == selectedSound)
                    {
                        soundChannel.StreamSeekPos = lastPlayingSound.LastStreamSeekPos;
                    }
                    lastPlayingSound = selectedSound;
                    soundChannel.Looping = loopSound;
                    KeepLoopingSoundAlive(soundChannel);
                }
            }

            void KeepLoopingSoundAlive(SoundChannel soundChannel)
            {
                if (soundChannel != null && soundChannel.Looping)
                {
                    ActiveLoopingSounds.Add(this);
                    soundEmitter = entity;
                    loopStartTime = Timing.TotalTime;
                }                
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
                else if (statusEffect.soundEmitter is { Removed: false })
                {
                    if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f)) > statusEffect.soundChannel.Far * statusEffect.soundChannel.Far *1.1f)
                    {
                        statusEffect.soundChannel.FadeOutAndDispose();
                        statusEffect.soundChannel = null;
                        return;
                    }
                    else
                    {
                        statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);
                        if (statusEffect.lastPlayingSound != null)
                        {
                            float targetGain = statusEffect.lastPlayingSound.GetSoundVolume(statusEffect.soundEmitter as ISerializableEntity);
                            float gainDiff = targetGain - statusEffect.soundChannel.Gain;
                            statusEffect.soundChannel.Gain += Math.Abs(gainDiff) < 0.1f ? gainDiff : Math.Sign(gainDiff) * 0.1f;
                        }
                        if (doMuffleCheck && !statusEffect.ignoreMuffling)
                        {
                            statusEffect.soundChannel.Muffled = SoundPlayer.ShouldMuffleSound(
                                Character.Controlled, statusEffect.soundEmitter.WorldPosition, statusEffect.soundChannel.Far, Character.Controlled?.CurrentHull);
                        }
                    }
                }
            }
            ActiveLoopingSounds.RemoveWhere(s => s.soundChannel == null);
        }
    }
}
