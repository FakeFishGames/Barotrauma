using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundCreaturePrefab
    {
        public readonly Sprite Sprite, LightSprite;
        public readonly DeformableSprite DeformableSprite, DeformableLightSprite;

        public readonly string Name;

        public readonly XElement Config;

        [Serialize(1.0f, true)]
        public float Speed { get; private set; }

        [Serialize(0.0f, true)]
        public float WanderAmount { get; private set; }

        [Serialize(0.0f, true)]
        public float WanderZAmount { get; private set; }

        [Serialize(1, true)]
        public int SwarmMin { get; private set; }

        [Serialize(1, true)]
        public int SwarmMax { get; private set; }

        [Serialize(200.0f, true)]
        public float SwarmRadius { get; private set; }

        [Serialize(0.2f, true)]
        public float SwarmCohesion { get; private set; }

        [Serialize(10.0f, true)]
        public float MinDepth { get; private set; }

        [Serialize(1000.0f, true)]
        public float MaxDepth { get; private set; }

        [Serialize(false, true)]
        public bool DisableRotation { get; private set; }

        [Serialize(false, true)]
        public bool DisableFlipping { get; private set; }

        [Serialize(1.0f, true)]
        public float Scale { get; private set; }

        [Serialize(1.0f, true)]
        public float Commonness { get; private set; }

        [Serialize(1000, true)]
        public int MaxCount { get; private set; }

        [Serialize(0.0f, true)]
        public float FlashInterval { get; private set; }

        [Serialize(0.0f, true)]
        public float FlashDuration { get; private set; }


        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public Dictionary<string, float> OverrideCommonness = new Dictionary<string, float>();

        public BackgroundCreaturePrefab(XElement element)
        {
            Name = element.Name.ToString();

            Config = element;

            SerializableProperty.DeserializeProperties(this, element);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, lazyLoad: true);
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement, lazyLoad: true);
                        break;
                    case "lightsprite":
                        LightSprite = new Sprite(subElement, lazyLoad: true);
                        break;
                    case "deformablelightsprite":
                        DeformableLightSprite = new DeformableSprite(subElement, lazyLoad: true);
                        break;
                    case "overridecommonness":
                        string levelType = subElement.GetAttributeString("leveltype", "").ToLowerInvariant();
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, subElement.GetAttributeFloat("commonness", 1.0f));
                        }
                        break;
                }
            }
        }

        public float GetCommonness(LevelGenerationParams generationParams)
        {
            if (generationParams?.Identifier != null &&
                (OverrideCommonness.TryGetValue(generationParams.Identifier, out float commonness) ||
                (generationParams.OldIdentifier != null && OverrideCommonness.TryGetValue(generationParams.OldIdentifier, out commonness))))
            {
                return commonness;
            }
            return Commonness;
        }
    }

}
