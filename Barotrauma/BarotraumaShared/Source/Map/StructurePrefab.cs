using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public XElement ConfigElement { get; private set; }

        private bool canSpriteFlipX, canSpriteFlipY;

        private float health;
        
        //default size
        private Vector2 size;
        
        //does the structure have a physics body
        [Serialize(false, false)]
        public bool Body
        {
            get;
            private set;
        }

        //rotation of the physics body in degrees
        [Serialize(0.0f, false)]
        public float BodyRotation
        {
            get;
            private set;
        }
        
        //in display units
        [Serialize(0.0f, false)]
        public float BodyWidth
        {
            get;
            private set;
        }

        //in display units
        [Serialize(0.0f, false)]
        public float BodyHeight
        {
            get;
            private set;
        }

        //in display units
        [Serialize("0.0,0.0", false)]
        public Vector2 BodyOffset
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool Platform
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool AllowAttachItems
        {
            get;
            private set;
        }

        [Serialize(100.0f, false)]
        public float Health
        {
            get { return health; }
            set { health = Math.Max(value, 0.0f); }
        }

        [Serialize(false, false)]
        public bool CastShadow
        {
            get;
            private set;
        }

        /// <summary>
        /// If null, the orientation is determined automatically based on the dimensions of the structure instances
        /// </summary>
        public bool? IsHorizontal
        {
            get;
            private set;
        }

        [Serialize(Direction.None, false)]
        public Direction StairDirection
        {
            get;
            private set;
        }

        [Serialize(45.0f, false)]
        public float StairAngle
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool NoAITarget
        {
            get;
            private set;
        }

        public bool CanSpriteFlipX
        {
            get { return canSpriteFlipX; }
        }

        public bool CanSpriteFlipY
        {
            get { return canSpriteFlipY; }
        }

        [Serialize("0,0", true)]
        public Vector2 Size
        {
            get { return size; }
            private set { size = value; }
        }

        public Vector2 ScaledSize => size * Scale;

        protected Vector2 textureScale = Vector2.One;
        [Editable(DecimalCount = 3), Serialize("1.0, 1.0", true)]
        public Vector2 TextureScale
        {
            get { return textureScale; }
            set
            {
                textureScale = new Vector2(
                    MathHelper.Clamp(value.X, 0.01f, 10),
                    MathHelper.Clamp(value.Y, 0.01f, 10));
            }
        }

        public Sprite BackgroundSprite
        {
            get;
            private set;
        }
        
        public static void LoadAll(IEnumerable<string> filePaths)
        {            
            foreach (string filePath in filePaths)
            {
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null) { return; }
                var rootElement = doc.Root;
                if (rootElement.IsOverride())
                {
                    foreach (var element in rootElement.Elements())
                    {
                        foreach (var childElement in element.Elements())
                        {
                            Load(childElement, true);
                        }
                    }
                }
                else
                {
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            foreach (var childElement in element.Elements())
                            {
                                Load(childElement, true);
                            }
                        }
                        else
                        {
                            Load(element, false);
                        }
                    }
                }
            }
        }
        
        public static StructurePrefab Load(XElement element, bool allowOverride)
        {
            StructurePrefab sp = new StructurePrefab
            {
                name = element.GetAttributeString("name", "")
            };
            sp.ConfigElement = element;
            if (string.IsNullOrEmpty(sp.name)) sp.name = element.Name.ToString();
            sp.identifier = element.GetAttributeString("identifier", "");
            if (string.IsNullOrEmpty(sp.name))
            {
                sp.name = TextManager.Get("EntityName." + sp.identifier, returnNull: true) ?? $"Not defined ({sp.identifier})";
            }
            sp.Tags = new HashSet<string>();
            string joinedTags = element.GetAttributeString("tags", "");
            if (string.IsNullOrEmpty(joinedTags)) joinedTags = element.GetAttributeString("Tags", "");
            foreach (string tag in joinedTags.Split(','))
            {
                sp.Tags.Add(tag.Trim().ToLowerInvariant());
            }

            if (element.Attribute("ishorizontal") != null)
            {
                sp.IsHorizontal = element.GetAttributeBool("ishorizontal", false);
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "sprite":
                        sp.sprite = new Sprite(subElement, lazyLoad: true);
                        if (subElement.Attribute("sourcerect") == null)
                        {
                            DebugConsole.ThrowError("Warning - sprite sourcerect not configured for structure \"" + sp.name + "\"!");
                        }

                        if (subElement.GetAttributeBool("fliphorizontal", false)) 
                            sp.sprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false)) 
                            sp.sprite.effects = SpriteEffects.FlipVertically;
                        
                        sp.canSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        sp.canSpriteFlipY = subElement.GetAttributeBool("canflipy", true);
                        
                        if (subElement.Attribute("name") == null && !string.IsNullOrWhiteSpace(sp.Name))
                        {
                            sp.sprite.Name = sp.Name;
                        }
                        sp.sprite.EntityID = sp.identifier;
                        break;
                    case "backgroundsprite":
                        sp.BackgroundSprite = new Sprite(subElement, lazyLoad: true);
                        if (subElement.Attribute("sourcerect") == null && sp.sprite != null)
                        {
                            sp.BackgroundSprite.SourceRect = sp.sprite.SourceRect;
                            sp.BackgroundSprite.size = sp.sprite.size;
                            sp.BackgroundSprite.size.X *= sp.sprite.SourceRect.Width;
                            sp.BackgroundSprite.size.Y *= sp.sprite.SourceRect.Height;
                            sp.BackgroundSprite.RelativeOrigin = subElement.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
                        }
                        if (subElement.GetAttributeBool("fliphorizontal", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false)) 
                            sp.BackgroundSprite.effects = SpriteEffects.FlipVertically;

                        break;
                }
            }

            if (!Enum.TryParse(element.GetAttributeString("category", "Structure"), true, out MapEntityCategory category))
            {
                category = MapEntityCategory.Structure;
            }
            sp.Category = category;

            sp.Aliases = 
                (element.GetAttributeStringArray("aliases", null) ?? 
                element.GetAttributeStringArray("Aliases", new string[0])).ToHashSet();

            string nonTranslatedName = element.GetAttributeString("name", null) ?? element.Name.ToString();
            sp.Aliases.Add(nonTranslatedName.ToLowerInvariant());

            SerializableProperty.DeserializeProperties(sp, element);
            if (sp.Body)
            {
                sp.Tags.Add("wall");
            }
            string translatedDescription = TextManager.Get("EntityDescription." + sp.identifier, true);
            if (!string.IsNullOrEmpty(translatedDescription)) sp.Description = translatedDescription;

            //backwards compatibility
            if (element.Attribute("size") == null)
            {
                sp.size = Vector2.Zero;
                if (element.Attribute("width") == null && element.Attribute("height") == null)
                {
                    sp.size.X = sp.sprite.SourceRect.Width;
                    sp.size.Y = sp.sprite.SourceRect.Height;
                }
                else
                {
                    sp.size.X = element.GetAttributeFloat("width", 0.0f);
                    sp.size.Y = element.GetAttributeFloat("height", 0.0f);
                }
            }

            if (!category.HasFlag(MapEntityCategory.Legacy) && string.IsNullOrEmpty(sp.identifier))
            {
                DebugConsole.ThrowError(
                    "Structure prefab \"" + sp.name + "\" has no identifier. All structure prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }
            if (sp.HandleExisting(sp.Identifier, allowOverride))
            {
                List.Add(sp);
            }
            return sp;
        }

        public override void UpdatePlacing(Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Vector2 size = ScaledSize;
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

                //don't allow resizing width/height to less than the grid size
                if (ResizeHorizontal && Math.Abs(placeSize.X) < Submarine.GridSize.X)
                {
                    placeSize.X = Submarine.GridSize.X;
                }
                if (ResizeVertical && Math.Abs(placeSize.Y) < Submarine.GridSize.Y)
                {
                    placeSize.Y = Submarine.GridSize.Y;
                }

                newRect = Submarine.AbsRect(placePosition, placeSize);
                if (PlayerInput.LeftButtonReleased())
                {                    
                    newRect.Location -= MathUtils.ToPoint(Submarine.MainSub.Position);
                    var structure = new Structure(newRect, this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };                    

                    selected = null;
                    return;
                }
            }
            
            if (PlayerInput.RightButtonHeld()) selected = null;
        }
    }
}
