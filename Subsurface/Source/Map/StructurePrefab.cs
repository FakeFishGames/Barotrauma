using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    class StructurePrefab : MapEntityPrefab
    {
        //public static List<StructurePrefab> list = new List<StructurePrefab>();

        //does the structure have a physics body
        private bool hasBody;

        private bool castShadow;

        private bool isPlatform;
        private Direction stairDirection;

        private float maxHealth;
        
        //default size
        private Vector2 size;
        
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

        public Vector2 Size
        {
            get { return size; }
        }

        public Sprite BackgroundSprite
        {
            get;
            private set;
        }
        
        public static void LoadAll(List<string> filePaths)
        {            
            foreach (string filePath in filePaths)
            {
                XDocument doc = ToolBox.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement el in doc.Root.Elements())
                {        
                    StructurePrefab sp = Load(el);
                    
                    list.Add(sp);
                }
            }
        }
        
        public static StructurePrefab Load(XElement element)
        {
            StructurePrefab sp = new StructurePrefab();
            sp.name = element.Name.ToString();

            //Vector4 sourceVector = ToolBox.GetAttributeVector4(element, "sourcerect", new Vector4(0,0,1,1));
            
            //Rectangle sourceRect = new Rectangle(
            //    (int)sourceVector.X,
            //    (int)sourceVector.Y,
            //    (int)sourceVector.Z,
            //    (int)sourceVector.W);
            
            //if (element.Attribute("sprite") != null)
            //{
            //    sp.sprite = new Sprite(element.Attribute("sprite").Value, sourceRect, Vector2.Zero);
                
            //    sp.sprite.Depth = ToolBox.GetAttributeFloat(element, "depth", 0.0f);

            //}

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "sprite":
                        sp.sprite = new Sprite(subElement);

                        if (ToolBox.GetAttributeBool(subElement, "fliphorizontal", false)) 
                            sp.sprite.effects = SpriteEffects.FlipHorizontally;
                        if (ToolBox.GetAttributeBool(subElement, "flipvertical", false)) 
                            sp.sprite.effects = SpriteEffects.FlipVertically;

                        break;
                    case "backgroundsprite":
                        sp.BackgroundSprite = new Sprite(subElement);

                        if (ToolBox.GetAttributeBool(subElement, "fliphorizontal", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipHorizontally;
                        if (ToolBox.GetAttributeBool(subElement, "flipvertical", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipVertically;

                        break;
                }
            }

            MapEntityCategory category;

            if (!Enum.TryParse(ToolBox.GetAttributeString(element, "category", "Structure"), true, out category))
            {
                category = MapEntityCategory.Structure;
            }

            sp.Category = category;

            sp.Description = ToolBox.GetAttributeString(element, "description", "");

            sp.size = Vector2.Zero;
            sp.size.X = ToolBox.GetAttributeFloat(element, "width", 0.0f);
            sp.size.Y = ToolBox.GetAttributeFloat(element, "height", 0.0f);
            
            string spriteColorStr = ToolBox.GetAttributeString(element, "spritecolor", "1.0,1.0,1.0,1.0");
            sp.SpriteColor = new Color(ToolBox.ParseToVector4(spriteColorStr));

            sp.maxHealth = ToolBox.GetAttributeFloat(element, "health", 100.0f);

            sp.resizeHorizontal = ToolBox.GetAttributeBool(element, "resizehorizontal", false);
            sp.resizeVertical = ToolBox.GetAttributeBool(element, "resizevertical", false);
            
            sp.isPlatform = ToolBox.GetAttributeBool(element, "platform", false);
            sp.stairDirection = (Direction)Enum.Parse(typeof(Direction), ToolBox.GetAttributeString(element, "stairdirection", "None"), true);

            sp.castShadow = ToolBox.GetAttributeBool(element, "castshadow", false); 
            
            sp.hasBody = ToolBox.GetAttributeBool(element, "body", false); 
            
            return sp;
        }

        public override void UpdatePlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            //Vector2 placeSize = size;

            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);


            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.LeftButtonHeld())
                    placePosition = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

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

                if (PlayerInput.LeftButtonReleased())
                {
                    newRect.Location -= Submarine.MainSub.Position.ToPoint();

                    var structure = new Structure(newRect, this, Submarine.MainSub);
                    structure.Submarine = Submarine.MainSub;

                    selected = null;
                    return;
                }
            }

            sprite.DrawTiled(spriteBatch, new Vector2(newRect.X, -newRect.Y), new Vector2(newRect.Width, newRect.Height), Color.White);

            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X - GameMain.GraphicsWidth, -newRect.Y, newRect.Width + GameMain.GraphicsWidth*2, newRect.Height), Color.White);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X, -newRect.Y - GameMain.GraphicsHeight, newRect.Width, newRect.Height + GameMain.GraphicsHeight*2), Color.White);
          
            if (PlayerInput.RightButtonHeld()) selected = null;
        }
    }
}
