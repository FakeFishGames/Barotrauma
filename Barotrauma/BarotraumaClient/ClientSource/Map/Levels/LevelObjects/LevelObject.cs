using Barotrauma.Lights;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.SpriteDeformations;
using System.Linq;
using FarseerPhysics.Dynamics;

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

        public Vector2 CurrentScale
        {
            get;
            private set;
        } = Vector2.One;

        public LightSource[] LightSources
        {
            get;
            private set;
        }
        public LevelTrigger[] LightSourceTriggers
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

        public RoundSound[] Sounds
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
            Sprite?.EnsureLazyLoaded();
            SpecularSprite?.EnsureLazyLoaded();
            Prefab.DeformableSprite?.EnsureLazyLoaded();

            CurrentSwingAmount = Prefab.SwingAmountRad;
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

            if (Prefab.LightSourceParams != null)
            {
                LightSources = new LightSource[Prefab.LightSourceParams.Count];
                LightSourceTriggers = new LevelTrigger[Prefab.LightSourceParams.Count];
                for (int i = 0; i < Prefab.LightSourceParams.Count; i++)
                {
                    LightSources[i] = new LightSource(Prefab.LightSourceParams[i])
                    {
                        Position = new Vector2(Position.X, Position.Y),
                        IsBackground = true
                    };
                    LightSourceTriggers[i] = Prefab.LightSourceTriggerIndex[i] > -1 ?
                        Triggers[Prefab.LightSourceTriggerIndex[i]] : null;
                }
            }

            Sounds = new RoundSound[Prefab.Sounds.Count];
            SoundChannels = new SoundChannel[Prefab.Sounds.Count];
            SoundTriggers = new LevelTrigger[Prefab.Sounds.Count];
            for (int i = 0; i < Prefab.Sounds.Count; i++)
            {
                Sounds[i] = Submarine.LoadRoundSound(Prefab.Sounds[i].SoundElement, false);
                SoundTriggers[i] = Prefab.Sounds[i].TriggerIndex > -1 ? Triggers[Prefab.Sounds[i].TriggerIndex] : null;
            }

            int j = 0;
            foreach (XElement subElement in Prefab.Config.Elements())
            {
                if (!subElement.Name.ToString().Equals("deformablesprite", StringComparison.OrdinalIgnoreCase)) { continue; }               
                foreach (XElement animationElement in subElement.Elements())
                {
                    var newDeformation = SpriteDeformation.Load(animationElement, Prefab.Name);
                    if (newDeformation != null)
                    {
                        newDeformation.Params = Prefab.SpriteDeformations[j].Params;
                        spriteDeformations.Add(newDeformation);
                        j++;
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
                CurrentSwingAmount = MathHelper.Lerp(CurrentSwingAmount, ActivePrefab.SwingAmountRad, deltaTime * 10.0f);

                if (ActivePrefab.SwingAmountRad > 0.0f)
                {
                    CurrentRotation += (float)Math.Sin(SwingTimer) * CurrentSwingAmount;
                }
            }

            CurrentScale = Vector2.One * Scale;
            if (ActivePrefab.ScaleOscillationFrequency > 0.0f)
            {
                ScaleOscillateTimer += deltaTime * ActivePrefab.ScaleOscillationFrequency;
                ScaleOscillateTimer = ScaleOscillateTimer % MathHelper.TwoPi;
                CurrentScaleOscillation = Vector2.Lerp(CurrentScaleOscillation, ActivePrefab.ScaleOscillation, deltaTime * 10.0f);
                
                float sin = (float)Math.Sin(ScaleOscillateTimer);
                CurrentScale *= new Vector2(
                    1.0f + sin * CurrentScaleOscillation.X,
                    1.0f + sin * CurrentScaleOscillation.Y);                
            }

            if (LightSources != null)
            {
                for (int i = 0; i < LightSources.Length; i++)
                {
                    if (LightSourceTriggers[i] != null) LightSources[i].Enabled = LightSourceTriggers[i].IsTriggered;
                    LightSources[i].Rotation = -CurrentRotation;
                    LightSources[i].SpriteScale = CurrentScale;
                }
            }

            if (spriteDeformations.Count > 0)
            {
                UpdateDeformations(deltaTime);
            }

            for (int i = 0; i < Sounds.Length; i++)
            {
                if (Sounds[i] == null) { continue; }
                if (SoundTriggers[i] == null || SoundTriggers[i].IsTriggered)
                {
                    RoundSound roundSound = Sounds[i];
                    Vector2 soundPos = LocalToWorld(new Vector2(Prefab.Sounds[i].Position.X, Prefab.Sounds[i].Position.Y));
                    if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), soundPos) <
                        roundSound.Range * roundSound.Range)
                    {
                        if (SoundChannels[i] == null || !SoundChannels[i].IsPlaying)
                        {
                            SoundChannels[i] = roundSound.Sound.Play(roundSound.Volume, roundSound.Range, soundPos);
                        }
                        SoundChannels[i].Position = new Vector3(soundPos.X, soundPos.Y, 0.0f);
                    }
                }
                else if (SoundChannels[i] != null && SoundChannels[i].IsPlaying)
                {
                    SoundChannels[i].FadeOutAndDispose();
                    SoundChannels[i] = null;
                }
            }
        }

        private void UpdateDeformations(float deltaTime)
        {
            foreach (SpriteDeformation deformation in spriteDeformations)
            {
                if (deformation is PositionalDeformation positionalDeformation)
                {
                    UpdatePositionalDeformation(positionalDeformation, deltaTime);
                }
                deformation.Update(deltaTime);
            }
            CurrentSpriteDeformation = SpriteDeformation.GetDeformation(spriteDeformations, ActivePrefab.DeformableSprite.Size);
            foreach (LightSource lightSource in LightSources)
            {
                if (lightSource?.DeformableLightSprite != null)
                {
                    lightSource.DeformableLightSprite.Deform(CurrentSpriteDeformation);
                }
            }
        }

        private void UpdatePositionalDeformation(PositionalDeformation positionalDeformation, float deltaTime)
        {
            Matrix matrix = ActivePrefab.DeformableSprite.GetTransform(
                                Position,
                                ActivePrefab.DeformableSprite.Origin,
                                CurrentRotation,
                                Vector2.One * Scale);

            Matrix rotationMatrix = Matrix.CreateRotationZ(CurrentRotation);

            foreach (LevelTrigger trigger in Triggers)
            {
                foreach (Entity triggerer in trigger.Triggerers)
                {
                    Vector2 moveAmount = triggerer.WorldPosition - trigger.TriggererPosition[triggerer];

                    moveAmount = Vector2.Transform(moveAmount, rotationMatrix);
                    moveAmount /= (ActivePrefab.DeformableSprite.Size * Scale);
                    moveAmount.Y = -moveAmount.Y;

                    positionalDeformation.Deform(trigger.WorldPosition, moveAmount, deltaTime, Matrix.Invert(matrix) *
                        Matrix.CreateScale(1.0f / ActivePrefab.DeformableSprite.Size.X, 1.0f / ActivePrefab.DeformableSprite.Size.Y, 1));
                }
            }
        }

        public void ClientRead(IReadMessage msg)
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
            }
            if (LightSources != null)
            {
                for (int i = 0; i < LightSources.Length; i++)
                {
                    LightSources[i].Remove();
                }
                LightSources = null;
            }
        }
    }
}
