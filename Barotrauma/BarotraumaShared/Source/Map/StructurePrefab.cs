using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        //does the structure have a physics body
        private bool hasBody;

        private bool castShadow;

        private bool isPlatform;
        private Direction stairDirection;
        private bool canSpriteFlipX;

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

        public bool CanSpriteFlipX
        {
            get { return canSpriteFlipX; }
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
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement el in doc.Root.Elements())
                {        
                    StructurePrefab sp = Load(el);
                    
                    List.Add(sp);
                }
            }
        }
        
        public static StructurePrefab Load(XElement element)
        {
            StructurePrefab sp = new StructurePrefab();
            sp.name = element.Name.ToString();
            
            sp.Tags = new List<string>();
            sp.Tags.AddRange(element.GetAttributeString("tags", "").Split(','));

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "sprite":
                        sp.sprite = new Sprite(subElement);

                        if (subElement.GetAttributeBool("fliphorizontal", false)) 
                            sp.sprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false)) 
                            sp.sprite.effects = SpriteEffects.FlipVertically;
                        
                        sp.canSpriteFlipX = subElement.GetAttributeBool("canflipx", true);

                        break;
                    case "backgroundsprite":
                        sp.BackgroundSprite = new Sprite(subElement);

                        if (subElement.GetAttributeBool("fliphorizontal", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipVertically;

                        break;
                }
            }

            MapEntityCategory category;

            if (!Enum.TryParse(element.GetAttributeString("category", "Structure"), true, out category))
            {
                category = MapEntityCategory.Structure;
            }

            sp.Category = category;

            sp.Description = element.GetAttributeString("description", "");
            
            string aliases = element.GetAttributeString("aliases", "");
            if (!string.IsNullOrWhiteSpace(aliases))
            {
                sp.Aliases = aliases.Split(',');
            }

            sp.size = Vector2.Zero;
            sp.size.X = element.GetAttributeFloat("width", 0.0f);
            sp.size.Y = element.GetAttributeFloat("height", 0.0f);
            
            string spriteColorStr = element.GetAttributeString("spritecolor", "1.0,1.0,1.0,1.0");
            sp.SpriteColor = new Color(XMLExtensions.ParseVector4(spriteColorStr));

            sp.maxHealth = element.GetAttributeFloat("health", 100.0f);

            sp.ResizeHorizontal = element.GetAttributeBool("resizehorizontal", false);
            sp.ResizeVertical = element.GetAttributeBool("resizevertical", false);
            
            sp.isPlatform = element.GetAttributeBool("platform", false);
            sp.stairDirection = (Direction)Enum.Parse(typeof(Direction), element.GetAttributeString("stairdirection", "None"), true);

            sp.castShadow = element.GetAttributeBool("castshadow", false); 
            
            sp.hasBody = element.GetAttributeBool("body", false); 
            
            return sp;
        }

        public override void UpdatePlacing(Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            
            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.LeftButtonHeld())
                    placePosition = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;
            }
            else
            {
                Vector2 placeSize = size;
                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                newRect = Submarine.AbsRect(placePosition, placeSize);

                if (PlayerInput.LeftButtonReleased())
                {
                    //don't allow resizing width/height to zero
                   if ((!ResizeHorizontal || placeSize.X != 0.0f) && (!ResizeVertical || placeSize.Y != 0.0f))
                    {
                        newRect.Location -= MathUtils.ToPoint(Submarine.MainSub.Position);

                        var structure = new Structure(newRect, this, Submarine.MainSub);
                        structure.Submarine = Submarine.MainSub;
                    }

                    selected = null;
                    return;
                }
            }
            
            if (PlayerInput.RightButtonHeld()) selected = null;
        }
    }
}
