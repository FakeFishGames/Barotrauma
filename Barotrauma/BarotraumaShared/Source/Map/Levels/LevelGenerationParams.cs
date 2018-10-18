using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Biome
    {
        public enum MapPlacement
        {
            Random = 1,
            Center = 2,
            Edge = 4
        }

        public readonly string Name;
        public readonly string Description;

        public readonly MapPlacement Placement;
        
        public Biome(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Biome(XElement element)
        {
            Name = element.GetAttributeString("name", "Biome");
            Description = element.GetAttributeString("description", "");
            
            string[] placementsStrs = element.GetAttributeString("MapPlacement", "Default").Split(',');
            foreach (string placementStr in placementsStrs)
            {
                MapPlacement parsedPlacement;            
                if (Enum.TryParse(placementStr.Trim(), out parsedPlacement))
                {
                    Placement |= parsedPlacement;
                }
            }

        }
    }

    class LevelGenerationParams : ISerializableEntity
    {
        private static List<LevelGenerationParams> levelParams;
        private static List<Biome> biomes;


        public string Name
        {
            get;
            private set;
        }

        private int width, height;

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

        private int mountainCountMin, mountainCountMax;
        
        private int mountainHeightMin, mountainHeightMax;

        private int ruinCount;

        //which biomes can this type of level appear in
        private List<Biome> allowedBiomes = new List<Biome>();

        public Color BackgroundColor
        {
            get;
            set;
        }

        public Color WallColor
        {
            get;
            set;
        }

        [Serialize(1000, false)]
        public int BackgroundSpriteAmount
        {
            get;
            set;
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        [Serialize(100000, false)]
        public int Width
        {
            get { return width; }
            set { width = Math.Max(value, 2000); }
        }

        [Serialize(50000, false)]
        public int Height
        {
            get { return height; }
            set { height = Math.Max(value, 2000); }
        }

        public Point VoronoiSiteInterval
        {
            get { return voronoiSiteInterval; }
            set
            {
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100, width / 2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100, height / 2);
            }
        }

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

        public Point MainPathNodeIntervalRange
        {
            get { return mainPathNodeIntervalRange; }
            set
            {
                mainPathNodeIntervalRange.X = MathHelper.Clamp(value.X, 100, width / 2);
                mainPathNodeIntervalRange.Y = MathHelper.Clamp(value.Y, mainPathNodeIntervalRange.X, width / 2);
            }
        }

        [Serialize(5, false)]
        public int SmallTunnelCount
        {
            get { return smallTunnelCount; }
            set { smallTunnelCount = MathHelper.Clamp(value, 0, 100); }
        }

        public Point SmallTunnelLengthRange
        {
            get { return smallTunnelLengthRange; }
            set
            {
                smallTunnelLengthRange.X = MathHelper.Clamp(value.X, 100, width);
                smallTunnelLengthRange.Y = MathHelper.Clamp(value.Y, smallTunnelLengthRange.X, width);
            }
        }

        [Serialize(300000, false)]
        public int SeaFloorDepth
        {
            get { return seaFloorBaseDepth; }
            set { seaFloorBaseDepth = MathHelper.Clamp(value, Level.MaxEntityDepth, 0); }
        }

        [Serialize(1000, false)]
        public int SeaFloorVariance
        {
            get { return seaFloorVariance; }
            set { seaFloorVariance = value; }
        }

        [Serialize(0, false)]
        public int MountainCountMin
        {
            get { return mountainCountMin; }
            set
            {
                mountainCountMin = Math.Max(value, 0);
            }
        }

        [Serialize(0, false)]
        public int MountainCountMax
        {
            get { return mountainCountMax; }
            set
            {
                mountainCountMax = Math.Max(value, 0);
            }
        }

        [Serialize(1000, false)]
        public int MountainHeightMin
        {
            get { return mountainHeightMin; }
            set
            {
                mountainHeightMin = Math.Max(value, 0);
            }
        }

        [Serialize(5000, false)]
        public int MountainHeightMax
        {
            get { return mountainHeightMax; }
            set
            {
                mountainHeightMax = Math.Max(value, 0);
            }
        }

        [Serialize(1, false)]
        public int RuinCount
        {
            get { return ruinCount; }
            set { ruinCount = MathHelper.Clamp(value, 0, 10); }
        }

        [Serialize(0.4f, false)]
        public float BottomHoleProbability
        {
            get { return bottomHoleProbability; }
            set { bottomHoleProbability = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }
        
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

            Vector3 colorVector = element.GetAttributeVector3("BackgroundColor", new Vector3(50, 46, 20));
            BackgroundColor = new Color((int)colorVector.X, (int)colorVector.Y, (int)colorVector.Z);

            colorVector = element.GetAttributeVector3("WallColor", new Vector3(255,255,255));
            WallColor = new Color((int)colorVector.X, (int)colorVector.Y, (int)colorVector.Z);

            VoronoiSiteInterval = element.GetAttributePoint("VoronoiSiteInterval", new Point(3000, 3000));

            VoronoiSiteVariance = element.GetAttributePoint("VoronoiSiteVariance", new Point(voronoiSiteInterval.X / 2, voronoiSiteInterval.Y / 2));

            MainPathNodeIntervalRange = element.GetAttributePoint("MainPathNodeIntervalRange", new Point(5000, 10000));

            SmallTunnelLengthRange = element.GetAttributePoint("SmallTunnelLengthRange", new Point(5000, 10000));
            
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
        }

        public static void LoadPresets()
        {
            levelParams = new List<LevelGenerationParams>();
            biomes = new List<Biome>();

            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.LevelGenerationParameters);
            if (!files.Any())
            {
                files.Add("Content/Map/LevelGenerationParameters.xml");
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
