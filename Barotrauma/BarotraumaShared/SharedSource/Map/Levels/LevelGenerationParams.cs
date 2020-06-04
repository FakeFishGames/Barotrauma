using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Biome
    {
        public readonly string Identifier;
        public readonly string DisplayName;
        public readonly string Description;

        public readonly List<int> AllowedZones = new List<int>();

        public Biome(string name, string description)
        {
            Identifier = name;
            Description = description;
        }

        public Biome(XElement element)
        {
            Identifier = element.GetAttributeString("identifier", "");
            if (string.IsNullOrEmpty(Identifier))
            {
                Identifier = element.GetAttributeString("name", "");
                DebugConsole.ThrowError("Error in biome \"" + Identifier + "\": identifier missing, using name as the identifier.");
            }

            DisplayName =
                TextManager.Get("biomename." + Identifier, returnNull: true) ??
                element.GetAttributeString("name", "Biome") ??
                TextManager.Get("biomename." + Identifier);

            Description =
                TextManager.Get("biomedescription." + Identifier, returnNull: true) ??
                element.GetAttributeString("description", "") ??
                TextManager.Get("biomedescription." + Identifier);

            string allowedZonesStr = element.GetAttributeString("AllowedZones", "1,2,3,4,5,6,7,8,9");
            string[] zoneIndices = allowedZonesStr.Split(',');
            for (int i = 0; i < zoneIndices.Length; i++)
            {
                int zoneIndex = -1;
                if (!int.TryParse(zoneIndices[i].Trim(), out zoneIndex))
                {
                    DebugConsole.ThrowError("Error in biome config \"" + Identifier + "\" - \"" + zoneIndices[i] + "\" is not a valid zone index.");
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

        private int minWidth, maxWidth, height;

        private Point voronoiSiteInterval;
        //how much the sites are "scattered" on x- and y-axis
        //if Vector2.Zero, the sites will just be placed in a regular grid pattern
        private Point voronoiSiteVariance;

        //how far apart the nodes of the main path can be
        //x = min interval, y = max interval
        private Point mainPathNodeIntervalRange;

        private int smallTunnelCount;
        //x = min length, y = max length
        private Point smallTunnelLengthRange;

        //how large portion of the bottom of the level should be "carved out"
        //if 0.0f, the bottom will be completely solid (making the abyss unreachable)
        //if 1.0f, the bottom will be completely open
        private float bottomHoleProbability;

        //the y-position of the ocean floor (= the position from which the bottom formations extend upwards)
        private int seaFloorBaseDepth;
        //how much random variance there can be in the height of the formations
        private int seaFloorVariance;

        private int cellSubdivisionLength;
        private float cellRoundingAmount;
        private float cellIrregularity;

        private int mountainCountMin, mountainCountMax;

        private int mountainHeightMin, mountainHeightMax;

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

        [Serialize(1000, true, description: "The total number of level objects (vegetation, vents, etc) in the level."), Editable(MinValueInt = 0, MaxValueInt = 100000)]
        public int LevelObjectAmount
        {
            get;
            set;
        }

        [Serialize(100000, true), Editable(MinValueInt = 10000, MaxValueInt = 1000000)]
        public int MinWidth
        {
            get { return minWidth; }
            set { minWidth = Math.Max(value, 2000); }
        }

        [Serialize(100000, true), Editable(MinValueInt = 10000, MaxValueInt = 1000000)]
        public int MaxWidth
        {
            get { return maxWidth; }
            set { maxWidth = Math.Max(value, 2000); }
        }

        [Serialize(50000, true), Editable(MinValueInt = 10000, MaxValueInt = 1000000)]
        public int Height
        {
            get { return height; }
            set { height = Math.Max(value, 2000); }
        }

        [Editable, Serialize("3000, 3000", true, description: "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph which the level walls are generated from. " +
            "(Decreasing this value causes the number of sites, and the complexity of the level, to increase exponentially - be careful when adjusting)")]
        public Point VoronoiSiteInterval
        {
            get { return voronoiSiteInterval; }
            set
            {
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100, MinWidth / 2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100, height / 2);
            }
        }

        [Editable, Serialize("700,700", true, description: "How much random variation to apply to the positions of the voronoi sites on each axis. " +
            "Small values produce roughly rectangular level walls. The larger the values are, the less uniform the shapes get.")]
        public Point VoronoiSiteVariance
        {
            get { return voronoiSiteVariance; }
            set
            {
                voronoiSiteVariance = new Point(
                    MathHelper.Clamp(value.X, 0, voronoiSiteInterval.X),
                    MathHelper.Clamp(value.Y, 0, voronoiSiteInterval.Y));
            }
        }

        [Editable(MinValueInt = 100, MaxValueInt = 10000), Serialize(1000, true, description: "The edges of the individual wall cells are subdivided into edges of this size. "
            + "Can be used in conjunction with the rounding values to make the cells rounder. Smaller values will make the cells look smoother, " +
            "but make the level more performance-intensive as the number of polygons used in rendering and physics calculations increases.")]
        public int CellSubdivisionLength
        {
            get { return cellSubdivisionLength; }
            set
            {
                cellSubdivisionLength = Math.Max(value, 10);
            }
        }


        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f), Serialize(0.5f, true, description: "How much the individual wall cells are rounded. "
            + "Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellRoundingAmount
        {
            get { return cellRoundingAmount; }
            set
            {
                cellRoundingAmount = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f), Serialize(0.1f, true, description: "How much random variance is applied to the edges of the cells. "
            + "Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellIrregularity
        {
            get { return cellIrregularity; }
            set
            {
                cellIrregularity = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }


        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }), 
            Serialize("5000, 10000", true, description: "The distance between the nodes that are used to generate the main path through the level (min, max). Larger values produce a straighter path.")]
        public Point MainPathNodeIntervalRange
        {
            get { return mainPathNodeIntervalRange; }
            set
            {
                mainPathNodeIntervalRange.X = MathHelper.Clamp(value.X, 100, MinWidth / 2);
                mainPathNodeIntervalRange.Y = MathHelper.Clamp(value.Y, mainPathNodeIntervalRange.X, MinWidth / 2);
            }
        }

        [Editable, Serialize(5, true, description: "The number of small tunnels placed along the main path.")]
        public int SmallTunnelCount
        {
            get { return smallTunnelCount; }
            set { smallTunnelCount = MathHelper.Clamp(value, 0, 100); }
        }

        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }), 
            Serialize("5000, 10000", true, description: "The minimum and maximum length of small tunnels placed along the main path.")]
        public Point SmallTunnelLengthRange
        {
            get { return smallTunnelLengthRange; }
            set
            {
                smallTunnelLengthRange.X = MathHelper.Clamp(value.X, 100, MinWidth);
                smallTunnelLengthRange.Y = MathHelper.Clamp(value.Y, smallTunnelLengthRange.X, MinWidth);
            }
        }

        [Serialize(100, true), Editable(MinValueInt = 0, MaxValueInt = 10000)]
        public int ItemCount
        {
            get;
            set;
        }

        [Serialize(0, true), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int FloatingIceChunkCount
        {
            get;
            set;
        }

        [Serialize(300000, true, description: "How far below the level the sea floor is placed."), Editable(MinValueFloat = Level.MaxEntityDepth, MaxValueFloat = 0.0f)]
        public int SeaFloorDepth
        {
            get { return seaFloorBaseDepth; }
            set { seaFloorBaseDepth = MathHelper.Clamp(value, Level.MaxEntityDepth, 0); }
        }

        [Serialize(1000, true, description: "Variance of the depth of the sea floor. Smaller values produce a smoother sea floor."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100000.0f)]
        public int SeaFloorVariance
        {
            get { return seaFloorVariance; }
            set { seaFloorVariance = value; }
        }

        [Serialize(0, true, description: "The minimum number of mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MountainCountMin
        {
            get { return mountainCountMin; }
            set
            {
                mountainCountMin = Math.Max(value, 0);
            }
        }

        [Serialize(0, true, description: "The maximum number of mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MountainCountMax
        {
            get { return mountainCountMax; }
            set
            {
                mountainCountMax = Math.Max(value, 0);
            }
        }

        [Serialize(1000, true, description: "The minimum height of the mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int MountainHeightMin
        {
            get { return mountainHeightMin; }
            set
            {
                mountainHeightMin = Math.Max(value, 0);
            }
        }

        [Serialize(5000, true, description: "The maximum height of the mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int MountainHeightMax
        {
            get { return mountainHeightMax; }
            set
            {
                mountainHeightMax = Math.Max(value, 0);
            }
        }

        [Serialize(1, true, description: "The number of alien ruins in the level."), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int RuinCount { get; set; }

        [Serialize(1, true, description: "The maximum number of wrecks in the level. Note that this value cannot be higher than the amount of wreck prefabs (subs)."), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int WreckCount { get; set; }

        // TODO: Move the wreck parameters under a separate class?
#region Wreck parameters
        [Serialize(1, true, description: "The minimum number of corpses per wreck."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MinCorpseCount { get; set; }

        [Serialize(5, true, description: "The maximum number of corpses per wreck."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MaxCorpseCount { get; set; }

        [Serialize(0.0f, true, description: "How likely is it that a Thalamus inhabits a wreck. Percentage from 0 to 1 per wreck."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float ThalamusProbability { get; set; }

        [Serialize(0.5f, true, description: "How likely the water level of a hull inside a wreck is randomly set."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckHullFloodingChance { get; set; }

        [Serialize(0.1f, true, description: "The min water percentage of randomly flooding hulls in wrecks."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckFloodingHullMinWaterPercentage { get; set; }

        [Serialize(1.0f, true, description: "The min water percentage of randomly flooding hulls in wrecks."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckFloodingHullMaxWaterPercentage { get; set; }
#endregion

        [Serialize(0.4f, true, description: "The probability for wall cells to be removed from the bottom of the map. A value of 0 will produce a completely enclosed tunnel and 1 will make the entire bottom of the level completely open."), Editable()]
        public float BottomHoleProbability
        {
            get { return bottomHoleProbability; }
            set { bottomHoleProbability = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        [Serialize(1.0f, true, description: "Scale of the water particle texture."), Editable]
        public float WaterParticleScale
        {
            get { return waterParticleScale; }
            private set { waterParticleScale = Math.Max(value, 0.01f); }
        }

        public Sprite BackgroundSprite { get; private set; }
        public Sprite BackgroundTopSprite { get; private set; }
        public Sprite WallSprite { get; private set; }
        public Sprite WallSpriteSpecular { get; private set; }
        public Sprite WallEdgeSprite { get; private set; }
        public Sprite WallEdgeSpriteSpecular { get; private set; }
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
                return levelParams.GetRandom(lp => lp.allowedBiomes.Count > 0, Rand.RandSync.Server);
            }

            var matchingLevelParams = levelParams.FindAll(lp => lp.allowedBiomes.Contains(biome));
            if (matchingLevelParams.Count == 0)
            {
                DebugConsole.ThrowError("Level generation presets not found for the biome \"" + biome.Identifier + "\"!");
                return new LevelGenerationParams(null);
            }

            return matchingLevelParams[Rand.Range(0, matchingLevelParams.Count, Rand.RandSync.Server)];
        }

        private LevelGenerationParams(XElement element)
        {
            Name = element == null ? "default" : element.Name.ToString();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

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
                    if (biomeName == "none") { continue; }

                    Biome matchingBiome = biomes.Find(b => b.Identifier.Equals(biomeName, StringComparison.OrdinalIgnoreCase));
                    if (matchingBiome == null)
                    {
                        matchingBiome = biomes.Find(b => b.DisplayName.Equals(biomeName, StringComparison.OrdinalIgnoreCase));
                        if (matchingBiome == null)
                        {
                            DebugConsole.ThrowError("Error in level generation parameters: biome \"" + biomeName + "\" not found.");
                            continue;
                        }
                        else
                        {
                            DebugConsole.NewMessage("Please use biome identifiers instead of names in level generation parameter \"" + Name + "\".", Color.Orange);
                        }
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
                    case "wallspecular":
                        WallSpriteSpecular = new Sprite(subElement);
                        break;
                    case "walledge":
                        WallEdgeSprite = new Sprite(subElement);
                        break;
                    case "walledgespecular":
                        WallEdgeSpriteSpecular = new Sprite(subElement);
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
                files = new List<ContentFile>() { new ContentFile("Content/Map/LevelGenerationParameters.xml", ContentType.LevelGenerationParameters) };
            }

            List<XElement> biomeElements = new List<XElement>();
            List<XElement> levelParamElements = new List<XElement>();

            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    biomeElements.Clear();
                    levelParamElements.Clear();
                    DebugConsole.NewMessage($"Overriding the level generation parameters and biomes with '{file.Path}'", Color.Yellow);
                }
                else if (biomeElements.Any() || levelParamElements.Any())
                {
                    DebugConsole.ThrowError($"Error in '{file.Path}': Another level generation parameter file already loaded! Use <override></override> tags to override it.");
                    break;
                }

                foreach (XElement element in mainElement.Elements())
                {
                    if (element.IsOverride())
                    {
                        if (element.FirstElement().Name.ToString().Equals("biomes", StringComparison.OrdinalIgnoreCase))
                        {
                            biomeElements.Clear();
                            biomeElements.AddRange(element.FirstElement().Elements());
                            DebugConsole.NewMessage($"Overriding biomes with '{file.Path}'", Color.Yellow);
                        }
                        else
                        {
                            levelParamElements.Clear();
                            DebugConsole.NewMessage($"Overriding the level generation parameters with '{file.Path}'", Color.Yellow);
                            levelParamElements.AddRange(element.Elements());
                        }
                    }                    
                    else if (element.Name.ToString().Equals("biomes", StringComparison.OrdinalIgnoreCase))
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
