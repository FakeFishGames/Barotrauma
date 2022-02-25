using Barotrauma.IO;
using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class BrokenItemSprite
    {
        //sprite will be rendered if the condition of the item is below this
        public readonly float MaxConditionPercentage;
        public readonly Sprite Sprite;
        public readonly bool FadeIn;
        public readonly Point Offset;

        public BrokenItemSprite(Sprite sprite, float maxCondition, bool fadeIn, Point offset)
        {
            Sprite = sprite;
            MaxConditionPercentage = MathHelper.Clamp(maxCondition, 0.0f, 100.0f);
            FadeIn = fadeIn;
            Offset = offset;
        }
    }

    class ContainedItemSprite
    {
        public enum DecorativeSpriteBehaviorType
        {
            None, HideWhenVisible, HideWhenNotVisible
        }

        public readonly Sprite Sprite;
        public readonly bool UseWhenAttached;
        public readonly DecorativeSpriteBehaviorType DecorativeSpriteBehavior;
        public readonly ImmutableHashSet<Identifier> AllowedContainerIdentifiers;
        public readonly ImmutableHashSet<Identifier> AllowedContainerTags;

        public ContainedItemSprite(ContentXElement element, string path = "", bool lazyLoad = false)
        {
            Sprite = new Sprite(element, path, lazyLoad: lazyLoad);
            UseWhenAttached = element.GetAttributeBool("usewhenattached", false);
            Enum.TryParse(element.GetAttributeString("decorativespritebehavior", "None"), ignoreCase: true, out DecorativeSpriteBehavior);
            AllowedContainerIdentifiers = element.GetAttributeIdentifierArray("allowedcontaineridentifiers", Array.Empty<Identifier>()).ToImmutableHashSet();
            AllowedContainerTags = element.GetAttributeIdentifierArray("allowedcontainertags", Array.Empty<Identifier>()).ToImmutableHashSet();
        }

        public bool MatchesContainer(Item container)
        {
            if (container == null) { return false; }
            return AllowedContainerIdentifiers.Contains(container.Prefab.Identifier) ||
                AllowedContainerTags.Any(t => container.Prefab.Tags.Contains(t));
        }
    }

    partial class ItemPrefab : MapEntityPrefab, IImplementsVariants<ItemPrefab>
    {
        public ImmutableDictionary<Identifier, ImmutableArray<DecorativeSprite>> UpgradeOverrideSprites { get; private set; }
        public ImmutableArray<BrokenItemSprite> BrokenSprites { get; private set; }
        public ImmutableArray<DecorativeSprite> DecorativeSprites { get; private set; }
        public ImmutableArray<ContainedItemSprite> ContainedSprites { get; private set; }
        public ImmutableDictionary<int, ImmutableArray<DecorativeSprite>> DecorativeSpriteGroups { get; private set; }
        public Sprite InventoryIcon { get; private set; }
        public Sprite MinimapIcon { get; private set; }
        public Sprite UpgradePreviewSprite { get; private set; }
        public Sprite InfectedSprite { get; private set; }
        public Sprite DamagedInfectedSprite { get; private set; }

        public float UpgradePreviewScale = 1.0f;

        //only used to display correct color in the sub editor, item instances have their own property that can be edited on a per-item basis
        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.No)]
        public Color InventoryIconColor { get; protected set; }

        [Serialize("", IsPropertySaveable.No)]
        public string ImpactSoundTag { get; private set; }
        
        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowInStatusMonitor
        {
            get;
            private set;
        }
        private void ParseSubElementsClient(ContentXElement element, ItemPrefab variantOf)
        {
            UpgradePreviewSprite = null;
            UpgradePreviewScale = 1f;
            InventoryIcon = null;
            MinimapIcon = null;
            InfectedSprite = null;
            DamagedInfectedSprite = null;

            var upgradeOverrideSprites = new Dictionary<Identifier, List<DecorativeSprite>>();
            var brokenSprites = new List<BrokenItemSprite>();
            var decorativeSprites = new List<DecorativeSprite>();
            var containedSprites = new List<ContainedItemSprite>();
            var decorativeSpriteGroups = new Dictionary<int, List<DecorativeSprite>>();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "upgradeoverride":
                        {

                            var sprites = new List<DecorativeSprite>();
                            foreach (var decorSprite in subElement.Elements())
                            {
                                if (decorSprite.NameAsIdentifier() == "decorativesprite")
                                {
                                    sprites.Add(new DecorativeSprite(decorSprite));
                                }
                            }
                            upgradeOverrideSprites.Add(subElement.GetAttributeIdentifier("identifier", Identifier.Empty), sprites);
                            break;
                        }
                    case "upgradepreviewsprite":
                        {
                            string iconFolder = GetTexturePath(subElement, variantOf);
                            UpgradePreviewSprite = new Sprite(subElement, iconFolder, lazyLoad: true);
                            UpgradePreviewScale = subElement.GetAttributeFloat("scale", 1.0f);
                        }
                        break;
                    case "inventoryicon":
                        {
                            string iconFolder = GetTexturePath(subElement, variantOf);
                            InventoryIcon = new Sprite(subElement, iconFolder, lazyLoad: true);
                        }
                        break;
                    case "minimapicon":
                        {
                            string iconFolder = GetTexturePath(subElement, variantOf);
                            MinimapIcon = new Sprite(subElement, iconFolder, lazyLoad: true);
                        }
                        break;
                    case "infectedsprite":
                        {
                            string iconFolder = GetTexturePath(subElement, variantOf);

                            InfectedSprite = new Sprite(subElement, iconFolder, lazyLoad: true);
                        }
                        break;
                    case "damagedinfectedsprite":
                        {
                            string iconFolder = GetTexturePath(subElement, variantOf);

                            DamagedInfectedSprite = new Sprite(subElement, iconFolder, lazyLoad: true);
                        }
                        break;
                    case "brokensprite":
                        string brokenSpriteFolder = GetTexturePath(subElement, variantOf);

                        var brokenSprite = new BrokenItemSprite(
                            new Sprite(subElement, brokenSpriteFolder, lazyLoad: true),
                            subElement.GetAttributeFloat("maxcondition", 0.0f),
                            subElement.GetAttributeBool("fadein", false),
                            subElement.GetAttributePoint("offset", Point.Zero));

                        int spriteIndex = 0;
                        for (int i = 0; i < brokenSprites.Count && brokenSprites[i].MaxConditionPercentage < brokenSprite.MaxConditionPercentage; i++)
                        {
                            spriteIndex = i;
                        }
                        brokenSprites.Insert(spriteIndex, brokenSprite);
                        break;
                    case "decorativesprite":
                        string decorativeSpriteFolder = GetTexturePath(subElement, variantOf);

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

                        break;
                    case "containedsprite":
                        string containedSpriteFolder = GetTexturePath(subElement, variantOf);
                        var containedSprite = new ContainedItemSprite(subElement, containedSpriteFolder, lazyLoad: true);
                        if (containedSprite.Sprite != null)
                        {
                            containedSprites.Add(containedSprite);
                        }
                        break;
                }
            }

            UpgradeOverrideSprites = upgradeOverrideSprites.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
            BrokenSprites = brokenSprites.ToImmutableArray();
            DecorativeSprites = decorativeSprites.ToImmutableArray();
            ContainedSprites = containedSprites.ToImmutableArray();
            DecorativeSpriteGroups = decorativeSpriteGroups.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
        }

        public override void UpdatePlacing(Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                Selected = null;
                return;
            }

            var potentialContainer = MapEntity.GetPotentialContainer(position);

            if (!ResizeHorizontal && !ResizeVertical)
            {
                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)(Sprite.size.X * Scale), (int)(Sprite.size.Y * Scale)), this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                    item.FindHull();
                    item.Submarine = Submarine.MainSub;

                    if (PlayerInput.IsShiftDown())
                    {
                        if (potentialContainer?.OwnInventory?.TryPutItem(item, Character.Controlled) ?? false)
                        {
                            SoundPlayer.PlayUISound(GUISoundType.PickItem);
                        }
                    }

                    SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> {item}, false));

                    placePosition = Vector2.Zero;
                    return;
                }
            }
            else
            {
                Vector2 placeSize = Size * Scale;

                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.PrimaryMouseButtonHeld()) placePosition = position;
                }
                else
                {
                    if (ResizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, Size.X);
                    if (ResizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, Size.Y);

                    if (PlayerInput.PrimaryMouseButtonReleased())
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

            if (potentialContainer != null)
            {
                potentialContainer.IsHighlighted = true;
            }


            //if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;

        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                Selected = null;
                return;
            }

            if (!ResizeHorizontal && !ResizeVertical)
            {
                Sprite.Draw(spriteBatch, new Vector2(position.X, -position.Y) + Sprite.size / 2.0f * Scale, SpriteColor, scale: Scale);
            }
            else
            {
                Vector2 placeSize = Size * Scale;
                if (placePosition != Vector2.Zero)
                {
                    if (ResizeHorizontal) { placeSize.X = Math.Max(position.X - placePosition.X, placeSize.X); }
                    if (ResizeVertical) { placeSize.Y = Math.Max(placePosition.Y - position.Y, placeSize.Y); }
                    position = placePosition;
                }
                Sprite?.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, color: SpriteColor);
            }
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Rectangle placeRect, float scale = 1.0f, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (!ResizeHorizontal && !ResizeVertical)
            {
                Sprite.Draw(spriteBatch, new Vector2(placeRect.Center.X, -(placeRect.Y - placeRect.Height / 2)), SpriteColor * 0.8f, scale: scale);
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(Screen.Selected.Cam, Submarine.MainSub);
                Vector2 placeSize = Size * Scale;
                if (placePosition != Vector2.Zero)
                {
                    if (ResizeHorizontal) { placeSize.X = Math.Max(position.X - placePosition.X, placeSize.X); }
                    if (ResizeVertical) { placeSize.Y = Math.Max(placePosition.Y - position.Y, placeSize.Y); }
                    position = placePosition;
                }
                Sprite?.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, color: SpriteColor);
            }
        }
    }
}
