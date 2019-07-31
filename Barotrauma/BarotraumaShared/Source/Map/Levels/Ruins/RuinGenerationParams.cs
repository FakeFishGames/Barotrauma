using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.RuinGeneration
{
    [Flags]
    enum RuinEntityType
    {
        Wall, Back, Door, Hatch, Prop
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

        private List<RuinRoom> roomTypeList;
                
        public string Name => "RuinGenerationParams";
        
        [Serialize("5000,5000", false), Editable()]
        public Point SizeMin
        {
            get;
            set;
        }
        [Serialize("8000,8000", false), Editable()]
        public Point SizeMax
        {
            get;
            set;
        }

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

        public IEnumerable<RuinRoom> RoomTypeList
        {
            get { return roomTypeList; }
        }

        private RuinGenerationParams(XElement element)
        {
            roomTypeList = new List<RuinRoom>();

            if (element != null)
            {                
                foreach (XElement subElement in element.Elements())
                {
                    roomTypeList.Add(new RuinRoom(subElement));
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

        private static void LoadAll()
        {
            paramsList = new List<RuinGenerationParams>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.RuinConfig))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc?.Root == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.GetFirstChild();
                    paramsList.Clear();
                    DebugConsole.NewMessage("Overriding all ruin configuration parameters.", Color.Yellow);
                }
                DebugConsole.NewMessage($"Adding new ruin configuration parameters from file '{configFile}'");
                var newParams = new RuinGenerationParams(mainElement)
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

    class RuinRoom : ISerializableEntity
    {
        public enum RoomPlacement
        {
            Any,
            First,
            Last
        }

        public string Name
        {
            get;
            private set;
        }

        [Serialize(1.0f, false), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f)]
        public float Commonness { get; private set; }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        } = new Dictionary<string, SerializableProperty>();
        
        [Serialize(RoomPlacement.Any, false), Editable()]
        public RoomPlacement Placement
        {
            get;
            set;
        }

        [Serialize(0, false), Editable()]
        public int PlacementOffset
        {
            get;
            set;
        }

        [Serialize(false, false), Editable()]
        public bool IsCorridor
        {
            get;
            set;
        }
        
        [Serialize(1.0f, false), Editable()]
        public float MinWaterAmount
        {
            get;
            set;
        }
        [Serialize(1.0f, false), Editable()]
        public float MaxWaterAmount
        {
            get;
            set;
        }

        private List<RuinEntityConfig> entityList = new List<RuinEntityConfig>();

        public RuinRoom(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            Name = element.GetAttributeString("name", "");

            if (element != null)
            {
                int groupIndex = 0;
                LoadEntities(element, ref groupIndex);
            }

            void LoadEntities(XElement element2, ref int groupIndex)
            {
                foreach (XElement subElement in element2.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "chooseone")
                    {
                        groupIndex++;
                        LoadEntities(subElement, ref groupIndex);
                    }
                    else
                    {
                        entityList.Add(new RuinEntityConfig(subElement) { SingleGroupIndex = groupIndex });
                    }
                }
            }
        }

        public RuinEntityConfig GetRandomEntity(RuinEntityType type, Alignment alignment)
        {
            var matchingEntities = entityList.FindAll(rs =>
                rs.Type == type &&
                rs.Alignment.HasFlag(alignment));

            if (!matchingEntities.Any()) return null;

            return ToolBox.SelectWeightedRandom(
                matchingEntities,
                matchingEntities.Select(s => s.Commonness).ToList(),
                Rand.RandSync.Server);
        }

        public List<RuinEntityConfig> GetPropList(RuinShape room, Rand.RandSync randSync)
        {
            Dictionary<int, List<RuinEntityConfig>> propGroups = new Dictionary<int, List<RuinEntityConfig>>();
            foreach (RuinEntityConfig entityConfig in entityList)
            {
                if (entityConfig.Type != RuinEntityType.Prop) { continue; }
                if (room.Rect.Width < entityConfig.MinRoomSize.X || room.Rect.Height < entityConfig.MinRoomSize.Y) { continue; }
                if (room.Rect.Width > entityConfig.MaxRoomSize.X || room.Rect.Height > entityConfig.MaxRoomSize.Y) { continue; }
                if (!propGroups.ContainsKey(entityConfig.SingleGroupIndex))
                {
                    propGroups[entityConfig.SingleGroupIndex] = new List<RuinEntityConfig>();
                }
                propGroups[entityConfig.SingleGroupIndex].Add(entityConfig);
            }

            List<RuinEntityConfig> props = new List<RuinEntityConfig>();
            foreach (KeyValuePair<int, List<RuinEntityConfig>> propGroup in propGroups)
            {
                if (propGroup.Key == 0)
                {
                    props.AddRange(propGroup.Value);
                }
                else
                {
                    props.Add(propGroup.Value[Rand.Int(propGroup.Value.Count, randSync)]);
                }
            }
            return props;
        }
    }

    class RuinEntityConfig : ISerializableEntity
    {
        public readonly MapEntityPrefab Prefab;

        public enum RelativePlacement
        {
            SameRoom,
            NextRoom,
            NextCorridor,
            PreviousRoom,
            PreviousCorridor,
            FirstRoom,
            FirstCorridor,
            LastRoom,
            LastCorridor
        }

        public class EntityConnection
        {
            //which type of room to search for the item to connect to
            //sameroom, nextroom, previousroom, firstroom and lastroom are also valid
            public string RoomName
            {
                get;
                private set;
            }

            public string TargetEntityIdentifier
            {
                get;
                private set;
            }

            //Identifier of the item to run the wire from. Only needed in item assemblies to determine which item in the assembly to use.
            public string SourceEntityIdentifier
            {
                get;
                private set;
            }

            //if set, the connection is done by running a wire from 
            //(Pair.First = the name of the connection in this item) to (Pair.Second = the name of the connection in the target item)
            public Pair<string, string> WireConnection
            {
                get;
                private set;
            }

            public EntityConnection(XElement element)
            {
                RoomName = element.GetAttributeString("roomname", "");
                TargetEntityIdentifier = element.GetAttributeString("targetentity", "");
                SourceEntityIdentifier = element.GetAttributeString("sourceentity", "");
                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "wire")
                    {
                        WireConnection = new Pair<string, string>(
                            subElement.GetAttributeString("from", ""),
                            subElement.GetAttributeString("to", ""));
                    }
                }
            }
        }

        [Serialize(Alignment.Bottom, false), Editable]
        public Alignment Alignment { get; private set; }

        [Serialize("0,0", false), Editable(ToolTip = "Minimum offset from the anchor position, relative to the size of the room."+
            " For example, a value of { -0.5,0 } with a Bottom alignment would mean the entity can be placed anywhere between the bottom-left corner of the room and bottom-center.")]
        public Vector2 MinOffset { get; private set; }
        [Serialize("0,0", false), Editable(ToolTip = "Maximum offset from the anchor position, relative to the size of the room." +
            " For example, a value of { 0.5,0 } with a Bottom alignment would mean the entity can be placed anywhere between the bottom-right corner of the room and bottom-center.")]
        public Vector2 MaxOffset { get; private set; }

        [Serialize(RuinEntityType.Prop, false), Editable]
        public RuinEntityType Type { get; private set; }

        [Serialize(false, false), Editable]
        public bool Expand { get; private set; }

        [Serialize(RelativePlacement.SameRoom, false), Editable]
        public RelativePlacement PlacementRelativeToParent { get; private set; }

        [Serialize(1.0f, false), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f)]
        public float Commonness { get; private set; }

        [Serialize(1, false)]
        public int MinAmount { get; private set; }
        [Serialize(1, false)]
        public int MaxAmount { get; private set; }

        [Serialize("0,0", false)]
        public Point MinRoomSize { get; private set; }

        [Serialize("100000,100000", false)]
        public Point MaxRoomSize { get; private set; }

        [Serialize("", false)]
        public string TargetContainer { get; private set; }

        public List<EntityConnection> EntityConnections { get; private set; } = new List<EntityConnection>();


        public int SingleGroupIndex;

        private readonly List<RuinEntityConfig> childEntities = new List<RuinEntityConfig>();        

        public IEnumerable<RuinEntityConfig> ChildEntities
        {
            get { return childEntities; }
        }

        public string Name => Prefab == null ? "null" : Prefab.Name;

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        } = new Dictionary<string, SerializableProperty>();

        public RuinEntityConfig(XElement element)
        {
            string name = element.GetAttributeString("prefab", "");
            Prefab = MapEntityPrefab.Find(name: null, identifier: name);

            if (Prefab == null)
            {
                DebugConsole.ThrowError("Loading ruin entity config failed - map entity prefab \"" + name + "\" not found.");
                return;
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            
            int gIndex = 0;
            LoadChildren(element, ref gIndex);

            void LoadChildren(XElement element2, ref int groupIndex)
            {
                foreach (XElement subElement in element2.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "connection":
                        case "entityconnection":
                            EntityConnections.Add(new EntityConnection(subElement));
                            break;
                        case "chooseone":
                            groupIndex++;
                            LoadChildren(subElement, ref groupIndex);
                            break;
                        default:
                            childEntities.Add(new RuinEntityConfig(subElement) { SingleGroupIndex = groupIndex });
                            break;
                    }
                }
            }
        }
    }
}
