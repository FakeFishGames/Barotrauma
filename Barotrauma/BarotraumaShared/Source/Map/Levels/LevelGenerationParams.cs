using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Biome
    {
        public readonly string Name;
        public readonly string Description;

        public readonly List<int> AllowedZones = new List<int>();
        
        public Biome(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Biome(XElement element)
        {
            Name = element.GetAttributeString("name", "Biome");
            Description = element.GetAttributeString("description", "");

            string allowedZonesStr = element.GetAttributeString("AllowedZones", "1,2,3,4,5,6,7,8,9");
            string[] zoneIndices = allowedZonesStr.Split(',');
            for (int i = 0; i < zoneIndices.Length; i++)
            {
                int zoneIndex = -1;
                if (!int.TryParse(zoneIndices[i].Trim(), out zoneIndex))
                {
                    DebugConsole.ThrowError("Error in biome config \"" + Name + "\" - \"" + zoneIndices[i] + "\" is not a valid zone index.");
                    continue;
                }
                AllowedZones.Add(zoneIndex);
            }
        }
    }

    class LevelGenerationParams : ISerializableEntity
    {
        public static List<LevelGenerationParams> LevelParams
        {
            get { return levelParams; }
        }

        private static List<LevelGenerationParams> levelParams;
        private static List<Biome> biomes;

        public string Name
        {
            get;
            private set;
        }

        private float minWidth, maxWidth, height;

        private Vector2 voronoiSiteInterval;
        //how much the sites are "scattered" on x- and y-axis
        //if Vector2.Zero, the sites will just be placed in a regular grid pattern
        private Vector2 voronoiSiteVariance;

        //how far apart the nodes of the main path can be
        //x = min interval, y = max interval
        private Vector2 mainPathNodeIntervalRange;

        private int smallTunnelCount;
        //x = min length, y = max length
        private Vector2 smallTunnelLengthRange;

        //how large portion of the bottom of the level should be "carved out"
        //if 0.0f, the bottom will be completely solid (making the abyss unreachable)
        //if 1.0f, the bottom will be completely open
        private float bottomHoleProbability;

        //the y-position of the ocean floor (= the position from which the bottom formations extend upwards)
        private float seaFloorBaseDepth;
        //how much random variance there can be in the height of the formations
        private float seaFloorVariance;

        private float cellSubdivisionLength;
        private float cellRoundingAmount;
        private float cellIrregularity;

        private int mountainCountMin, mountainCountMax;
        
        private float mountainHeightMin, mountainHeightMax;

        private int ruinCount;

        private float waterParticleScale;

        //which biomes can this type of level appear in
        private List<Biome> allowedBiomes = new List<Biome>();

        public IEnumerable<Biome> AllowedBiomes
        {
            get { return allowedBiomes; }
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        [Serialize("27,30,36", true), Editable]
        public Color AmbientLightColor
        {
            get;
            set;
        }

        [Serialize("20,40,50", true), Editable()]
        public Color BackgroundTextureColor
        {
            get;
            set;
        }

        [Serialize("20,40,50", true), Editable]
        public Color BackgroundColor
        {
            get;
            set;
        }

        [Serialize("255,255,255", true), Editable]
        public Color WallColor
        {
            get;
            set;
        }

        [Serialize(1000, true), Editable(MinValueInt = 0, MaxValueInt = 100000, ToolTip = "The total number of level objects (vegetation, vents, etc) in the level.")]
        public int LevelObjectAmount
        {
            get;
            set;
        }

        [Serialize(100000.0f, true), Editable(MinValueFloat = 10000, MaxValueFloat = 1000000)]
        public float MinWidth
        {
            get { return minWidth; }
            set { minWidth = Math.Max(value, 2000.0f); }
        }

        [Serialize(100000.0f, true), Editable(MinValueFloat = 10000, MaxValueFloat = 1000000)]
        public float MaxWidth
        {
            get { return maxWidth; }
            set { maxWidth = Math.Max(value, 2000.0f); }
        }

        [Serialize(50000.0f, true), Editable(MinValueFloat = 10000, MaxValueFloat = 1000000)]
        public float Height
        {
            get { return height; }
            set { height = Math.Max(value, 2000.0f); }
        }

        [Serialize("3000.0, 3000.0", true), Editable(
            ToolTip = "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph which the level walls are generated from. " +
            "(Decreasing this value causes the number of sites, and the complexity of the level, to increase exponentially - be careful when adjusting)")]
        public Vector2 VoronoiSiteInterval
        {
            get { return voronoiSiteInterval; }
            set
            {
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100.0f, MinWidth / 2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100.0f, height / 2);
            }
        }

        [Serialize("700,700", true), Editable(ToolTip = "How much random variation to apply to the positions of the voronoi sites on each axis. "+
            "Small values produce roughly rectangular level walls. The larger the values are, the less uniform the shapes get.")]
        public Vector2 VoronoiSiteVariance
        {
            get { return voronoiSiteVariance; }
            set
            {
                voronoiSiteVariance = new Vector2(
                    MathHelper.Clamp(value.X, 0, voronoiSiteInterval.X),
                    MathHelper.Clamp(value.Y, 0, voronoiSiteInterval.Y));
            }
        }

        [Serialize(1000.0f, true), Editable(MinValueFloat = 100.0f, MaxValueFloat = 10000.0f, ToolTip = "The edges of the individual wall cells are subdivided into edges of this size. "
            + "Can be used in conjunction with the rounding values to make the cells rounder. Smaller values will make the cells look smoother, " +
            "but make the level more performance-intensive as the number of polygons used in rendering and physics calculations increases.")]
        public float CellSubdivisionLength
        {
            get { return cellSubdivisionLength; }
            set
            {
                cellSubdivisionLength = Math.Max(value, 10.0f);
            }
        }


        [Serialize(0.5f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ToolTip = "How much the individual wall cells are rounded. "
            +"Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellRoundingAmount
        {
            get { return cellRoundingAmount; }
            set
            {
                cellRoundingAmount = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Serialize(0.1f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ToolTip = "How much random variance is applied to the edges of the cells. "
            + "Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellIrregularity
        {
            get { return cellIrregularity; }
            set
            {
                cellIrregularity = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }


        [Serialize("5000.0, 10000.0", true), Editable(ToolTip = "The distance between the nodes that are used to generate the main path through the level (min, max). Larger values produce a straighter path.")]
        public Vector2 MainPathNodeIntervalRange
        {
            get { return mainPathNodeIntervalRange; }
            set
            {
                mainPathNodeIntervalRange.X = MathHelper.Clamp(value.X, 100.0f, MinWidth / 2);
                mainPathNodeIntervalRange.Y = MathHelper.Clamp(value.Y, mainPathNodeIntervalRange.X, MinWidth / 2);
            }
        }

        [Serialize(5, true), Editable(ToolTip = "The number of small tunnels placed along the main path.")]
        public int SmallTunnelCount
        {
            get { return smallTunnelCount; }
            set { smallTunnelCount = MathHelper.Clamp(value, 0, 100); }
        }

        [Serialize("5000.0, 10000.0", true), Editable(ToolTip = "The minimum and maximum length of small tunnels placed along the main path.")]
        public Vector2 SmallTunnelLengthRange
        {
            get { return smallTunnelLengthRange; }
            set
            {
                smallTunnelLengthRange.X = MathHelper.Clamp(value.X, 100.0f, MinWidth);
                smallTunnelLengthRange.Y = MathHelper.Clamp(value.Y, smallTunnelLengthRange.X, MinWidth);
            }
        }

        [Serialize(0, true)]
        public int FloatingIceChunkCount
        {
            get;
            set;
        }

        [Serialize(-300000.0f, true), Editable(MinValueFloat = Level.MaxEntityDepth, MaxValueFloat = 0.0f, ToolTip = "How far below the level the sea floor is placed.")]
        public float SeaFloorDepth
        {
            get { return seaFloorBaseDepth; }
            set { seaFloorBaseDepth = MathHelper.Clamp(value, Level.MaxEntityDepth, 0.0f); }
        }

        [Serialize(1000.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100000.0f, ToolTip = "Variance of the depth of the sea floor. Smaller values produce a smoother sea floor.")]
        public float SeaFloorVariance
        {
            get { return seaFloorVariance; }
            set { seaFloorVariance = value; }
        }

        [Serialize(0, true), Editable(MinValueInt = 0, MaxValueInt = 20, ToolTip = "The minimum number of mountains on the sea floor.")]
        public int MountainCountMin
        {
            get { return mountainCountMin; }
            set
            {
                mountainCountMin = Math.Max(value, 0);
            }
        }

        [Serialize(0, true), Editable(MinValueInt = 0, MaxValueInt = 20, ToolTip = "The maximum number of mountains on the sea floor.")]
        public int MountainCountMax
        {
            get { return mountainCountMax; }
            set
            {
                mountainCountMax = Math.Max(value, 0);
            }
        }

        [Serialize(1000.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000000.0f, ToolTip = "The minimum height of the mountains on the sea floor.")]
        public float MountainHeightMin
        {
            get { return mountainHeightMin; }
            set
            {
                mountainHeightMin = Math.Max(value, 0);
            }
        }

        [Serialize(5000.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000000.0f, ToolTip = "The maximum height of the mountains on the sea floor.")]
        public float MountainHeightMax
        {
            get { return mountainHeightMax; }
            set
            {
                mountainHeightMax = Math.Max(value, 0);
            }
        }

        [Serialize(1, true), Editable(MinValueInt = 0, MaxValueInt = 50, ToolTip = "The number of alien ruins in the level.")]
        public int RuinCount
        {
            get { return ruinCount; }
            set { ruinCount = MathHelper.Clamp(value, 0, 10); }
        }

        [Serialize(0.4f, true), Editable(ToolTip = "The probability for wall cells to be removed from the bottom of the map. A value of 0 will produce a completely enclosed tunnel and 1 will make the entire bottom of the level completely open.")]
        public float BottomHoleProbability
        {
            get { return bottomHoleProbability; }
            set { bottomHoleProbability = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        [Serialize(1.0f, true), Editable(ToolTip = "Scale of the water particle texture.")]
        public float WaterParticleScale
        {
            get { return waterParticleScale; }
            private set { waterParticleScale = Math.Max(value, 0.01f); }
        }

        public Sprite BackgroundSprite { get; private set; }
        public Sprite BackgroundTopSprite { get; private set; }
        public Sprite WallSprite { get; private set; }
        public Sprite WallEdgeSprite { get; private set; }
        public Sprite WaterParticles { get; private set; }
        
        public static List<Biome> GetBiomes()
        {
            return biomes;
        }

        public static LevelGenerationParams GetRandom(string seed, Biome biome = null)
        {
            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            if (levelParams == null || !levelParams.Any())
            {
                DebugConsole.ThrowError("Level generation presets not found - using default presets");
                return new LevelGenerationParams(null);
            }

            if (biome == null)
            {
                return levelParams[Rand.Range(0, levelParams.Count, Rand.RandSync.Server)];
            }

            var matchingLevelParams = levelParams.FindAll(lp => lp.allowedBiomes.Contains(biome));
            if (matchingLevelParams.Count == 0)
            {
                DebugConsole.ThrowError("Level generation presets not found for the biome \"" + biome.Name + "\"!");
                return new LevelGenerationParams(null);
            }

            return matchingLevelParams[Rand.Range(0, matchingLevelParams.Count, Rand.RandSync.Server)];
        }

        private LevelGenerationParams(XElement element)
        {
            Name = element == null ? "default" : element.Name.ToString();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            
            VoronoiSiteVariance = element.GetAttributeVector2("VoronoiSiteVariance", new Vector2(voronoiSiteInterval.X, voronoiSiteInterval.Y) * 0.4f);
            
            string biomeStr = element.GetAttributeString("biomes", "");

            if (string.IsNullOrWhiteSpace(biomeStr))
            {
                allowedBiomes = new List<Biome>(biomes);
            }
            else
            {
                string[] biomeNames = biomeStr.Split(',');
                for (int i = 0; i < biomeNames.Length; i++)
                {
                    string biomeName = biomeNames[i].Trim().ToLowerInvariant();
                    Biome matchingBiome = biomes.Find(b => b.Name.ToLowerInvariant() == biomeName);
                    if (matchingBiome == null)
                    {
                        DebugConsole.ThrowError("Error in level generation parameters: biome \"" + biomeName + "\" not found.");
                        continue;
                    }

                    allowedBiomes.Add(matchingBiome);
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "background":
                        BackgroundSprite = new Sprite(subElement);
                        break;
                    case "backgroundtop":
                        BackgroundTopSprite = new Sprite(subElement);
                        break;
                    case "wall":
                        WallSprite = new Sprite(subElement);
                        break;
                    case "walledge":
                        WallEdgeSprite = new Sprite(subElement);
                        break;
                    case "waterparticles":
                        WaterParticles = new Sprite(subElement);
                        break;
                }
            }
        }

        public static void LoadPresets()
        {
            levelParams = new List<LevelGenerationParams>();
            biomes = new List<Biome>();

            var files = GameMain.Instance.GetFilesOfType(ContentType.LevelGenerationParameters);
            if (!files.Any())
            {
                files = new List<string>() { "Content/Map/LevelGenerationParameters.xml" };
            }
            
            List<XElement> biomeElements = new List<XElement>();
            List<XElement> levelParamElements = new List<XElement>();

            foreach (string file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    if (element.Name.ToString().ToLowerInvariant() == "biomes")
                    {
                        biomeElements.AddRange(element.Elements());
                    }
                    else
                    {
                        levelParamElements.Add(element);
                    }
                }
            }

            foreach (XElement biomeElement in biomeElements)
            {
                biomes.Add(new Biome(biomeElement));
            }

            foreach (XElement levelParamElement in levelParamElements)
            {
                levelParams.Add(new LevelGenerationParams(levelParamElement));
            }
        }
    }
}
