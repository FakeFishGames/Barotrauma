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
            Name = ToolBox.GetAttributeString(element, "name", "Biome");
            Description = ToolBox.GetAttributeString(element, "description", "");
            
            string[] placementsStrs = ToolBox.GetAttributeString(element, "MapPlacement", "Default").Split(',');
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

    class LevelGenerationParams : IPropertyObject
    {
        private static List<LevelGenerationParams> levelParams;
        private static List<Biome> biomes;


        public string Name
        {
            get;
            private set;
        }

        private float width, height;

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

        private int mountainCountMin, mountainCountMax;
        
        private float mountainHeightMin, mountainHeightMax;

        private int ruinCount;

        //which biomes can this type of level appear in
        private List<Biome> allowedBiomes = new List<Biome>();

        public Color BackgroundColor
        {
            get;
            set;
        }

        [HasDefaultValue(1000, false)]
        public int BackgroundSpriteAmount
        {
            get;
            set;
        }

        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get;
            set;
        }

        [HasDefaultValue(100000.0f, false)]
        public float Width
        {
            get { return width; }
            set { width = Math.Max(value, 2000.0f); }
        }

        [HasDefaultValue(50000.0f, false)]
        public float Height
        {
            get { return height; }
            set { height = Math.Max(value, 2000.0f); }
        }

        public Vector2 VoronoiSiteInterval
        {
            get { return voronoiSiteInterval; }
            set
            {
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100.0f, width / 2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100.0f, height / 2);
            }
        }

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

        public Vector2 MainPathNodeIntervalRange
        {
            get { return mainPathNodeIntervalRange; }
            set
            {
                mainPathNodeIntervalRange.X = MathHelper.Clamp(value.X, 100.0f, width / 2);
                mainPathNodeIntervalRange.Y = MathHelper.Clamp(value.Y, mainPathNodeIntervalRange.X, width / 2);
            }
        }

        [HasDefaultValue(5, false)]
        public int SmallTunnelCount
        {
            get { return smallTunnelCount; }
            set { smallTunnelCount = MathHelper.Clamp(value, 0, 100); }
        }

        public Vector2 SmallTunnelLengthRange
        {
            get { return smallTunnelLengthRange; }
            set
            {
                smallTunnelLengthRange.X = MathHelper.Clamp(value.X, 100.0f, width);
                smallTunnelLengthRange.Y = MathHelper.Clamp(value.Y, smallTunnelLengthRange.X, width);
            }
        }

        [HasDefaultValue(-300000.0f, false)]
        public float SeaFloorDepth
        {
            get { return seaFloorBaseDepth; }
            set { seaFloorBaseDepth = MathHelper.Clamp(value, Level.MaxEntityDepth, 0.0f); }
        }

        [HasDefaultValue(1000.0f, false)]
        public float SeaFloorVariance
        {
            get { return seaFloorVariance; }
            set { seaFloorVariance = value; }
        }

        [HasDefaultValue(0, false)]
        public int MountainCountMin
        {
            get { return mountainCountMin; }
            set
            {
                mountainCountMin = Math.Max(value, 0);
            }
        }

        [HasDefaultValue(0, false)]
        public int MountainCountMax
        {
            get { return mountainCountMax; }
            set
            {
                mountainCountMax = Math.Max(value, 0);
            }
        }

        [HasDefaultValue(1000.0f, false)]
        public float MountainHeightMin
        {
            get { return mountainHeightMin; }
            set
            {
                mountainHeightMin = Math.Max(value, 0);
            }
        }

        [HasDefaultValue(5000.0f, false)]
        public float MountainHeightMax
        {
            get { return mountainHeightMax; }
            set
            {
                mountainHeightMax = Math.Max(value, 0);
            }
        }

        [HasDefaultValue(1, false)]
        public int RuinCount
        {
            get { return ruinCount; }
            set { ruinCount = MathHelper.Clamp(value, 0, 10); }
        }

        [HasDefaultValue(0.4f, false)]
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
            ObjectProperties = ObjectProperty.InitProperties(this, element);

            Vector3 colorVector = ToolBox.GetAttributeVector3(element, "BackgroundColor", new Vector3(50, 46, 20));
            BackgroundColor = new Color((int)colorVector.X, (int)colorVector.Y, (int)colorVector.Z);

            VoronoiSiteInterval = ToolBox.GetAttributeVector2(element, "VoronoiSiteInterval", new Vector2(3000, 3000));

            VoronoiSiteVariance = ToolBox.GetAttributeVector2(element, "VoronoiSiteVariance", new Vector2(voronoiSiteInterval.X, voronoiSiteInterval.Y) * 0.4f);

            MainPathNodeIntervalRange = ToolBox.GetAttributeVector2(element, "MainPathNodeIntervalRange", new Vector2(5000.0f, 10000.0f));

            SmallTunnelLengthRange = ToolBox.GetAttributeVector2(element, "SmallTunnelLengthRange", new Vector2(5000.0f, 10000.0f));
            
            string biomeStr = ToolBox.GetAttributeString(element, "biomes", "");

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
                XDocument doc = ToolBox.TryLoadXml(file);
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
