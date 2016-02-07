using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class ItemPrefab : MapEntityPrefab
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

        public string ConfigFile
        {
            get { return configFile; }
        }

        public List<string> DeconstructItems
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

        public Vector2 Size
        {
            get { return size; }
        }

        public override void UpdatePlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam); 

            if (PlayerInput.RightButtonClicked())
            {
                selected = null;
                return;
            }
            
            if (!resizeHorizontal && !resizeVertical)
            {
                if (PlayerInput.LeftButtonClicked())
                {
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)sprite.size.X, (int)sprite.size.Y), this, Submarine.Loaded);
                    //constructor.Invoke(lobject);
                    item.Submarine = Submarine.Loaded;
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.Loaded==null ? item.Position : item.Position - Submarine.Loaded.Position), 0.0f);
                    item.FindHull();

                    placePosition = Vector2.Zero;

                   // selected = null;
                    return;
                }

                sprite.Draw(spriteBatch, new Vector2(position.X + sprite.size.X / 2.0f, -position.Y + sprite.size.Y / 2.0f), SpriteColor);
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
                        var item = new Item(new Rectangle((int)placePosition.X, (int)placePosition.Y, (int)placeSize.X, (int)placeSize.Y), this, Submarine.Loaded);
                        placePosition = Vector2.Zero;

                        item.Submarine = Submarine.Loaded;
                        item.SetTransform(ConvertUnits.ToSimUnits(Submarine.Loaded == null ? item.Position : item.Position - Submarine.Loaded.Position), 0.0f);
                        item.FindHull();
                        
                        //selected = null;
                        return;
                    }

                    position = placePosition;
                }

                if (sprite != null) sprite.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, SpriteColor);
            }
            
            //if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;

        }

        public static void LoadAll(List<string> filePaths)
        {
            //string[] files = Directory.GetFiles(contentFolder, "*.xml", SearchOption.AllDirectories);

            foreach (string filePath in filePaths)
            {
                XDocument doc = ToolBox.TryLoadXml(filePath);
                if (doc == null) return;

                if (doc.Root.Name.ToString().ToLower() == "item")
                {
                    new ItemPrefab(doc.Root, filePath);
                }
                else
                {
                    foreach (XElement element in doc.Root.Elements())
                    {
                        if (element.Name.ToString().ToLower() != "item") continue;

                        new ItemPrefab(element, filePath);
                    }
                }
            }
        }

        public ItemPrefab (XElement element, string filePath)
        {

            configFile = filePath;

            name = ToolBox.GetAttributeString(element, "name", "");
            if (name == "") DebugConsole.ThrowError("Unnamed item in "+filePath+"!");

            Description = ToolBox.GetAttributeString(element, "description", "");

            pickThroughWalls    = ToolBox.GetAttributeBool(element, "pickthroughwalls", false);
            pickDistance        = ToolBox.GetAttributeFloat(element, "pickdistance", 0.0f);
            
            isLinkable          = ToolBox.GetAttributeBool(element, "linkable", false);

            resizeHorizontal    = ToolBox.GetAttributeBool(element, "resizehorizontal", false);
            resizeVertical      = ToolBox.GetAttributeBool(element, "resizevertical", false);

            focusOnSelected     = ToolBox.GetAttributeBool(element, "focusonselected", false);

            offsetOnSelected    = ToolBox.GetAttributeFloat(element, "offsetonselected", 0.0f);
            
            FireProof = ToolBox.GetAttributeBool(element, "fireproof", false);


            string categoriesStr = ToolBox.GetAttributeString(element, "category", "Misc");
            string[] categories = categoriesStr.Split(',');

            for (int i = 0; i<categories.Length; i++)
            {

                MapEntityCategory category;
                if (Enum.TryParse(ToolBox.GetAttributeString(element, "category", "Misc"), out category))
                {
                    Category = i == 0 ? category : Category | category;
                }
            }
            
            string spriteColorStr = ToolBox.GetAttributeString(element, "spritecolor", "1.0,1.0,1.0,1.0");
            SpriteColor = new Color(ToolBox.ParseToVector4(spriteColorStr));

            price = ToolBox.GetAttributeInt(element, "price", 0);
            
            Triggers            = new List<Rectangle>();

            DeconstructItems    = new List<string>();
            DeconstructTime     = 1.0f;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        string spriteFolder = "";
                        if (!ToolBox.GetAttributeString(subElement, "texture", "").Contains("/"))
                        {
                            spriteFolder = Path.GetDirectoryName(filePath);
                        }

                        sprite = new Sprite(subElement, spriteFolder);
                        size = sprite.size;
                        break;
                    case "deconstruct":
                        DeconstructTime = ToolBox.GetAttributeFloat(subElement, "time", 10.0f);

                        foreach (XElement deconstructItem in subElement.Elements())
                        {
                            DeconstructItems.Add(ToolBox.GetAttributeString(deconstructItem, "name", "not found"));
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
