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

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float Speed { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float WanderAmount { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float WanderZAmount { get; private set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int SwarmMin { get; private set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int SwarmMax { get; private set; }

        [Serialize(200.0f, IsPropertySaveable.Yes)]
        public float SwarmRadius { get; private set; }

        [Serialize(0.2f, IsPropertySaveable.Yes)]
        public float SwarmCohesion { get; private set; }

        [Serialize(10.0f, IsPropertySaveable.Yes)]
        public float MinDepth { get; private set; }

        [Serialize(1000.0f, IsPropertySaveable.Yes)]
        public float MaxDepth { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool DisableRotation { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool DisableFlipping { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float Scale { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float Commonness { get; private set; }

        [Serialize(1000, IsPropertySaveable.Yes)]
        public int MaxCount { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float FlashInterval { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float FlashDuration { get; private set; }


        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public Dictionary<Identifier, float> OverrideCommonness = new Dictionary<Identifier, float>();

        public BackgroundCreaturePrefab(ContentXElement element)
        {
            Name = element.Name.ToString();

            Config = element;

            SerializableProperty.DeserializeProperties(this, element);

            foreach (var subElement in element.Elements())
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
                        Identifier levelType = subElement.GetAttributeIdentifier("leveltype", Identifier.Empty);
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
            if (generationParams != null &&
                !generationParams.Identifier.IsEmpty &&
                (OverrideCommonness.TryGetValue(generationParams.Identifier, out float commonness) ||
                (!generationParams.OldIdentifier.IsEmpty && OverrideCommonness.TryGetValue(generationParams.OldIdentifier, out commonness))))
            {
                return commonness;
            }
            return Commonness;
        }
    }

}
