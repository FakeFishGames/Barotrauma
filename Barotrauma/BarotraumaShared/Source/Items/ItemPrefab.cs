using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
        private float pickDistance;

        private bool pickThroughWalls;

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

        public float PickDistance
        {
            get { return pickDistance; }
        }

        public bool PickThroughWalls
        {
            get { return pickThroughWalls; }
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
            DebugConsole.Log("Loading item prefabs: ");

            foreach (string filePath in filePaths)
            {
                DebugConsole.Log("*** "+filePath+" ***");

                XDocument doc = ToolBox.TryLoadXml(filePath);
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

            name = ToolBox.GetAttributeString(element, "name", "");
            if (name == "") DebugConsole.ThrowError("Unnamed item in "+filePath+"!");

            DebugConsole.Log("    "+name);

            Description = ToolBox.GetAttributeString(element, "description", "");

            pickThroughWalls    = ToolBox.GetAttributeBool(element, "pickthroughwalls", false);
            pickDistance        = ToolBox.GetAttributeFloat(element, "pickdistance", 0.0f);
            
            isLinkable          = ToolBox.GetAttributeBool(element, "linkable", false);

            resizeHorizontal    = ToolBox.GetAttributeBool(element, "resizehorizontal", false);
            resizeVertical      = ToolBox.GetAttributeBool(element, "resizevertical", false);

            focusOnSelected     = ToolBox.GetAttributeBool(element, "focusonselected", false);

            offsetOnSelected    = ToolBox.GetAttributeFloat(element, "offsetonselected", 0.0f);

            CanUseOnSelf        = ToolBox.GetAttributeBool(element, "canuseonself", false);
            
            FireProof           = ToolBox.GetAttributeBool(element, "fireproof", false);

            ImpactTolerance     = ToolBox.GetAttributeFloat(element, "impacttolerance", 0.0f);

            string aliases = ToolBox.GetAttributeString(element, "aliases", "");
            if (!string.IsNullOrWhiteSpace(aliases))
            {
                Aliases = aliases.Split(',');
            }

            MapEntityCategory category;

            if (!Enum.TryParse(ToolBox.GetAttributeString(element, "category", "Misc"), true, out category))
            {
                category = MapEntityCategory.Misc;
            }

            Category = category;
            
            
            string spriteColorStr = ToolBox.GetAttributeString(element, "spritecolor", "1.0,1.0,1.0,1.0");
            SpriteColor = new Color(ToolBox.ParseToVector4(spriteColorStr));

            price = ToolBox.GetAttributeInt(element, "price", 0);
            
            Triggers            = new List<Rectangle>();

            DeconstructItems    = new List<DeconstructItem>();
            DeconstructTime     = 1.0f;

            tags = new List<string>();
            tags.AddRange(ToolBox.GetAttributeString(element, "tags", "").Split(','));

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        string spriteFolder = "";
                        if (!ToolBox.GetAttributeString(subElement, "texture", "").Contains("/"))
                        {
                            spriteFolder = Path.GetDirectoryName(filePath);
                        }

                        canSpriteFlipX = ToolBox.GetAttributeBool(subElement, "canflipx", true);

                        sprite = new Sprite(subElement, spriteFolder);
                        size = sprite.size;
                        break;
                    case "deconstruct":
                        DeconstructTime = ToolBox.GetAttributeFloat(subElement, "time", 10.0f);

                        foreach (XElement deconstructItem in subElement.Elements())
                        {

                            string deconstructItemName = ToolBox.GetAttributeString(deconstructItem, "name", "not found");
                            bool requireFullCondition = ToolBox.GetAttributeBool(deconstructItem, "requirefullcondition", false);

                            DeconstructItems.Add(new DeconstructItem(deconstructItemName, requireFullCondition));

                        }

                        break;
                    case "trigger":
                        Rectangle trigger = new Rectangle(0, 0, 10,10);

                        trigger.X = ToolBox.GetAttributeInt(subElement, "x", 0);
                        trigger.Y = ToolBox.GetAttributeInt(subElement, "y", 0);

                        trigger.Width = ToolBox.GetAttributeInt(subElement, "width", 0);
                        trigger.Height = ToolBox.GetAttributeInt(subElement, "height", 0);

                        Triggers.Add(trigger);

                        break;
                }
            }

            list.Add(this);
        }
    }
}
