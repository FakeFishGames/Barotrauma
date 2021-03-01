using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CaveGenerationParams : ISerializableEntity
    {
        public static List<CaveGenerationParams> CaveParams { get; private set; }

        public string Name
        {
            get { return Identifier; }
        }

        public readonly string Identifier;

        private int minWidth, maxWidth;
        private int minHeight, maxHeight;

        private int minBranchCount, maxBranchCount;

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public readonly Dictionary<string, float> OverrideCommonness = new Dictionary<string, float>();

        [Editable, Serialize(1.0f, true)]
        public float Commonness
        {
            get;
            private set;
        }

        [Serialize(8000, true), Editable(MinValueInt = 1000, MaxValueInt = 100000)]
        public int MinWidth
        {
            get { return minWidth; }
            set { minWidth = Math.Max(value, 1000); }
        }

        [Serialize(10000, true), Editable(MinValueInt = 1000, MaxValueInt = 1000000)]
        public int MaxWidth
        {
            get { return maxWidth; }
            set { maxWidth = Math.Max(value, minWidth); }
        }

        [Serialize(8000, true), Editable(MinValueInt = 1000, MaxValueInt = 100000)]
        public int MinHeight
        { 
            get { return minHeight; }
            set { minHeight = Math.Max(value, 1000); }
        }

        [Serialize(10000, true), Editable(MinValueInt = 1000, MaxValueInt = 1000000)]
        public int MaxHeight
        {
            get { return maxHeight; }
            set { maxHeight = Math.Max(value, minHeight); }
        }

        [Serialize(2, true), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MinBranchCount
        {
            get { return minBranchCount; }
            set { minBranchCount = Math.Max(value, 0); }
        }

        [Serialize(4, true), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MaxBranchCount
        {
            get { return maxBranchCount; }
            set { maxBranchCount = Math.Max(value, minBranchCount); }
        }

        [Serialize(50, true), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int LevelObjectAmount
        {
            get;
            set;
        }

        [Serialize(0.1f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1.0f, DecimalCount = 2 )]
        public float DestructibleWallRatio
        {
            get;
            set;
        }

        public Sprite WallSprite { get; private set; }
        public Sprite WallEdgeSprite { get; private set; }

        public static CaveGenerationParams GetRandom(LevelGenerationParams generationParams, Rand.RandSync rand)
        {
            if (CaveParams.All(p => p.GetCommonness(generationParams) <= 0.0f))
            {
                return CaveParams.First();
            }
            return ToolBox.SelectWeightedRandom(CaveParams, CaveParams.Select(p => p.GetCommonness(generationParams)).ToList(), rand);
        }

        public float GetCommonness(LevelGenerationParams generationParams)
        {
            if (generationParams?.Identifier != null &&
                OverrideCommonness.TryGetValue(generationParams.Identifier, out float commonness))
            {
                return commonness;
            }
            return Commonness;
        }

        private CaveGenerationParams(XElement element)
        {
            Identifier = element == null ? "default" : element.GetAttributeString("identifier", null) ?? element.Name.ToString();
            Identifier = Identifier.ToLowerInvariant();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            foreach (XElement subElement in element.Elements())
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
                        string levelType = subElement.GetAttributeString("leveltype", "").ToLowerInvariant();
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, subElement.GetAttributeFloat("commonness", 1.0f));
                        }
                        break;
                }
            }
        }

        public static void LoadPresets()
        {
            CaveParams = new List<CaveGenerationParams>();

            var files = GameMain.Instance.GetFilesOfType(ContentType.CaveGenerationParameters);
            if (!files.Any())
            {
                files = new List<ContentFile>() { new ContentFile("Content/Map/CaveGenerationParameters.xml", ContentType.CaveGenerationParameters) };
            }

            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    CaveParams.Clear();
                    DebugConsole.NewMessage($"Overriding cave generation parameters with '{file.Path}'", Color.Yellow);
                }

                foreach (XElement element in mainElement.Elements())
                {
                    bool isOverride = element.IsOverride();
                    if (isOverride)
                    {
                        string identifier = element.FirstElement().GetAttributeString("identifier", "");
                        var existingParams = CaveParams.Find(p => p.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                        if (existingParams != null)
                        {
                            DebugConsole.NewMessage($"Overriding the cave generation parameters '{identifier}' using the file '{file.Path}'", Color.Yellow);
                            CaveParams.Remove(existingParams);
                        }
                        CaveParams.Add(new CaveGenerationParams(element.FirstElement()));
                        
                    }
                    else
                    {
                        string identifier = element.FirstElement().GetAttributeString("identifier", "");
                        var existingParams = CaveParams.Find(p => p.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                        if (existingParams != null)
                        {
                            DebugConsole.ThrowError($"Duplicate cave generation parameters: '{identifier}' defined in {element.Name} of '{file.Path}'. Use <override></override> tags to override the generation parameters.");
                            continue;
                        }
                        else
                        {
                            CaveParams.Add(new CaveGenerationParams(element));
                        }
                    }
                }
            }
        }

        public void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element, true);
            foreach (KeyValuePair<string, float> overrideCommonness in OverrideCommonness)
            {
                bool elementFound = false;
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().Equals("overridecommonness", StringComparison.OrdinalIgnoreCase)
                        && subElement.GetAttributeString("leveltype", "").Equals(overrideCommonness.Key, StringComparison.OrdinalIgnoreCase))
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
