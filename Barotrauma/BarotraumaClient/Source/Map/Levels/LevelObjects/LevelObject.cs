using Barotrauma.Lights;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.SpriteDeformations;
using Lidgren.Network;

namespace Barotrauma
{
    partial class LevelObject
    {
        public float SwingTimer;
        public float ScaleOscillateTimer;

        public float CurrentSwingAmount;
        public Vector2 CurrentScaleOscillation;

        public float CurrentRotation;

        private List<SpriteDeformation> spriteDeformations = new List<SpriteDeformation>();

        public LightSource LightSource
        {
            get;
            private set;
        }

        public ParticleEmitter[] ParticleEmitters
        {
            get;
            private set;
        }
        public LevelTrigger[] ParticleEmitterTriggers
        {
            get;
            private set;
        }

        public Sound[] Sounds
        {
            get;
            private set;
        }
        public SoundChannel[] SoundChannels
        {
            get;
            private set;
        }
        public LevelTrigger[] SoundTriggers
        {
            get;
            private set;
        }

        public Vector2[,] CurrentSpriteDeformation
        {
            get;
            private set;
        }

        partial void InitProjSpecific()
        {
            CurrentSwingAmount = Prefab.SwingAmount;
            CurrentScaleOscillation = Prefab.ScaleOscillation;

            SwingTimer = Rand.Range(0.0f, MathHelper.TwoPi);
            ScaleOscillateTimer = Rand.Range(0.0f, MathHelper.TwoPi);

            if (Prefab.ParticleEmitterPrefabs != null)
            {
                ParticleEmitters = new ParticleEmitter[Prefab.ParticleEmitterPrefabs.Count];
                ParticleEmitterTriggers = new LevelTrigger[Prefab.ParticleEmitterPrefabs.Count];
                for (int i = 0; i < Prefab.ParticleEmitterPrefabs.Count; i++)
                {
                    ParticleEmitters[i] = new ParticleEmitter(Prefab.ParticleEmitterPrefabs[i]);
                    ParticleEmitterTriggers[i] = Prefab.ParticleEmitterTriggerIndex[i] > -1 ?
                        Triggers[Prefab.ParticleEmitterTriggerIndex[i]] : null;
                }
            }

            if (Prefab.LightSourceConfig != null)
            {
                LightSource = new LightSource(Prefab.LightSourceConfig)
                {
                    Position = new Vector2(Position.X, Position.Y),
                    IsBackground = true
                };
            }
            
            Sounds = new Sound[Prefab.Sounds.Count];
            SoundChannels = new SoundChannel[Prefab.Sounds.Count];
            SoundTriggers = new LevelTrigger[Prefab.Sounds.Count];
            for (int i = 0; i < Prefab.Sounds.Count; i++)
            {
                Sounds[i] = Submarine.LoadRoundSound(Prefab.Sounds[i].SoundElement, false);
                SoundTriggers[i] = Prefab.Sounds[i].TriggerIndex > -1 ? Triggers[Prefab.Sounds[i].TriggerIndex] : null;
            }

            foreach (XElement subElement in Prefab.Config.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "deformablesprite")
                {
                    foreach (XElement animationElement in subElement.Elements())
                    {
                        var newDeformation = SpriteDeformation.Load(animationElement);
                        if (newDeformation != null) spriteDeformations.Add(newDeformation);
                    }
                }
            }
        }

        public void Update(float deltaTime)
        {
            if (ParticleEmitters != null)
            {
                for (int i = 0; i < ParticleEmitters.Length; i++)
                {
                    if (ParticleEmitterTriggers[i] != null && !ParticleEmitterTriggers[i].IsTriggered) continue;
                    Vector2 emitterPos = LocalToWorld(Prefab.EmitterPositions[i]);
                    ParticleEmitters[i].Emit(deltaTime, emitterPos, hullGuess: null,
                        angle: ParticleEmitters[i].Prefab.CopyEntityAngle ? Rotation : 0.0f);
                }
            }

            CurrentRotation = Rotation;
            if (ActivePrefab.SwingFrequency > 0.0f)
            {
                SwingTimer += deltaTime * ActivePrefab.SwingFrequency;
                SwingTimer = SwingTimer % MathHelper.TwoPi;
                //lerp the swing amount to the correct value to prevent it from abruptly changing to a different value
                //when a trigger changes the swing amoung
                CurrentSwingAmount = MathHelper.Lerp(CurrentSwingAmount, ActivePrefab.SwingAmount, deltaTime * 10.0f);
                
                if (ActivePrefab.SwingAmount > 0.0f)
                {
                    CurrentRotation +=(float)Math.Sin(SwingTimer) * CurrentSwingAmount;
                }
            }

            if (ActivePrefab.ScaleOscillationFrequency > 0.0f)
            {
                ScaleOscillateTimer += deltaTime * ActivePrefab.ScaleOscillationFrequency;
                ScaleOscillateTimer = ScaleOscillateTimer % MathHelper.TwoPi;
                CurrentScaleOscillation = Vector2.Lerp(CurrentScaleOscillation, ActivePrefab.ScaleOscillation, deltaTime * 10.0f);
            }

            if (LightSource != null)
            {
                LightSource.Rotation = CurrentRotation;
            }
            
            if (spriteDeformations.Count > 0)
            {
                foreach (SpriteDeformation deformation in spriteDeformations)
                {
                    deformation.Update(deltaTime);
                }

                CurrentSpriteDeformation = SpriteDeformation.GetDeformation(spriteDeformations, ActivePrefab.DeformableSprite.Size);
            }

            for (int i = 0; i < Sounds.Length; i++)
            {
                if (SoundTriggers[i] == null || SoundTriggers[i].IsTriggered)
                {
                    Sound sound = Sounds[i];
                    Vector2 soundPos = LocalToWorld(new Vector2(Prefab.Sounds[i].Position.X, Prefab.Sounds[i].Position.Y));
                    if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), soundPos) <
                        sound.BaseFar * sound.BaseFar)
                    {
                        if (SoundChannels[i] == null || !SoundChannels[i].IsPlaying)
                        {
                            SoundChannels[i] = sound.Play(1.0f, sound.BaseFar, soundPos);
                        }
                        SoundChannels[i].Position = new Vector3(soundPos.X, soundPos.Y, 0.0f);
                    }
                }
                else if (SoundChannels[i] != null && SoundChannels[i].IsPlaying)
                {
                    SoundChannels[i].Dispose();
                    SoundChannels[i] = null;
                }
            }
        }

        public void ClientRead(NetBuffer msg)
        {
            for (int i = 0; i < Triggers.Count; i++)
            {
                if (!Triggers[i].UseNetworkSyncing) continue;
                Triggers[i].ClientRead(msg);
            }
        }

        partial void RemoveProjSpecific()
        {
            for (int i = 0; i < Sounds.Length; i++)
            {
                SoundChannels[i]?.Dispose();
                SoundChannels[i] = null;
                Sounds[i]?.Dispose();
                Sounds[i] = null;
            }
        }
    }
}
