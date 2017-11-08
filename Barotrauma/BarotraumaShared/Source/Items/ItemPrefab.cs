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
        public readonly bool RequireFullCondition;

        public DeconstructItem(string itemPrefabName, bool requireFullCondition)
        {
            ItemPrefabName = itemPrefabName;
            RequireFullCondition = requireFullCondition;
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        //static string contentFolder = "Content/Items/";

        string configFile;

        //should the camera focus on the construction when selected
        protected bool focusOnSelected;
        //the amount of "camera offset" when selecting the construction
        protected float offsetOnSelected;
        //default size
        protected Vector2 size;

        //how close the Character has to be to the item to pick it up
        private float interactDistance;
        // this can be used to allow items which are behind other items tp
        private float interactPriority; 

        private bool interactThroughWalls;

        //an area next to the construction
        //the construction can be Activated() by a Character inside the area
        public List<Rectangle> Triggers;

        public readonly bool FireProof;

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

        //if a matching itemprefab is not found when loading a sub, the game will attempt to find a prefab with a matching alias
        //(allows changing item names while keeping backwards compatibility with older sub files)
        public string[] Aliases
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

        public float InteractDistance
        {
            get { return interactDistance; }
        }

        public float InteractPriority
        {
            get { return interactPriority; }
        }

        public bool InteractThroughWalls
        {
            get { return interactThroughWalls; }
        }

        public override bool IsLinkable
        {
            get { return isLinkable; }
        }

        public bool FocusOnSelected
        {
            get { return focusOnSelected; }
        }

        public float OffsetOnSelected
        {
            get { return offsetOnSelected; }
        }

        public float Health
        {
            get;
            private set;
        }

        public bool Indestructible
        {
            get;
            private set;
        }

        public float ImpactTolerance
        {
            get { return impactTolerance; }
            set { impactTolerance = Math.Max(value, 0.0f); }
        }

        public bool CanUseOnSelf
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

            if (!resizeHorizontal && !resizeVertical)
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
                    if (resizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (resizeVertical)
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

        public ItemPrefab (XElement element, string filePath)
        {
            configFile = filePath;
            ConfigElement = element;

            name = element.GetAttributeString("name", "");
            if (name == "") DebugConsole.ThrowError("Unnamed item in "+filePath+"!");

            DebugConsole.Log("    "+name);

            Description = element.GetAttributeString("description", "");

            interactThroughWalls    = element.GetAttributeBool("interactthroughwalls", false);
            interactDistance        = element.GetAttributeFloat("interactdistance", 120.0f); // Default to 120 as the new item picking method is tuned to this number
            interactPriority        = element.GetAttributeFloat("interactpriority", 0.0f);

            isLinkable          = element.GetAttributeBool("linkable", false);

            resizeHorizontal    = element.GetAttributeBool("resizehorizontal", false);
            resizeVertical      = element.GetAttributeBool("resizevertical", false);

            focusOnSelected     = element.GetAttributeBool("focusonselected", false);

            offsetOnSelected    = element.GetAttributeFloat("offsetonselected", 0.0f);

            CanUseOnSelf        = element.GetAttributeBool("canuseonself", false);
            

            Health              = element.GetAttributeFloat("health", 100.0f);
            Indestructible      = element.GetAttributeBool("indestructible", false);
            FireProof           = element.GetAttributeBool("fireproof", false);
            ImpactTolerance     = element.GetAttributeFloat("impacttolerance", 0.0f);

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
            
            
            string spriteColorStr = element.GetAttributeString("spritecolor", "1.0,1.0,1.0,1.0");
            SpriteColor = new Color(XMLExtensions.ParseVector4(spriteColorStr));

            price = element.GetAttributeInt("price", 0);
            
            Triggers            = new List<Rectangle>();

            DeconstructItems    = new List<DeconstructItem>();
            DeconstructTime     = 1.0f;

            tags = new List<string>();
            tags.AddRange(element.GetAttributeString("tags", "").Split(','));

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
                        size = sprite.size;
                        break;
                    case "deconstruct":
                        DeconstructTime = subElement.GetAttributeFloat("time", 10.0f);

                        foreach (XElement deconstructItem in subElement.Elements())
                        {

                            string deconstructItemName = deconstructItem.GetAttributeString("name", "not found");
                            bool requireFullCondition = deconstructItem.GetAttributeBool("requirefullcondition", false);

                            DeconstructItems.Add(new DeconstructItem(deconstructItemName, requireFullCondition));

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

            list.Add(this);
        }
    }
}
