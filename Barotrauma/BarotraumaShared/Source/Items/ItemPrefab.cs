using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    struct DeconstructItem
    {
        public readonly string ItemIdentifier;
        //minCondition does <= check, meaning that below or equeal to min condition will be skipped.
        public readonly float MinCondition;
        //maxCondition does > check, meaning that above this max the deconstruct item will be skipped.
        public readonly float MaxCondition;
        //Condition of item on creation
        public readonly float OutCondition;
        //should the condition of the deconstructed item be copied to the output items
        public readonly bool CopyCondition;

        public DeconstructItem(XElement element)
        {
            ItemIdentifier = element.GetAttributeString("identifier", "notfound");
            MinCondition = element.GetAttributeFloat("mincondition", -0.1f);
            MaxCondition = element.GetAttributeFloat("maxcondition", 1.0f);
            OutCondition = element.GetAttributeFloat("outcondition", 1.0f);
            CopyCondition = element.GetAttributeBool("copycondition", false);
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        private readonly string configFile;
        
        //default size
        protected Vector2 size;                

        private float impactTolerance;

        private bool canSpriteFlipX, canSpriteFlipY;
        
        private Dictionary<string, PriceInfo> prices;

        //an area next to the construction
        //the construction can be Activated() by a Character inside the area
        public List<Rectangle> Triggers;

        public string ConfigFile
        {
            get { return configFile; }
        }

        public XElement ConfigElement
        {
            get;
            private set;
        }

        public List<DeconstructItem> DeconstructItems
        {
            get;
            private set;
        }

        public float DeconstructTime
        {
            get;
            private set;
        }

        //how close the Character has to be to the item to pick it up
        [Serialize(120.0f, false)]
        public float InteractDistance
        {
            get;
            private set;
        }

        // this can be used to allow items which are behind other items tp
        [Serialize(0.0f, false)]
        public float InteractPriority
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool InteractThroughWalls
        {
            get;
            private set;
        }


        //should the camera focus on the item when selected
        [Serialize(false, false)]
        public bool FocusOnSelected
        {
            get;
            private set;
        }

        //the amount of "camera offset" when selecting the construction
        [Serialize(0.0f, false)]
        public float OffsetOnSelected
        {
            get;
            private set;
        }

        [Serialize(100.0f, false)]
        public float Health
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool Indestructible
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool FireProof
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool WaterProof
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float ImpactTolerance
        {
            get { return impactTolerance; }
            set { impactTolerance = Math.Max(value, 0.0f); }
        }

        [Serialize(0.0f, false)]
        public float SonarSize
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseInHealthInterface
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DisableItemUsageWhenSelected
        {
            get;
            private set;
        }

        [Serialize("", false)]        
        public string CargoContainerIdentifier
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseContainedSpriteColor
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseContainedInventoryIconColor
        {
            get;
            private set;
        }

        /// <summary>
        /// How likely it is for the item to spawn in a level of a given type.
        /// Key = name of the LevelGenerationParameters (empty string = default value)
        /// Value = commonness
        /// </summary>
        public Dictionary<string, float> LevelCommonness
        {
            get;
            private set;
        } = new Dictionary<string, float>();

        public bool CanSpriteFlipX
        {
            get { return canSpriteFlipX; }
        }

        public bool CanSpriteFlipY
        {
            get { return canSpriteFlipY; }
        }

        public Vector2 Size
        {
            get { return size; }
        }

        public bool CanBeBought
        {
            get { return prices != null && prices.Count > 0; }
        }

        public override void UpdatePlacing(Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

            if (PlayerInput.RightButtonClicked())
            {
                selected = null;
                return;
            }

            if (!ResizeHorizontal && !ResizeVertical)
            {
                if (PlayerInput.LeftButtonClicked())
                {
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)(sprite.size.X * Scale), (int)(sprite.size.Y * Scale)), this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                    item.FindHull();

                    placePosition = Vector2.Zero;
                    return;
                }
            }
            else
            {
                Vector2 placeSize = size;

                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.LeftButtonHeld()) placePosition = position;
                }
                else
                {
                    if (ResizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (ResizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, size.Y);

                    if (PlayerInput.LeftButtonReleased())
                    {
                        var item = new Item(new Rectangle((int)placePosition.X, (int)placePosition.Y, (int)placeSize.X, (int)placeSize.Y), this, Submarine.MainSub);
                        placePosition = Vector2.Zero;

                        item.Submarine = Submarine.MainSub;
                        item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                        item.FindHull();

                        //selected = null;
                        return;
                    }

                    position = placePosition;
                }
            }

            //if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;

        }

        public static void LoadAll(IEnumerable<string> filePaths)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.Log("Loading item prefabs: ");
            }

            foreach (string filePath in filePaths)
            {
                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.Log("*** " + filePath + " ***");
                }

                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null) return;

                if (doc.Root.Name.ToString().ToLowerInvariant() == "item")
                {
                    new ItemPrefab(doc.Root, filePath);
                }
                else
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLowerInvariant() != "item") continue;

                        new ItemPrefab(element, filePath);
                    }
                }
            }
        }

        public ItemPrefab(XElement element, string filePath)
        {
            configFile = filePath;
            ConfigElement = element;

            identifier = element.GetAttributeString("identifier", "");

            name = TextManager.Get("EntityName." + identifier, true) ?? element.GetAttributeString("name", "");
            if (name == "") DebugConsole.ThrowError("Unnamed item in " + filePath + "!");

            DebugConsole.Log("    " + name);

            Aliases = element.GetAttributeStringArray("aliases", new string[0], convertToLowerInvariant: true);

            if (!Enum.TryParse(element.GetAttributeString("category", "Misc"), true, out MapEntityCategory category))
            {
                category = MapEntityCategory.Misc;
            }
            Category = category;
            
            Triggers            = new List<Rectangle>();
            DeconstructItems    = new List<DeconstructItem>();
            DeconstructTime     = 1.0f;
            
            Tags = element.GetAttributeStringArray("tags", new string[0], convertToLowerInvariant: true).ToHashSet();
            if (Tags.None())
            {
                Tags = element.GetAttributeStringArray("Tags", new string[0], convertToLowerInvariant: true).ToHashSet();
            }

            if (element.Attribute("cargocontainername") != null)
            {
                DebugConsole.ThrowError("Error in item prefab \"" + name + "\" - cargo container should be configured using the item's identifier, not the name.");
            }

            SerializableProperty.DeserializeProperties(this, element);

            string translatedDescription = TextManager.Get("EntityDescription." + identifier, true);
            if (!string.IsNullOrEmpty(translatedDescription)) Description = translatedDescription;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        string spriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            spriteFolder = Path.GetDirectoryName(filePath);
                        }

                        canSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        canSpriteFlipY = subElement.GetAttributeBool("canflipy", true);

                        sprite = new Sprite(subElement, spriteFolder);
                        if (subElement.Attribute("sourcerect") == null)
                        {
                            DebugConsole.ThrowError("Warning - sprite sourcerect not configured for item \"" + Name + "\"!");
                        }
                        size = sprite.size;
                        break;
                    case "price":
                        string locationType = subElement.GetAttributeString("locationtype", "");
                        if (prices == null) prices = new Dictionary<string, PriceInfo>();
                        prices[locationType.ToLowerInvariant()] = new PriceInfo(subElement);
                        break;
#if CLIENT
                    case "inventoryicon":
                        string iconFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            iconFolder = Path.GetDirectoryName(filePath);
                        }
                        InventoryIcon = new Sprite(subElement, iconFolder);
                        break;
                    case "brokensprite":
                        string brokenSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            brokenSpriteFolder = Path.GetDirectoryName(filePath);
                        }

                        var brokenSprite = new BrokenItemSprite(
                            new Sprite(subElement, brokenSpriteFolder), 
                            subElement.GetAttributeFloat("maxcondition", 0.0f),
                            subElement.GetAttributeBool("fadein", false));

                        int spriteIndex = 0;
                        for (int i = 0; i < BrokenSprites.Count && BrokenSprites[i].MaxCondition < brokenSprite.MaxCondition; i++)
                        {
                            spriteIndex = i;
                        }
                        BrokenSprites.Insert(spriteIndex, brokenSprite);
                        break;
                    case "decorativesprite":
                        string decorativeSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            decorativeSpriteFolder = Path.GetDirectoryName(filePath);
                        }

                        DecorativeSprites.Add(new DecorativeSprite(subElement, decorativeSpriteFolder));
                        break;
#endif
                    case "deconstruct":
                        DeconstructTime = subElement.GetAttributeFloat("time", 10.0f);

                        foreach (XElement deconstructItem in subElement.Elements())
                        {
                            if (deconstructItem.Attribute("name") != null)
                            {
                                DebugConsole.ThrowError("Error in item config \"" + Name + "\" - use item identifiers instead of names to configure the deconstruct items.");
                                continue;
                            }

                            DeconstructItems.Add(new DeconstructItem(deconstructItem));
                        }

                        break;
                    case "trigger":
                        Rectangle trigger = new Rectangle(0, 0, 10, 10)
                        {
                            X = subElement.GetAttributeInt("x", 0),
                            Y = subElement.GetAttributeInt("y", 0),
                            Width = subElement.GetAttributeInt("width", 0),
                            Height = subElement.GetAttributeInt("height", 0)
                        };

                        Triggers.Add(trigger);

                        break;
                    case "levelresource":
                        foreach (XElement levelCommonnessElement in subElement.Elements())
                        {
                            string levelName = levelCommonnessElement.GetAttributeString("levelname", "").ToLowerInvariant();
                            if (!LevelCommonness.ContainsKey(levelName))
                            {
                                LevelCommonness.Add(levelName, levelCommonnessElement.GetAttributeFloat("commonness", 0.0f));
                            }
                        }
                        break;
                    case "suitabletreatment":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in item prefab \"" + Name + "\" - suitable treatments should be defined using item identifiers, not item names.");
                        }

                        string treatmentIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();

                        var matchingAffliction = AfflictionPrefab.List.Find(a => a.Identifier == treatmentIdentifier);
                        if (matchingAffliction != null)
                        {
                            matchingAffliction.TreatmentSuitability.Add(identifier, subElement.GetAttributeFloat("suitability", 0.0f));
                        }
                        break;
                }
            }
            
            if (!category.HasFlag(MapEntityCategory.Legacy) && string.IsNullOrEmpty(identifier))
            {
                DebugConsole.ThrowError(
                    "Item prefab \"" + name + "\" has no identifier. All item prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }
            if (!string.IsNullOrEmpty(identifier))
            {
                MapEntityPrefab existingPrefab = List.Find(e => e.Identifier == identifier);
                if (existingPrefab != null)
                {
                    DebugConsole.ThrowError(
                        "Map entity prefabs \"" + name + "\" and \"" + existingPrefab.Name + "\" have the same identifier!");
                }
            }

            AllowedLinks = element.GetAttributeStringArray("allowedlinks", new string[0], convertToLowerInvariant: true).ToList();

            List.Add(this);
        }

        public PriceInfo GetPrice(Location location)
        {
            if (prices == null || !prices.ContainsKey(location.Type.Name.ToLowerInvariant())) return null;
            return prices[location.Type.Name.ToLowerInvariant()];
        }


        public IEnumerable<PriceInfo> GetPrices()
        {
            return prices?.Values;
        }
    }
}
