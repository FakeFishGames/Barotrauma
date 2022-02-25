using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CaveGenerationParams : PrefabWithUintIdentifier, ISerializableEntity
    {
        public readonly static PrefabCollection<CaveGenerationParams> CaveParams = new PrefabCollection<CaveGenerationParams>();

        public string Name => Identifier.Value;

        private int minWidth, maxWidth;
        private int minHeight, maxHeight;

        private int minBranchCount, maxBranchCount;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public readonly Dictionary<Identifier, float> OverrideCommonness = new Dictionary<Identifier, float>();

        [Editable, Serialize(1.0f, IsPropertySaveable.Yes)]
        public float Commonness
        {
            get;
            private set;
        }

        [Serialize(8000, IsPropertySaveable.Yes), Editable(MinValueInt = 1000, MaxValueInt = 100000)]
        public int MinWidth
        {
            get { return minWidth; }
            set { minWidth = Math.Max(value, 1000); }
        }

        [Serialize(10000, IsPropertySaveable.Yes), Editable(MinValueInt = 1000, MaxValueInt = 1000000)]
        public int MaxWidth
        {
            get { return maxWidth; }
            set { maxWidth = Math.Max(value, minWidth); }
        }

        [Serialize(8000, IsPropertySaveable.Yes), Editable(MinValueInt = 1000, MaxValueInt = 100000)]
        public int MinHeight
        { 
            get { return minHeight; }
            set { minHeight = Math.Max(value, 1000); }
        }

        [Serialize(10000, IsPropertySaveable.Yes), Editable(MinValueInt = 1000, MaxValueInt = 1000000)]
        public int MaxHeight
        {
            get { return maxHeight; }
            set { maxHeight = Math.Max(value, minHeight); }
        }

        [Serialize(2, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MinBranchCount
        {
            get { return minBranchCount; }
            set { minBranchCount = Math.Max(value, 0); }
        }

        [Serialize(4, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MaxBranchCount
        {
            get { return maxBranchCount; }
            set { maxBranchCount = Math.Max(value, minBranchCount); }
        }

        [Serialize(50, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int LevelObjectAmount
        {
            get;
            set;
        }

        [Serialize(0.1f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0, MaxValueFloat = 1.0f, DecimalCount = 2 )]
        public float DestructibleWallRatio
        {
            get;
            set;
        }

        public readonly Sprite WallSprite;
        public readonly Sprite WallEdgeSprite;

        public static CaveGenerationParams GetRandom(LevelGenerationParams generationParams, bool abyss, Rand.RandSync rand)
        {
            var caveParams = CaveParams.OrderBy(p => p.UintIdentifier).ToList();
            if (caveParams.All(p => p.GetCommonness(generationParams, abyss) <= 0.0f))
            {
                return caveParams.First();
            }
            return ToolBox.SelectWeightedRandom(caveParams.ToList(), caveParams.Select(p => p.GetCommonness(generationParams, abyss)).ToList(), rand);
        }

        public float GetCommonness(LevelGenerationParams generationParams, bool abyss)
        {
            if (generationParams != null &&
                generationParams.Identifier != Identifier.Empty &&
                OverrideCommonness.TryGetValue(abyss ? "abyss".ToIdentifier() : generationParams.Identifier, out float commonness))
            {
                return commonness;
            }
            return Commonness;
        }

        public CaveGenerationParams(ContentXElement element, CaveGenerationParametersFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "wall":
                        WallSprite = new Sprite(subElement);
                        break;
                    case "walledge":
                        WallEdgeSprite = new Sprite(subElement);
                        break;
                    case "overridecommonness":
                        Identifier levelType = subElement.GetAttributeIdentifier("leveltype", "");
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, subElement.GetAttributeFloat("commonness", 1.0f));
                        }
                        break;
                }
            }
        }

        public void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element, true);
            foreach (KeyValuePair<Identifier, float> overrideCommonness in OverrideCommonness)
            {
                bool elementFound = false;
                foreach (var subElement in element.Elements())
                {
                    if (subElement.NameAsIdentifier() == "overridecommonness"
                        && subElement.GetAttributeIdentifier("leveltype", "") == overrideCommonness.Key)
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

        public override void Dispose()
        {
            WallSprite?.Remove(); WallEdgeSprite?.Remove();
        }
    }
}
