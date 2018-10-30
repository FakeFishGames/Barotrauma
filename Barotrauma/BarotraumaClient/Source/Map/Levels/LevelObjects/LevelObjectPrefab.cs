using Barotrauma.Lights;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
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
        } = new List<int>();
        public List<LightSourceParams> LightSourceParams
        {
            get;
            private set;
        } = new List<Lights.LightSourceParams>();

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
                        LightSourceTriggerIndex.Add(parentTriggerIndex);
                        LightSourceParams.Add(new LightSourceParams(subElement));
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

        public void Save(XElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "childobject":
                    case "lightsource":
                        subElement.Remove();
                        break;
                }
            }
            
            foreach (LightSourceParams lightSourceParams in LightSourceParams)
            {
                var lightElement = new XElement("LightSource");
                SerializableProperty.SerializeProperties(lightSourceParams, lightElement);
                element.Add(lightElement);
            }

            foreach (ChildObject childObj in ChildObjects)
            {
                element.Add(new XElement("ChildObject",
                    new XAttribute("names", string.Join(", ", childObj.AllowedNames)),
                    new XAttribute("mincount", childObj.MinCount),
                    new XAttribute("maxcount", childObj.MaxCount)));
            }

            foreach (KeyValuePair<string, float> overrideCommonness in OverrideCommonness)
            {
                bool elementFound = false;
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "overridecommonness"
                        && subElement.GetAttributeString("leveltype", "") == overrideCommonness.Key)
                    {
                        subElement.Attribute("commonness").Value = overrideCommonness.Value.ToString("G", CultureInfo.InvariantCulture);
                        elementFound = true;
                        break;
                    }
                }
                if (!elementFound)
                {
                    element.Add(new XElement("overridecommonness",
                        new XAttribute("leveltype", overrideCommonness.Key),
                        new XAttribute("commonness", overrideCommonness.Value.ToString("G", CultureInfo.InvariantCulture))));
                }
            }
        }
    }
}
