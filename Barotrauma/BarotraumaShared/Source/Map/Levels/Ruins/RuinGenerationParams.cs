using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.RuinGeneration
{
    [Flags]
    enum RuinStructureType
    {
        Wall = 1, CorridorWall = 2, Prop = 4, Back = 8, Door = 16, Hatch = 32, HeavyWall = 64
    }

    class RuinGenerationParams : ISerializableEntity
    {
        public static List<RuinGenerationParams> List
        {
            get
            {
                if (paramsList == null)
                {
                    LoadAll();
                }
                return paramsList;
            }
        }

        private static List<RuinGenerationParams> paramsList;

        private string filePath;

        private List<RuinStructure> structureList;
        
        public string Name => "RuinGenerationParams";

        [Serialize(3, false), Editable(MinValueInt = 1, MaxValueInt = 10, ToolTip = "The ruin generation algorithm \"splits\" the ruin area into two, splits these areas again, repeats this for some number of times and creates a room at each of the final split areas. This is value determines the minimum number of times the split is done.")]
        public int RoomDivisionIterationsMin
        {
            get;
            set;
        }

        [Serialize(4, false), Editable(MinValueInt = 1, MaxValueInt = 10, ToolTip = "The ruin generation algorithm \"splits\" the ruin area into two, splits these areas again, repeats this for some number of times and creates a room at each of the final split areas. This is value determines the maximum number of times the split is done.")]
        public int RoomDivisionIterationsMax
        {
            get;
            set;
        }

        [Serialize(0.5f, false), Editable(MinValueFloat = 0.1f, MaxValueFloat = 0.9f, ToolTip = "The probability for the split algorithm to split the area vertically. High values tend to create tall, vertical rooms, and low values wide, horizontal rooms.")]
        public float VerticalSplitProbability
        {
            get;
            set;
        }

        [Serialize(400, false), Editable(ToolTip = "The splitting algorithm attempts to keep the dimensions the split areas larger than this. For example, if the width of the split areas would be smaller than this after a vertical split, the algorithm will do a horizontal split.")]
        public int MinSplitWidth
        {
            get;
            set;
        }

        [Serialize("0.5,0.9", false), Editable(ToolTip = "The minimum and maximum width of a room relative to the areas created by the split algorithm.")]
        public Vector2 RoomWidthRange
        {
            get;
            set;
        }
        [Serialize("0.5,0.9", false), Editable(ToolTip = "The minimum and maximum height of a room relative to the areas created by the split algorithm.")]
        public Vector2 RoomHeightRange
        {
            get;
            set;
        }

        [Serialize("200,256", false), Editable(ToolTip = "The minimum and maximum width of the corridors between rooms.")]
        public Point CorridorWidthRange
        {
            get;
            set;
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        } = new Dictionary<string, SerializableProperty>();

        private RuinGenerationParams(XElement element)
        {
            structureList = new List<RuinStructure>();

            if (element != null)
            {
                foreach (XElement subElement in element.Elements())
                {
                    structureList.Add(new RuinStructure(subElement));
                }
            }
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public static RuinGenerationParams GetRandom()
        {
            if (paramsList == null) { LoadAll(); }

            if (paramsList.Count == 0)
            {
                DebugConsole.ThrowError("No ruin configuration files found in any content package.");
                return new RuinGenerationParams(null);
            }

            return paramsList[Rand.Int(paramsList.Count, Rand.RandSync.Server)];
        }

        public RuinStructure GetRandomStructure(RuinStructureType type, Alignment alignment, RuinStructure.RoomType roomType = RuinStructure.RoomType.Any)
        {
            var matchingStructures = structureList.FindAll(rs => 
                rs.Type.HasFlag(type) && 
                rs.Alignment.HasFlag(alignment) && 
                (roomType == RuinStructure.RoomType.Any || rs.RoomPlacement == RuinStructure.RoomType.Any || rs.RoomPlacement.HasFlag(roomType)));

            if (!matchingStructures.Any()) return null;

            return ToolBox.SelectWeightedRandom(
                matchingStructures,
                matchingStructures.Select(s => s.Commonness).ToList(),
                Rand.RandSync.Server);
        }

        private static void LoadAll()
        {
            paramsList = new List<RuinGenerationParams>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc?.Root == null) continue;
                var newParams = new RuinGenerationParams(doc.Root)
                {
                    filePath = configFile
                };
                paramsList.Add(newParams);
            }
        }

        public static void SaveAll()
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            foreach (RuinGenerationParams generationParams in List)
            {
                foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
                {
                    if (configFile != generationParams.filePath) continue;

                    XDocument doc = XMLExtensions.TryLoadXml(configFile);
                    if (doc?.Root == null) continue;

                    SerializableProperty.SerializeProperties(generationParams, doc.Root);

                    using (var writer = XmlWriter.Create(configFile, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                }
            }
        }
    }

    class RuinStructure
    {
        public readonly MapEntityPrefab Prefab;
        public readonly Alignment Alignment;
        public readonly RuinStructureType Type;
        public readonly float Commonness;
        public readonly RoomType RoomPlacement;

        private readonly List<RuinStructure> childStructures = new List<RuinStructure>();

        public IEnumerable<RuinStructure> ChildStructures
        {
            get { return childStructures; }
        }

        public enum RoomType
        {
            Any = 0,
            SameRoom = 1,
            PreviousRoom = 2,
            NextRoom = 4,
            FirstRoom = 8,
            LastRoom = 16
        }

        public RuinStructure(XElement element)
        {
            string name = element.GetAttributeString("prefab", "");
            Prefab = MapEntityPrefab.Find(name);

            if (Prefab == null)
            {
                DebugConsole.ThrowError("Loading ruin structure failed - structure prefab \"" + name + " not found.");
                return;
            }

            string alignmentStr = element.GetAttributeString("alignment", "Bottom");
            if (!Enum.TryParse(alignmentStr, true, out Alignment))
            {
                DebugConsole.ThrowError("Error in ruin structure \"" + name + "\" - " + alignmentStr + " is not a valid alignment.");
            }
            
            string typeStr = element.GetAttributeString("type", "");
            if (!Enum.TryParse(typeStr, true, out Type))
            {
                DebugConsole.ThrowError("Error in ruin structure \"" + name + "\" - " + typeStr + " is not a valid type.");
                return;
            }

            string placementStr = element.GetAttributeString("placement", "Any");
            if (!Enum.TryParse(placementStr, true, out RoomPlacement))
            {
                DebugConsole.ThrowError("Error in ruin structure \"" + name + "\" - " + placementStr + " is not a valid room placement type.");
                return;
            }

            foreach (XElement subElement in element.Elements())
            {
                childStructures.Add(new RuinStructure(subElement));
            }

            Commonness = element.GetAttributeFloat("commonness", 1);
        }        
    }
}
