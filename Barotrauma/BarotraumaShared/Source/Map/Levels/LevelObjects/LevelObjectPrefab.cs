using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LevelObjectPrefab : ISerializableEntity
    {
        private static List<LevelObjectPrefab> list = new List<LevelObjectPrefab>();
        public static List<LevelObjectPrefab> List
        {
            get { return list; }
        }

        public class ChildObject
        {
            public readonly List<string> AllowedNames;
            public readonly int MinCount, MaxCount;

            public ChildObject(XElement element)
            {
                AllowedNames = element.GetAttributeStringArray("names", new string[0]).ToList();
                MinCount = element.GetAttributeInt("mincount", 1);
                MaxCount = Math.Max(element.GetAttributeInt("maxcount", 1), MinCount);
            }
        }

        [Flags]
        public enum SpawnPosType
        {
            None = 0,
            Wall = 1,
            RuinWall = 2,
            SeaFloor = 4,
            MainPath = 8
        }
        
        /// <summary>
        /// Which sides of a wall the object can appear on.
        /// </summary>
        public readonly Alignment Alignment;
        
        public Sprite Sprite
        {
            get;
            private set;
        }

        public Sprite SpecularSprite
        {
            get;
            private set;
        }

        public DeformableSprite DeformableSprite
        {
            get;
            private set;
        }

        public readonly Vector2 Scale;

        public SpawnPosType SpawnPos;

        public readonly XElement Config;

        public readonly List<XElement> LevelTriggerElements;
        
        /// <summary>
        /// Overrides the commonness of the object in a specific level type. 
        /// Key = name of the level type, value = commonness in that level type.
        /// </summary>
        public Dictionary<string, float> OverrideCommonness;

        public XElement PhysicsBodyElement
        {
            get;
            private set;
        }
        public int PhysicsBodyTriggerIndex
        {
            get;
            private set;
        }

        [Serialize("0.0,1.0", false)]
        public Vector2 DepthRange
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        /// <summary>
        /// The tendency for the prefab to form clusters. Used as an exponent for perlin noise values 
        /// that are used to determine the probability for an object to spawn at a specific position.
        /// </summary>
        public float ClusteringAmount
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        /// <summary>
        /// A value between 0-1 that determines the z-coordinate to sample perlin noise from when
        /// determining the probability  for an object to spawn at a specific position.
        /// Using the same (or close) value for different objects means the objects tend to form clusters
        /// in the same areas.
        /// </summary>
        public float ClusteringGroup
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool AlignWithSurface
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        /// <summary>
        /// Minimum length of a graph edge the object can spawn on.
        /// </summary>
        public float MinSurfaceWidth
        {
            get;
            private set;
        }

        private Vector2 randomRotation;
        [Serialize("0.0,0.0", false)]
        public Vector2 RandomRotation
        {
            get { return randomRotation; }
            private set
            {
                randomRotation = new Vector2(MathHelper.ToRadians(value.X), MathHelper.ToRadians(value.Y));
            }
        }

        private float swingAmount;
        [Serialize(0.0f, false)]
        public float SwingAmount
        {
            get { return swingAmount; }
            private set
            {
                swingAmount = MathHelper.ToRadians(value);
            }
        }

        [Serialize(0.0f, false)]
        public float SwingFrequency
        {
            get;
            private set;
        }

        [Serialize("0.0,0.0", false)]
        public Vector2 ScaleOscillation
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float ScaleOscillationFrequency
        {
            get;
            private set;
        }

        [Serialize(1.0f, false)]
        public float Commonness
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float SonarDisruption
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public List<ChildObject> ChildObjects
        {
            get;
            private set;
        }
        
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get; private set;
        }

        /// <summary>
        /// A list of prefabs whose properties override this one's properties when a trigger is active.
        /// E.g. if a trigger in the index 1 of the trigger list is active, the properties in index 1 in this list are used (unless it's null)
        /// </summary>
        public List<LevelObjectPrefab> OverrideProperties
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return "LevelObjectPrefab (" + Name + ")";
        }

        public static void LoadAll()
        {
            var files = GameMain.Instance.GetFilesOfType(ContentType.LevelObjectPrefabs);
            if (files.Count() > 0)
            {
                foreach (var file in files)
                {
                    LoadConfig(file);
                }
            }
            else
            {
                LoadConfig("Content/LevelObjects/LevelObject/Prefabs.xml");
            }
        }

        private static void LoadConfig(string configPath)
        {
            try
            {
                XDocument doc = XMLExtensions.TryLoadXml(configPath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    list.Add(new LevelObjectPrefab(element));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load LevelObject prefabs from {0}", configPath), e);
            }
        }

        public LevelObjectPrefab(XElement element)
        {
            Config = element;

            Name = element.Name.ToString();

            ChildObjects = new List<ChildObject>();
            LevelTriggerElements = new List<XElement>();
            OverrideProperties = new List<LevelObjectPrefab>();

            string alignmentStr = element.GetAttributeString("alignment", "");

            if (string.IsNullOrEmpty(alignmentStr) || !Enum.TryParse(alignmentStr, out Alignment))
            {
                Alignment = Alignment.Top | Alignment.Bottom | Alignment.Left | Alignment.Right;
            }
            
            string[] spawnPosStrs = element.GetAttributeString("spawnpos", "Wall").Split(',');
            foreach (string spawnPosStr in spawnPosStrs)
            {
                if (Enum.TryParse(spawnPosStr.Trim(), out SpawnPosType parsedSpawnPos))
                {
                    SpawnPos |= parsedSpawnPos;
                }
            }

            Scale.X = element.GetAttributeFloat("minsize", 1.0f);
            Scale.Y = element.GetAttributeFloat("maxsize", 1.0f);
            
            OverrideCommonness = new Dictionary<string, float>();

            LoadElements(element, -1);

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            InitProjSpecific(element);

            //use the maximum width of the sprite as the minimum surface width if no value is given
            if (!element.Attributes("minsurfacewidth").Any())
            {
                if (Sprite != null) MinSurfaceWidth = Sprite.size.X * Scale.Y;
                if (DeformableSprite != null) MinSurfaceWidth = Math.Max(MinSurfaceWidth, DeformableSprite.Size.X * Scale.Y);
            }
        }

        private void LoadElements(XElement element, int parentTriggerIndex)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement);
                        break;
                    case "specularsprite":
                        SpecularSprite = new Sprite(subElement);
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement);
                        break;
                    case "overridecommonness":
                        string levelType = subElement.GetAttributeString("leveltype", "");
                        if (!OverrideCommonness.ContainsKey(levelType))
                        {
                            OverrideCommonness.Add(levelType, subElement.GetAttributeFloat("commonness", 1.0f));
                        }
                        break;
                    case "leveltrigger":
                    case "trigger":
                        OverrideProperties.Add(null);
                        LevelTriggerElements.Add(subElement);
                        LoadElements(subElement, LevelTriggerElements.Count - 1);
                        break;
                    case "childobject":
                        ChildObjects.Add(new ChildObject(subElement));
                        break;
                    case "overrideproperties":
                        var propertyOverride = new LevelObjectPrefab(subElement);
                        OverrideProperties[OverrideProperties.Count - 1] = propertyOverride;
                        if (propertyOverride.Sprite == null && propertyOverride.DeformableSprite == null)
                        {
                            propertyOverride.Sprite = Sprite;
                            propertyOverride.DeformableSprite = DeformableSprite;
                        }
                        break;
                    case "body":
                    case "physicsbody":
                        PhysicsBodyElement = subElement;
                        PhysicsBodyTriggerIndex = parentTriggerIndex;
                        break;
                }
            }
        }
        
        partial void InitProjSpecific(XElement element);

        public float GetCommonness(string levelType)
        {
            if (!OverrideCommonness.TryGetValue(levelType, out float commonness))
            {
                return Commonness;
            }
            return commonness;
        }
    }
}
