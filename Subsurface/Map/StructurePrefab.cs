using System;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Subsurface
{
    class StructurePrefab : MapEntityPrefab
    {
        //public static List<StructurePrefab> list = new List<StructurePrefab>();

        //does the structure have a physics body
        bool hasBody;

        bool castShadow;

        bool isPlatform;
        Direction stairDirection;

        float maxHealth;
        
        //default size
        Vector2 size;
        
        public bool HasBody
        {
            get { return hasBody; }
        }

        public bool IsPlatform
        {
            get { return isPlatform; }
        }

        public float MaxHealth
        {
            get { return maxHealth; }
        }

        public bool CastShadow
        {
            get { return castShadow; }
        }

        public Direction StairDirection
        {
            get { return stairDirection; }
        }
        
        public static void LoadAll(List<string> filePaths)
        {            
            foreach (string filePath in filePaths)
            {
                XDocument doc = ToolBox.TryLoadXml(filePath);
                if (doc == null) return;

                foreach (XElement el in doc.Root.Elements())
                {        
                    StructurePrefab sp = Load(el);

                    Debug.WriteLine(sp.name);

                    list.Add(sp);
                }
            }
        }

        public static StructurePrefab Load(XElement el)
        {
            StructurePrefab sp = new StructurePrefab();
            sp.name = el.Name.ToString();

            Vector4 sourceVector = ToolBox.GetAttributeVector4(el, "sourcerect", new Vector4(0,0,1,1));
            
            Rectangle sourceRect = new Rectangle(
                (int)sourceVector.X,
                (int)sourceVector.Y,
                (int)sourceVector.Z,
                (int)sourceVector.W);
            
            if (el.Attribute("sprite") != null)
            {
                sp.sprite = new Sprite(el.Attribute("sprite").Value, sourceRect, Vector2.Zero);
                
                sp.sprite.Depth = ToolBox.GetAttributeFloat(el, "depth", 0.0f);

                if (ToolBox.GetAttributeBool(el, "fliphorizontal", false)) sp.sprite.effects = SpriteEffects.FlipHorizontally;
                if (ToolBox.GetAttributeBool(el, "flipvertical", false)) sp.sprite.effects = SpriteEffects.FlipVertically;
            }

            sp.size = Vector2.Zero;
            sp.size.X = ToolBox.GetAttributeFloat(el, "width", 0.0f);
            sp.size.Y = ToolBox.GetAttributeFloat(el, "height", 0.0f);

            sp.maxHealth = ToolBox.GetAttributeFloat(el, "health", 100.0f);

            sp.resizeHorizontal = ToolBox.GetAttributeBool(el, "resizehorizontal", false);
            sp.resizeVertical = ToolBox.GetAttributeBool(el, "resizevertical", false);
            
            sp.isPlatform = ToolBox.GetAttributeBool(el, "platform", false);
            sp.stairDirection = (Direction)Enum.Parse(typeof(Direction), ToolBox.GetAttributeString(el, "stairdirection", "None"));

            sp.castShadow = ToolBox.GetAttributeBool(el, "castshadow", false); 


            sp.hasBody = ToolBox.GetAttributeBool(el, "body", false); 
            
            return sp;
        }

        public override void UpdatePlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam);
            //Vector2 placeSize = size;

            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);


            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                    placePosition = Submarine.MouseToWorldGrid(cam);

                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;

                //sprite.Draw(spriteBatch, new Vector2(position.X, -position.Y), placeSize, Color.White);
            }
            else
            {
                Vector2 placeSize = size;
                if (resizeHorizontal) placeSize.X = position.X - placePosition.X;  
                if (resizeVertical) placeSize.Y = placePosition.Y - position.Y;

                newRect = Submarine.AbsRect(placePosition, placeSize);

                //newRect.Width = (int)Math.Max(newRect.Width, Map.gridSize.X);
                //newRect.Height = (int)Math.Max(newRect.Height, Map.gridSize.Y);

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                {
                    new Structure(newRect, this);
                    selected = null;
                    return;
                }

                //position = placePosition;                
            }

            sprite.DrawTiled(spriteBatch, new Vector2(newRect.X, -newRect.Y), new Vector2(newRect.Width, newRect.Height), Color.White);

            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X - Game1.GraphicsWidth, -newRect.Y, newRect.Width + Game1.GraphicsWidth*2, newRect.Height), Color.White);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X, -newRect.Y - Game1.GraphicsHeight, newRect.Width, newRect.Height + Game1.GraphicsHeight*2), Color.White);
          
            if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;
        }
    }
}
