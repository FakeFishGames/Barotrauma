using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    class LevelGenerationParams : IPropertyObject
    {
        private static List<LevelGenerationParams> presets;

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

        private int ruinCount;

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
            set { 
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100.0f, width/2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100.0f, height/2); 
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
        
        public static LevelGenerationParams GetRandom(string seed)
        {
            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            if (presets == null || !presets.Any())
            {
                DebugConsole.ThrowError("Level generation presets not found - using default presets");
                return new LevelGenerationParams(null);
            }

            return presets[Rand.Range(0, presets.Count, Rand.RandSync.Server)];
        }

        private LevelGenerationParams(XElement element)
        {
            Name = element==null ? "default" : element.Name.ToString();
            ObjectProperties = ObjectProperty.InitProperties(this, element);

            Vector3 colorVector = ToolBox.GetAttributeVector3(element, "BackgroundColor", new Vector3(50, 46, 20));
            BackgroundColor = new Color((int)colorVector.X, (int)colorVector.Y, (int)colorVector.Z);

            VoronoiSiteInterval = ToolBox.GetAttributeVector2(element, "VoronoiSiteInterval", new Vector2(3000, 3000));

            VoronoiSiteVariance = ToolBox.GetAttributeVector2(element, "VoronoiSiteVariance", new Vector2(voronoiSiteInterval.X, voronoiSiteInterval.Y) * 0.4f);

            MainPathNodeIntervalRange = ToolBox.GetAttributeVector2(element, "MainPathNodeIntervalRange", new Vector2(5000.0f, 10000.0f));

            SmallTunnelLengthRange = ToolBox.GetAttributeVector2(element, "SmallTunnelLengthRange", new Vector2(5000.0f, 10000.0f));
        }

        public static void LoadPresets()
        {
            presets = new List<LevelGenerationParams>();

            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.LevelGenerationParameters);
            if (!files.Any())
            {
                files.Add("Content/Map/LevelGenerationParameters.xml");
            }
                        
            foreach (string file in files)
            {
                XDocument doc = ToolBox.TryLoadXml(file);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    presets.Add(new LevelGenerationParams(element));
                }
            }
        }
    }
}
