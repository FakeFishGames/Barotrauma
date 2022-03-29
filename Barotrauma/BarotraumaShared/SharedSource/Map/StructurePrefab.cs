using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.IO;
using System.Collections.Immutable;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public static readonly PrefabCollection<StructurePrefab> Prefabs = new PrefabCollection<StructurePrefab>();

        public override LocalizedString Name { get; }

        public readonly ContentXElement ConfigElement;

        public readonly bool CanSpriteFlipX;
        public readonly bool CanSpriteFlipY;

        /// <summary>
        /// If null, the orientation is determined automatically based on the dimensions of the structure instances
        /// </summary>
        public readonly bool? IsHorizontal;

        public Vector2 ScaledSize => Size * Scale;

        public readonly Sprite BackgroundSprite;

        public override Sprite Sprite { get; }

        public override string OriginalName { get; }

        public override ImmutableHashSet<Identifier> Tags { get; }

        public override ImmutableHashSet<Identifier> AllowedLinks { get; } 

        public override MapEntityCategory Category { get; }

        public override ImmutableHashSet<string> Aliases { get; }

        //does the structure have a physics body
        [Serialize(false, IsPropertySaveable.No)]
        public bool Body { get; private set; }

        //rotation of the physics body in degrees
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float BodyRotation { get; private set; }
        
        //in display units
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float BodyWidth { get; private set; }

        //in display units
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float BodyHeight { get; private set; }

        //in display units
        [Serialize("0.0,0.0", IsPropertySaveable.No)]
        public Vector2 BodyOffset { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool Platform { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowAttachItems { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float MinHealth { get; private set; }

        private float health;
        [Serialize(100.0f, IsPropertySaveable.No)]
        public float Health
        {
            get { return health; }
            private set { health = Math.Max(value, MinHealth); }
        }

        [Serialize(true, IsPropertySaveable.No)]
        public bool IndestructibleInOutposts { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool CastShadow { get; private set; }

        [Serialize(Direction.None, IsPropertySaveable.No)]
        public Direction StairDirection { get; private set; }

        [Serialize(45.0f, IsPropertySaveable.No)]
        public float StairAngle { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool NoAITarget { get; private set; }

        [Serialize("0,0", IsPropertySaveable.Yes)]
        public Vector2 Size { get; private set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string DamageSound { get; private set; }

        protected Vector2 textureScale = Vector2.One;
        [Editable(DecimalCount = 3), Serialize("1.0, 1.0", IsPropertySaveable.Yes)]
        public Vector2 TextureScale
        {
            get { return textureScale; }
            private set
            {
                textureScale = new Vector2(
                    MathHelper.Clamp(value.X, 0.01f, 10),
                    MathHelper.Clamp(value.Y, 0.01f, 10));
            }
        }

        protected override Identifier DetermineIdentifier(XElement element)
        {
            Identifier identifier = base.DetermineIdentifier(element);
            string originalName = element.GetAttributeString("name", "");
            if (identifier.IsEmpty && !string.IsNullOrEmpty(originalName))
            {
                string categoryStr = element.GetAttributeString("category", "Misc");
                if (Enum.TryParse(categoryStr, true, out MapEntityCategory category) && category.HasFlag(MapEntityCategory.Legacy))
                {
                    identifier = $"legacystructure_{originalName.Replace(" ", "")}".ToIdentifier();
                }
            }
            return identifier;
        }

        public StructurePrefab(ContentXElement element, StructureFile file) : base(element, file)
        {
            OriginalName = element.GetAttributeString("name", "");
            ConfigElement = element;

            var parentType = element.Parent?.GetAttributeIdentifier("prefabtype", Identifier.Empty) ?? Identifier.Empty;

            Identifier nameIdentifier = element.GetAttributeIdentifier("nameidentifier", "");

            //only used if the item doesn't have a name/description defined in the currently selected language
            Identifier fallbackNameIdentifier = element.GetAttributeIdentifier("fallbacknameidentifier", "");

            Identifier descriptionIdentifier = element.GetAttributeIdentifier("descriptionidentifier", "");

            Name = TextManager.Get(nameIdentifier.IsEmpty
                    ? $"EntityName.{Identifier}"
                    : $"EntityName.{nameIdentifier}",
                $"EntityName.{fallbackNameIdentifier}");

            if (parentType == "wrecked")
            {
                Name = TextManager.GetWithVariable("wreckeditemformat", "[name]", Name);
            }

            var tags = new HashSet<Identifier>();
            string joinedTags = element.GetAttributeString("tags", "");
            if (string.IsNullOrEmpty(joinedTags)) joinedTags = element.GetAttributeString("Tags", "");
            foreach (string tag in joinedTags.Split(','))
            {
                tags.Add(tag.Trim().ToIdentifier());
            }

            if (element.Attribute("ishorizontal") != null)
            {
                IsHorizontal = element.GetAttributeBool("ishorizontal", false);
            }

#if CLIENT
            var decorativeSprites = new List<DecorativeSprite>();
            var decorativeSpriteGroups = new Dictionary<int, List<DecorativeSprite>>();
#endif
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, lazyLoad: true);
                        if (subElement.Attribute("sourcerect") == null &&
                            subElement.Attribute("sheetindex") == null)
                        {
                            DebugConsole.ThrowError("Warning - sprite sourcerect not configured for structure \"" + Name + "\"!");
                        }
#if CLIENT
                        if (subElement.GetAttributeBool("fliphorizontal", false))
                            Sprite.effects = SpriteEffects.FlipHorizontally;
                        if (subElement.GetAttributeBool("flipvertical", false))
                            Sprite.effects = SpriteEffects.FlipVertically;
#endif
                        CanSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        CanSpriteFlipY = subElement.GetAttributeBool("canflipy", true);

                        if (subElement.Attribute("name") == null && !Name.IsNullOrWhiteSpace())
                        {
                            Sprite.Name = Name.Value;
                        }
                        Sprite.EntityIdentifier = Identifier;
                        break;
                    case "backgroundsprite":
                        BackgroundSprite = new Sprite(subElement, lazyLoad: true);
                        if (subElement.Attribute("sourcerect") == null && Sprite != null)
                        {
                            BackgroundSprite.SourceRect = Sprite.SourceRect;
                            BackgroundSprite.size = Sprite.size;
                            BackgroundSprite.size.X *= Sprite.SourceRect.Width;
                            BackgroundSprite.size.Y *= Sprite.SourceRect.Height;
                            BackgroundSprite.RelativeOrigin = subElement.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
                        }
#if CLIENT
                        if (subElement.GetAttributeBool("fliphorizontal", false)) { BackgroundSprite.effects = SpriteEffects.FlipHorizontally; }
                        if (subElement.GetAttributeBool("flipvertical", false)) { BackgroundSprite.effects = SpriteEffects.FlipVertically; }
                        BackgroundSpriteColor = subElement.GetAttributeColor("color", Color.White);
#endif
                        break;
                    case "decorativesprite":
#if CLIENT
                        string decorativeSpriteFolder = "";
                        if (subElement.DoesAttributeReferenceFileNameAlone("texture"))
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
                            decorativeSprites.Add(decorativeSprite);
                            groupID = decorativeSprite.RandomGroupID;
                        }
                        if (!decorativeSpriteGroups.ContainsKey(groupID))
                        {
                            decorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                        }
                        decorativeSpriteGroups[groupID].Add(decorativeSprite);
#endif
                        break;
                }
            }
#if CLIENT
            DecorativeSprites = decorativeSprites.ToImmutableArray();
            DecorativeSpriteGroups = decorativeSpriteGroups.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
#endif

            string categoryStr = element.GetAttributeString("category", "Structure");
            if (!Enum.TryParse(categoryStr, true, out MapEntityCategory category))
            {
                category = MapEntityCategory.Structure;
            }
            Category = category;

            Aliases =
                (element.GetAttributeStringArray("aliases", null, convertToLowerInvariant: true) ??
                element.GetAttributeStringArray("Aliases", Array.Empty<string>(), convertToLowerInvariant: true)).ToImmutableHashSet();

            string nonTranslatedName = element.GetAttributeString("name", null) ?? element.Name.ToString();
            Aliases.Add(nonTranslatedName.ToLowerInvariant());

            SerializableProperty.DeserializeProperties(this, element);
            if (Body)
            {
                tags.Add("wall".ToIdentifier());
            }

            if (Description.IsNullOrEmpty())
            {
                if (!descriptionIdentifier.IsEmpty)
                {
                    Description = TextManager.Get($"EntityDescription.{descriptionIdentifier}");
                }
                else if (nameIdentifier.IsEmpty)
                {
                    Description = TextManager.Get($"EntityDescription.{Identifier}");
                }
                else
                {
                    Description = TextManager.Get($"EntityDescription.{nameIdentifier}");
                }
            }

            //backwards compatibility
            if (element.Attribute("size") == null)
            {
                Size = Vector2.Zero;
                if (element.Attribute("width") == null && element.Attribute("height") == null)
                {
                    Size = Sprite.SourceRect.Size.ToVector2();
                }
                else
                {
                    Size = new Vector2(
                        element.GetAttributeFloat("width", 0.0f),
                        element.GetAttributeFloat("height", 0.0f));
                }
            }

            //backwards compatibility
            if (categoryStr.Equals("Thalamus", StringComparison.OrdinalIgnoreCase))
            {
                Category = MapEntityCategory.Wrecked;
                Subcategory = "Thalamus";
            }

            if (Identifier == Identifier.Empty)
            {
                DebugConsole.ThrowError(
                    "Structure prefab \"" + Name + "\" has no identifier. All structure prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }
#if DEBUG
            if (!Category.HasFlag(MapEntityCategory.Legacy) && !HideInMenus)
            {
                if (!string.IsNullOrEmpty(OriginalName))
                {
                    DebugConsole.AddWarning($"Structure \"{(Identifier == Identifier.Empty ? Name : Identifier.Value)}\" has a hard-coded name, and won't be localized to other languages.");
                }
            }
#endif

            Tags = tags.ToImmutableHashSet();
            AllowedLinks = Enumerable.Empty<Identifier>().ToImmutableHashSet();
        }

        protected override void CreateInstance(Rectangle rect)
        {
            throw new NotImplementedException();
        }

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }
    }
}
