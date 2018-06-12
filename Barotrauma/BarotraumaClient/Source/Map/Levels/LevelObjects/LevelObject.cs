using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class LevelObject
    {
        public float SwingTimer;
        public float ScaleOscillateTimer;

        public float CurrentSwingAmount;
        public Vector2 CurrentScaleOscillation;

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

            Sounds = new Sound[Prefab.Sounds.Count];
            SoundChannels = new SoundChannel[Prefab.Sounds.Count];
            SoundTriggers = new LevelTrigger[Prefab.Sounds.Count];
            for (int i = 0; i < Prefab.Sounds.Count; i++)
            {
                Sounds[i] = Submarine.LoadRoundSound(Prefab.Sounds[i].SoundElement, false);
                SoundTriggers[i] = Prefab.Sounds[i].TriggerIndex > -1 ? Triggers[Prefab.Sounds[i].TriggerIndex] : null;
            }
        }
    }
}
