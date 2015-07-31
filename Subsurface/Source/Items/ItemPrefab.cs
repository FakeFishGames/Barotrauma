using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
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

        //how close the character has to be to the item to pick it up
        private float pickDistance;

        //an area next to the construction
        //the construction can be Activated() by a character inside the area
        public List<Rectangle> Triggers;

        public string ConfigFile
        {
            get { return configFile; }
        }

        public float PickDistance
        {
            get { return pickDistance; }
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
            
            if (!resizeHorizontal && !resizeVertical)
            {
                if (PlayerInput.LeftButtonClicked())
                {
                    new Item(new Rectangle((int)position.X, (int)position.Y, (int)sprite.size.X, (int)sprite.size.Y), this);
                    //constructor.Invoke(lobject);

                    placePosition = Vector2.Zero;

                    selected = null;
                    return;
                }

                sprite.Draw(spriteBatch, new Vector2(position.X + sprite.size.X / 2.0f, -position.Y + sprite.size.Y / 2.0f));
            }
            else
            {
                Vector2 placeSize = size;

                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                        placePosition = position;
                }
                else
                {
                    if (resizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (resizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, size.Y);

                    if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                    {
                        new Item(new Rectangle((int)placePosition.X, (int)placePosition.Y, (int)placeSize.X, (int)placeSize.Y), this);
                        
                        selected = null;
                        return;
                    }

                    position = placePosition;
                }

                sprite.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, Color.White);
            }
            
            if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;

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
            
            pickDistance = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "pickdistance", 0.0f));
            
            isLinkable          = ToolBox.GetAttributeBool(element, "linkable", false);

            resizeHorizontal    = ToolBox.GetAttributeBool(element, "resizehorizontal", false);
            resizeVertical      = ToolBox.GetAttributeBool(element, "resizevertical", false);

            focusOnSelected     = ToolBox.GetAttributeBool(element, "focusonselected", false);

            offsetOnSelected    = ToolBox.GetAttributeFloat(element, "offsetonselected", 0.0f);

            price = ToolBox.GetAttributeInt(element, "price", 0);
            
            Triggers            = new List<Rectangle>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        sprite = new Sprite(subElement, Path.GetDirectoryName(filePath));
                        size = sprite.size;
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
