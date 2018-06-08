using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelObjectPrefab
    {
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

        public int SoundTriggerIndex
        {
            get;
            private set;
        }
        public XElement SoundElement
        {
            get;
            private set;
        }
        public Vector2 SoundPosition
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            SoundTriggerIndex = -1;
            LoadElements(element, -1);
        }

        private void LoadElements(XElement element, int parentTriggerIndex)
        {
            //TODO: allow multiple triggers
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "leveltrigger":
                    case "trigger":
                        LoadElements(subElement, 0);
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
                        SoundElement = subElement;
                        SoundPosition = subElement.GetAttributeVector2("position", Vector2.Zero);
                        SoundTriggerIndex = parentTriggerIndex;
                        break;
                }
            }
        }
    }
}
