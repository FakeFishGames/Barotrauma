using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.IO;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public static readonly PrefabCollection<StructurePrefab> Prefabs = new PrefabCollection<StructurePrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        private string name;
        public override string Name
        {
            get { return name; }
        }

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

        [Serialize(0.0f, false)]
        public float MinHealth
        {
            get;
            set;
        }

        [Serialize(100.0f, false)]
        public float Health
        {
            get { return health; }
            set { health = Math.Max(value, MinHealth); }
        }

        [Serialize(true, false)]
        public bool IndestructibleInOutposts
        {
            get;
            set;
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
        
        public static void LoadAll(IEnumerable<ContentFile> files)
        {            
            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }
        }
        
        public static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }
            var rootElement = doc.Root;
            if (rootElement.IsOverride())
            {
                foreach (var element in rootElement.Elements())
                {
                    foreach (var childElement in element.Elements())
                    {
                        Load(childElement, true, file);
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
                            Load(childElement, true, file);
                        }
                    }
                    else
                    {
                        Load(element, false, file);
                    }
                }
            }
        }

        public static void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }

        private static StructurePrefab Load(XElement element, bool allowOverride, ContentFile file)
        {
            StructurePrefab sp = new StructurePrefab
            {
                originalName = element.GetAttributeString("name", ""),
                FilePath = file.Path,
                ContentPackage = file.ContentPackage
            };
            sp.name = sp.originalName;
            sp.ConfigElement = element;
            sp.identifier = new StringIdentifier(element.GetAttributeString("identifier", ""));
            
            var parentType = element.Parent?.GetAttributeString("prefabtype", "") ?? string.Empty;
            
            string nameIdentifier = element.GetAttributeString("nameidentifier", "");
            string descriptionIdentifier = element.GetAttributeString("descriptionidentifier", "");

            if (string.IsNullOrEmpty(sp.originalName))
            {
                if (string.IsNullOrEmpty(nameIdentifier))
                {
                    sp.name = TextManager.Get("EntityName." + sp.MapEntityIdentifier.IdentifierString, true) ?? string.Empty;
                }
                else
                {
                    sp.name = TextManager.Get("EntityName." + nameIdentifier, true) ?? string.Empty;
                }
            }
            
            if (string.IsNullOrEmpty(sp.name))
            {
                sp.name = TextManager.Get("EntityName." + sp.MapEntityIdentifier.IdentifierString, returnNull: true) ?? $"Not defined ({sp.MapEntityIdentifier.IdentifierString})";
            }

            string TagsAttribute = element.GetAttributeString("tags", "");
            if (string.IsNullOrEmpty(TagsAttribute))
            {
                TagsAttribute = element.GetAttributeString("Tags", "");
            }

            sp.Tags = new StringTags();
            sp.Tags.AllTagsString = TagsAttribute;

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
#if CLIENT
                        if (subElement.GetAttributeBool("fliphorizontal", false))
                            sp.sprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false))
                            sp.sprite.effects = SpriteEffects.FlipVertically;
#endif
                        sp.canSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        sp.canSpriteFlipY = subElement.GetAttributeBool("canflipy", true);

                        if (subElement.Attribute("name") == null && !string.IsNullOrWhiteSpace(sp.Name))
                        {
                            sp.sprite.Name = sp.Name;
                        }
                        sp.sprite.EntityID = sp.MapEntityIdentifier.IdentifierString;
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
#if CLIENT
                        if (subElement.GetAttributeBool("fliphorizontal", false)) { sp.BackgroundSprite.effects = SpriteEffects.FlipHorizontally; }
                        if (subElement.GetAttributeBool("flipvertical", false)) { sp.BackgroundSprite.effects = SpriteEffects.FlipVertically; }
                        sp.BackgroundSpriteColor = subElement.GetAttributeColor("color", Color.White);
#endif
                        break;
                    case "decorativesprite":
#if CLIENT
                        string decorativeSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            decorativeSpriteFolder = Path.GetDirectoryName(file.Path);
                        }

                        int groupID = 0;
                        DecorativeSprite decorativeSprite = null;
                        if (subElement.Attribute("texture") == null)
                        {
                            groupID = subElement.GetAttributeInt("randomgroupid", 0);
                        }
                        else
                        {
                            decorativeSprite = new DecorativeSprite(subElement, decorativeSpriteFolder, lazyLoad: true);
                            sp.DecorativeSprites.Add(decorativeSprite);
                            groupID = decorativeSprite.RandomGroupID;
                        }
                        if (!sp.DecorativeSpriteGroups.ContainsKey(groupID))
                        {
                            sp.DecorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                        }
                        sp.DecorativeSpriteGroups[groupID].Add(decorativeSprite);
#endif
                        break;
                }
            }
            
            if (string.Equals(parentType, "wrecked", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(sp.Name))
                {
                    sp.name = TextManager.GetWithVariable("wreckeditemformat", "[name]", sp.name);
                }
            }

            if (!Enum.TryParse(element.GetAttributeString("category", "Structure"), true, out MapEntityCategory category))
            {
                category = MapEntityCategory.Structure; 
            }
            sp.Category = category;

            if (category.HasFlag(MapEntityCategory.Legacy))
            {
                if (string.IsNullOrWhiteSpace(sp.MapEntityIdentifier.IdentifierString))
                {
                    sp.identifier = new StringIdentifier("legacystructure_" + sp.name.ToLowerInvariant().Replace(" ", ""));
                }
            }

            sp.Aliases = 
                (element.GetAttributeStringArray("aliases", null) ?? 
                element.GetAttributeStringArray("Aliases", new string[0])).ToHashSet();

            string nonTranslatedName = element.GetAttributeString("name", null) ?? element.Name.ToString();
            sp.Aliases.Add(nonTranslatedName.ToLowerInvariant());

            SerializableProperty.DeserializeProperties(sp, element);
            if (sp.Body)
            {
                sp.Tags.AddTag("wall");
            }

            if (string.IsNullOrEmpty(sp.Description))
            {
                if (!string.IsNullOrEmpty(descriptionIdentifier))
                {
                    sp.Description = TextManager.Get("EntityDescription." + descriptionIdentifier, returnNull: true) ?? string.Empty;
                }
                else  if (string.IsNullOrEmpty(nameIdentifier))
                {
                    sp.Description = TextManager.Get("EntityDescription." + sp.MapEntityIdentifier.IdentifierString, returnNull: true) ?? string.Empty;
                }
                else
                {
                    sp.Description = TextManager.Get("EntityDescription." + nameIdentifier, true) ?? string.Empty;
                }
            }

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

            if (string.IsNullOrEmpty(sp.MapEntityIdentifier.IdentifierString))
            {
                DebugConsole.ThrowError(
                    "Structure prefab \"" + sp.name + "\" has no identifier. All structure prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }
            Prefabs.Add(sp, allowOverride);
            return sp;
        }
    }
}
