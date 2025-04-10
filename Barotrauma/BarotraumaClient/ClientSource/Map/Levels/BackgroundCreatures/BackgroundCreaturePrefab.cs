using Barotrauma.SpriteDeformations;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundCreaturePrefab : Prefab, ISerializableEntity
    {
        public readonly static PrefabCollection<BackgroundCreaturePrefab> Prefabs = new PrefabCollection<BackgroundCreaturePrefab>();

        public Sprite Sprite { get; private set; }
        public Sprite LightSprite { get; private set; }
        public DeformableSprite DeformableSprite { get; private set; }
        public DeformableSprite DeformableLightSprite { get; private set; }

        private readonly string name;

        public readonly XElement Config;

        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float Speed { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, DecimalCount = 3)]
        public float WanderAmount { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 3)]
        public float WanderZAmount { get; private set; }

        [Serialize(1, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int SwarmMin { get; private set; }

        [Serialize(1, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int SwarmMax { get; private set; }

        [Serialize(200.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10000.0f)]
        public float SwarmRadius { get; private set; }

        [Serialize(0.2f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f)]
        public float SwarmCohesion { get; private set; }

        [Serialize(10.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10000.0f)]
        public float MinDepth { get; private set; }

        [Serialize(1000.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10000.0f)]
        public float MaxDepth { get; private set; }

        [Serialize(10000.0f, IsPropertySaveable.Yes, description: "Creatures fade out to the background color of the level the further they are from the camera. This value is the depth at which the object becomes \"maximally\" faded out."), Editable]
        public float FadeOutDepth
        {
            get;
            private set;
        }
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool FadeOut { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool DisableRotation { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool DisableFlipping { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
        public float Scale { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
        public float Commonness { get; private set; }

        [Serialize(1000, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int MaxCount { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float FlashInterval { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float FlashDuration { get; private set; }

        public string Name => name;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        /// <summary>
        /// Only used for editing sprite deformation parameters. The actual LevelObjects use separate SpriteDeformation instances.
        /// </summary>
        public List<SpriteDeformation> SpriteDeformations
        {
            get;
            private set;
        } = new List<SpriteDeformation>();

        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public Dictionary<Identifier, float> OverrideCommonness = new Dictionary<Identifier, float>();

        public BackgroundCreaturePrefab(ContentXElement element, BackgroundCreaturePrefabsFile file) : base(file, ParseIdentifier(element))
        {
            name = element.Name.ToString();

            Config = element;

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, lazyLoad: true);
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement, lazyLoad: true);
                        foreach (XElement deformElement in subElement.Elements())
                        {
                            var deformation = SpriteDeformation.Load(deformElement, Name);
                            if (deformation != null)
                            {
                                SpriteDeformations.Add(deformation);
                            }
                        }
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

        public static Identifier ParseIdentifier(XElement element)
        {
            Identifier identifier = element.GetAttributeIdentifier("identifier", "");
            if (identifier.IsEmpty)
            {
                identifier = element.NameAsIdentifier();
            }
            return identifier;
        }

        public float GetCommonness(LevelData levelData)
        {
            if (levelData?.GenerationParams is not { } generationParams || generationParams.Identifier.IsEmpty)
            {
                return Commonness;
            }

            if (OverrideCommonness.TryGetValue(generationParams.Identifier, out float commonness) || (!generationParams.OldIdentifier.IsEmpty && OverrideCommonness.TryGetValue(generationParams.OldIdentifier, out commonness)) ||
                OverrideCommonness.TryGetValue(levelData.Biome.Identifier, out commonness))
            {
                return commonness;
            }
            return Commonness;
        }

        public override void Dispose()
        {
            Sprite?.Remove();
            Sprite = null;
            LightSprite?.Remove();
            LightSprite = null;
            DeformableLightSprite?.Remove();
            DeformableLightSprite = null;
            DeformableSprite?.Remove();
            DeformableSprite = null;
        }
    }

}
