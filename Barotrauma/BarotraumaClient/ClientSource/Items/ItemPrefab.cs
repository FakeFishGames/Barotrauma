﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

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

        private IReadOnlyList<DamageModifier> wearableDamageModifiers;
        private IReadOnlyDictionary<Identifier, float> wearableSkillModifiers;

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

            var wearableDamageModifiers = new List<DamageModifier>();
            var wearableSkillModifiers = new Dictionary<Identifier, float>();

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
                        if (subElement.GetAttribute("texture") == null)
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
                    case "wearable":
                        foreach (ContentXElement wearableSubElement in subElement.Elements())
                        {
                            switch (wearableSubElement.Name.LocalName.ToLowerInvariant())
                            {
                                case "damagemodifier":
                                    wearableDamageModifiers.Add(new DamageModifier(wearableSubElement, Name.Value + ", Wearable", checkErrors: false));
                                    break;
                                case "skillmodifier":
                                    Identifier skillIdentifier = wearableSubElement.GetAttributeIdentifier("skillidentifier", Identifier.Empty);
                                    float skillValue = wearableSubElement.GetAttributeFloat("skillvalue", 0f);
                                    if (wearableSkillModifiers.ContainsKey(skillIdentifier))
                                    {
                                        wearableSkillModifiers[skillIdentifier] += skillValue;
                                    }
                                    else
                                    {
                                        wearableSkillModifiers.TryAdd(skillIdentifier, skillValue);
                                    }
                                    break;
                            }
                        }
                        break;
                }
            }
            this.wearableDamageModifiers = wearableDamageModifiers.ToImmutableList();
            this.wearableSkillModifiers = wearableSkillModifiers.ToImmutableDictionary();

            UpgradeOverrideSprites = upgradeOverrideSprites.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
            BrokenSprites = brokenSprites.ToImmutableArray();
            DecorativeSprites = decorativeSprites.ToImmutableArray();
            ContainedSprites = containedSprites.ToImmutableArray();
            DecorativeSpriteGroups = decorativeSpriteGroups.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();

#if CLIENT
            foreach (Item item in Item.ItemList)
            {
                if (item.Prefab == this)
                {
                    item.InitSpriteStates();
                }
            }
#endif
        }

        public bool CanCharacterBuy()
        {
            if (DefaultPrice == null) { return false; }
            if (!DefaultPrice.RequiresUnlock) { return true; }
            return Character.Controlled is not null && Character.Controlled.HasStoreAccessForItem(this);
        }
        public LocalizedString GetTooltip(Character character)
        {
            LocalizedString tooltip = $"‖color:{XMLExtensions.ToStringHex(GUIStyle.TextColorBright)}‖{Name}‖color:end‖";
            if (!Description.IsNullOrEmpty())
            {
                tooltip += $"\n{Description}";
            }
            if (wearableDamageModifiers.Any() || wearableSkillModifiers.Any())
            {
                Wearable.AddTooltipInfo(wearableDamageModifiers, wearableSkillModifiers, ref tooltip);
            }
            if (SkillRequirementHints != null && SkillRequirementHints.Any())
            {
                tooltip += GetSkillRequirementHints(character);
            }
            return tooltip;
        }

        public override void UpdatePlacing(Camera cam)
        {

            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                Selected = null;
                return;
            }
            
            var potentialContainer = MapEntity.GetPotentialContainer(cam.ScreenToWorld(PlayerInput.MousePosition));

            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            if (!ResizeHorizontal && !ResizeVertical)
            {
                if (PlayerInput.PrimaryMouseButtonClicked() && GUI.MouseOn == null)
                {
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)(Sprite.size.X * Scale), (int)(Sprite.size.Y * Scale)), this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                    item.GetComponent<Items.Components.Door>()?.RefreshLinkedGap();
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
                    if (PlayerInput.PrimaryMouseButtonHeld() && GUI.MouseOn == null) { placePosition = position; }
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

                        SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { item }, false));

                        return;
                    }
                }
            }

            if (potentialContainer != null)
            {
                potentialContainer.IsHighlighted = true;
            }
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

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
                Sprite.DrawTiled(spriteBatch, new Vector2(placeRect.X, -placeRect.Y), placeRect.Size.ToVector2(), SpriteColor * 0.8f);
            }
        }

        public LocalizedString GetSkillRequirementHints(Character character)
        {
            LocalizedString text = "";
            if (SkillRequirementHints != null && SkillRequirementHints.Any() && character != null)
            {
                Color orange = GUIStyle.Orange;
                // Reuse an existing, localized, text because it's what we want here: "Required skills:"
                text = "\n\n" + $"‖color:{orange.ToStringHex()}‖{TextManager.Get("requiredrepairskills")}‖color:end‖";
                foreach (var hint in SkillRequirementHints)
                {
                    int skillLevel = (int)character.GetSkillLevel(hint.Skill);
                    Color levelColor = GUIStyle.Yellow;
                    if (skillLevel >= hint.Level)
                    {
                        levelColor = GUIStyle.Green;
                    }
                    else if (skillLevel < hint.Level / 2)
                    {
                        levelColor = GUIStyle.Red;
                    }
                    text += "\n" + hint.GetFormattedText(skillLevel, levelColor.ToStringHex());
                }
            }
            return text;
        }
    }
}
