using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    struct DeconstructItem
    {
        public readonly string ItemPrefabName;
        public readonly float MinCondition;
        public readonly float MaxCondition;
        public readonly float OutCondition;

        public DeconstructItem(string itemPrefabName, float minCondition, float maxCondition, float outCondition)
        {
            ItemPrefabName = itemPrefabName;
            MinCondition = minCondition;
            MaxCondition = maxCondition;
            OutCondition = outCondition;
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        private readonly string configFile;
        
        //default size
        protected Vector2 size;
                
        //an area next to the construction
        //the construction can be Activated() by a Character inside the area
        public List<Rectangle> Triggers;

        private float impactTolerance;

        public string ConfigFile
        {
            get { return configFile; }
        }

        public XElement ConfigElement
        {
            get;
            private set;
        }

        private bool canSpriteFlipX;

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

        [Serialize(0.0f, false)]
        public float ImpactTolerance
        {
            get { return impactTolerance; }
            set { impactTolerance = Math.Max(value, 0.0f); }
        }

        [Serialize(false, false)]
        public bool CanUseOnSelf
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
        public string CargoContainerName
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

        public bool CanSpriteFlipX
        {
            get { return canSpriteFlipX; }
        }

        public Vector2 Size
        {
            get { return size; }
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
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)sprite.size.X, (int)sprite.size.Y), this, Submarine.MainSub);
                    //constructor.Invoke(lobject);
                    item.Submarine = Submarine.MainSub;
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                    item.FindHull();

                    placePosition = Vector2.Zero;

                    // selected = null;
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

        public static void LoadAll(List<string> filePaths)
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

            name = element.GetAttributeString("name", "");
            if (name == "") DebugConsole.ThrowError("Unnamed item in " + filePath + "!");

            DebugConsole.Log("    " + name);

            string aliases = element.GetAttributeString("aliases", "");
            if (!string.IsNullOrWhiteSpace(aliases))
            {
                Aliases = aliases.Split(',');
            }

            MapEntityCategory category;
            if (!Enum.TryParse(element.GetAttributeString("category", "Misc"), true, out category))
            {
                category = MapEntityCategory.Misc;
            }
            Category = category;
            
            Triggers            = new List<Rectangle>();
            DeconstructItems    = new List<DeconstructItem>();
            DeconstructTime     = 1.0f;

            Tags = new List<string>();
            Tags.AddRange(element.GetAttributeString("tags", "").Split(','));

            SerializableProperty.DeserializeProperties(this, element);

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

                        sprite = new Sprite(subElement, spriteFolder);
                        if (subElement.Attribute("sourcerect") == null)
                        {
                            DebugConsole.ThrowError("Warning - sprite sourcerect not configured for item \"" + Name + "\"!");
                        }
                        size = sprite.size;
                        break;
#if CLIENT
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
#endif
                    case "deconstruct":
                        DeconstructTime = subElement.GetAttributeFloat("time", 10.0f);

                        foreach (XElement deconstructItem in subElement.Elements())
                        {
                            string deconstructItemName = deconstructItem.GetAttributeString("name", "not found");
                            //minCondition does <= check, meaning that below or equeal to min condition will be skipped.
                            float minCondition = deconstructItem.GetAttributeFloat("mincondition", -0.1f);
                            //maxCondition does > check, meaning that above this max the deconstruct item will be skipped.
                            float maxCondition = deconstructItem.GetAttributeFloat("maxcondition", 1.0f);
                            //Condition of item on creation
                            float outCondition = deconstructItem.GetAttributeFloat("outcondition", 1.0f);

                            DeconstructItems.Add(new DeconstructItem(deconstructItemName, minCondition, maxCondition, outCondition));

                        }

                        break;
                    case "trigger":
                        Rectangle trigger = new Rectangle(0, 0, 10,10);

                        trigger.X = subElement.GetAttributeInt("x", 0);
                        trigger.Y = subElement.GetAttributeInt("y", 0);

                        trigger.Width = subElement.GetAttributeInt("width", 0);
                        trigger.Height = subElement.GetAttributeInt("height", 0);

                        Triggers.Add(trigger);

                        break;
                }
            }

            List.Add(this);
        }
    }
}
