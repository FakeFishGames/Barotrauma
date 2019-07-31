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
            public List<string> AllowedNames;
            public int MinCount, MaxCount;

            public ChildObject()
            {
                AllowedNames = new List<string>();
                MinCount = 1;
                MaxCount = 1;
            }

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

        public List<Sprite> Sprites
        {
            get;
            private set;
        } = new List<Sprite>();

        public List<Sprite> SpecularSprites
        {
            get;
            private set;
        } = new List<Sprite>();

        public DeformableSprite DeformableSprite
        {
            get;
            private set;
        }

        [Serialize(1.0f, false), Editable(MinValueFloat = 0.01f, MaxValueFloat = 10.0f)]
        public float MinSize
        {
            get;
            private set;
        }
        [Serialize(1.0f, false), Editable(MinValueFloat = 0.01f, MaxValueFloat = 10.0f)]
        public float MaxSize
        {
            get;
            private set;
        }

        /// <summary>
        /// Which sides of a wall the object can appear on.
        /// </summary>
        [Serialize((Alignment.Top | Alignment.Bottom | Alignment.Left | Alignment.Right), true), Editable(ToolTip = "Which sides of a wall the object can spawn on.")]
        public Alignment Alignment
        {
            get;
            private set;
        }

        [Serialize(SpawnPosType.Wall, false), Editable()]
        public SpawnPosType SpawnPos
        {
            get;
            private set;
        }

        public XElement Config
        {
            get;
            private set;
        }

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

        [Serialize("0.0,1.0", true), Editable()]
        public Vector2 DepthRange
        {
            get;
            private set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, 
            ToolTip = "The tendency for the prefab to form clusters. Used as an exponent for perlin noise values that are used to determine the probability for an object to spawn at a specific position.")]
        /// <summary>
        /// The tendency for the prefab to form clusters. Used as an exponent for perlin noise values 
        /// that are used to determine the probability for an object to spawn at a specific position.
        /// </summary>
        public float ClusteringAmount
        {
            get;
            private set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f,
            ToolTip = "A value between 0-1 that determines the z-coordinate to sample perlin noise from when determining the probability " +
            " for an object to spawn at a specific position. Using the same (or close) value for different objects means the objects tend " +
            "to form clusters in the same areas.")]
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

        [Serialize(false, true), Editable(ToolTip = "Should the object be rotated to align it with the wall surface it spawns on.")]
        public bool AlignWithSurface
        {
            get;
            private set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, 
            ToolTip = "Minimum length of a graph edge the object can spawn on.")]
        /// <summary>
        /// Minimum length of a graph edge the object can spawn on.
        /// </summary>
        public float MinSurfaceWidth
        {
            get;
            private set;
        }

        private Vector2 randomRotation;
        [Serialize("0.0,0.0", true), Editable(ToolTip = "How much the rotation of the object can vary (min and max values in degrees).")]
        public Vector2 RandomRotation
        {
            get { return new Vector2(MathHelper.ToDegrees(randomRotation.X), MathHelper.ToDegrees(randomRotation.Y)); }
            private set
            {
                randomRotation = new Vector2(MathHelper.ToRadians(value.X), MathHelper.ToRadians(value.Y));
            }
        }

        public Vector2 RandomRotationRad => randomRotation;

        private float swingAmount;
        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 360.0f, ToolTip = "How much the object swings (in degrees).")]
        public float SwingAmount
        {
            get { return MathHelper.ToDegrees(swingAmount); }
            private set
            {
                swingAmount = MathHelper.ToRadians(value);
            }
        }

        public float SwingAmountRad => swingAmount;

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, ToolTip = "How fast the object swings.")]
        public float SwingFrequency
        {
            get;
            private set;
        }

        [Serialize("0.0,0.0", true), Editable(ToolTip = "How much the scale of the object oscillates on each axis. A value of 0.5,0.5 would make the object's scale oscillate from 100% to 150%.")]
        public Vector2 ScaleOscillation
        {
            get;
            private set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, ToolTip = "How fast the object's scale oscillates.")]
        public float ScaleOscillationFrequency
        {
            get;
            private set;
        }

        [Serialize(1.0f, true), Editable(ToolTip = "How likely it is for the object to spawn in a level. "+
            "This is relative to the commonness of the other objects - for example, having an object with "+
            "a commonness of 1 and another with a commonness of 10 would mean the latter appears in levels 10 times as frequently as the former. "+
            "The commonness value can be overridden on specific level types.")]
        public float Commonness
        {
            get;
            private set;
        }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, ToolTip = "How much the object disrupts submarine's sonar.")]
        public float SonarDisruption
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            set;
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
                if (doc == null || doc.Root == null) { return; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    DebugConsole.NewMessage($"Overriding all level object prefabs with '{configPath}'", Color.Yellow);
                    list.Clear();
                }
                else
                {
                    DebugConsole.NewMessage($"Loading level object prefabs from file '{configPath}'");
                }
                foreach (XElement element in mainElement.Elements())
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
            ChildObjects = new List<ChildObject>();
            LevelTriggerElements = new List<XElement>();
            OverrideProperties = new List<LevelObjectPrefab>();
            OverrideCommonness = new Dictionary<string, float>();

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            if (element != null)
            {
                Config = element;
                Name = element.Name.ToString();
                LoadElements(element, -1);
                InitProjSpecific(element);
            }

            //use the maximum width of the sprite as the minimum surface width if no value is given
            if (element != null && !element.Attributes("minsurfacewidth").Any())
            {
                if (Sprites.Any()) MinSurfaceWidth = Sprites[0].size.X * MaxSize;
                if (DeformableSprite != null) MinSurfaceWidth = Math.Max(MinSurfaceWidth, DeformableSprite.Size.X * MaxSize);
            }
        }

        private void LoadElements(XElement element, int parentTriggerIndex)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprites.Add( new Sprite(subElement, lazyLoad: true));
                        break;
                    case "specularsprite":
                        SpecularSprites.Add(new Sprite(subElement, lazyLoad: true));
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement, lazyLoad: true);
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
                        if (!propertyOverride.Sprites.Any() && propertyOverride.DeformableSprite == null)
                        {
                            propertyOverride.Sprites = Sprites;
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
