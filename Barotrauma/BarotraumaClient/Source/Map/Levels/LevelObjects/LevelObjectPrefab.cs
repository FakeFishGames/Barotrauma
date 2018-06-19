using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelObjectPrefab
    {
        public class SoundConfig
        {
            public readonly XElement SoundElement;
            public readonly Vector2 Position;
            public readonly int TriggerIndex;

            public SoundConfig(XElement element, int triggerIndex)
            {
                SoundElement = element;
                Position = element.GetAttributeVector2("position", Vector2.Zero);
                TriggerIndex = triggerIndex;
            }
        }

        public List<int> ParticleEmitterTriggerIndex
        {
            get;
            private set;
        }
        public List<ParticleEmitterPrefab> ParticleEmitterPrefabs
        {
            get;
            private set;
        }
        public List<Vector2> EmitterPositions
        {
            get;
            private set;
        }

        public List<SoundConfig> Sounds
        {
            get;
            private set;
        }

        public List<int> LightSourceTriggerIndex
        {
            get;
            private set;
        }
        public List<XElement> LightSourceConfigs
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            Sounds = new List<SoundConfig>();
            LoadElementsProjSpecific(element, -1);
        }

        private void LoadElementsProjSpecific(XElement element, int parentTriggerIndex)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "leveltrigger":
                    case "trigger":
                        LoadElementsProjSpecific(subElement, LevelTriggerElements.IndexOf(subElement));
                        break;
                    case "lightsource":
                        if (LightSourceConfigs == null)
                        {
                            LightSourceConfigs = new List<XElement>();
                            LightSourceTriggerIndex = new List<int>();
                        }

                        LightSourceTriggerIndex.Add(parentTriggerIndex);
                        LightSourceConfigs.Add(subElement);
                        break;
                    case "particleemitter":
                        if (ParticleEmitterPrefabs == null)
                        {
                            ParticleEmitterPrefabs = new List<ParticleEmitterPrefab>();
                            EmitterPositions = new List<Vector2>();
                            ParticleEmitterTriggerIndex = new List<int>();
                        }

                        ParticleEmitterPrefabs.Add(new ParticleEmitterPrefab(subElement));
                        ParticleEmitterTriggerIndex.Add(parentTriggerIndex);
                        EmitterPositions.Add(subElement.GetAttributeVector2("position", Vector2.Zero));
                        break;
                    case "sound":
                        Sounds.Add(new SoundConfig(subElement, parentTriggerIndex));
                        break;
                }
            }
        }
    }
}
