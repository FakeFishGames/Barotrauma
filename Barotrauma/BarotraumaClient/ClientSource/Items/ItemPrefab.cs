using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class BrokenItemSprite
    {
        //sprite will be rendered if the condition of the item is below this
        public readonly float MaxCondition;
        public readonly Sprite Sprite;
        public readonly bool FadeIn;
        public readonly Point Offset;

        public BrokenItemSprite(Sprite sprite, float maxCondition, bool fadeIn, Point offset)
        {
            Sprite = sprite;
            MaxCondition = MathHelper.Clamp(maxCondition, 0.0f, 100.0f);
            FadeIn = fadeIn;
            Offset = offset;
        }
    }

    class ContainedItemSprite
    {
        public readonly Sprite Sprite;
        public readonly bool UseWhenAttached;
        public readonly string[] AllowedContainerIdentifiers;
        public readonly string[] AllowedContainerTags;

        public ContainedItemSprite(XElement element, string path = "", bool lazyLoad = false)
        {
            Sprite = new Sprite(element, path, lazyLoad: lazyLoad);
            UseWhenAttached = element.GetAttributeBool("usewhenattached", false);
            AllowedContainerIdentifiers = element.GetAttributeStringArray("allowedcontaineridentifiers", new string[0], convertToLowerInvariant: true);
            AllowedContainerTags = element.GetAttributeStringArray("allowedcontainertags", new string[0], convertToLowerInvariant: true);
        }

        public bool MatchesContainer(Item container)
        {
            if (container == null) { return false; }
            return AllowedContainerIdentifiers.Contains(container.prefab.Identifier) ||
                AllowedContainerTags.Any(t => container.prefab.Tags.HasTag(t));
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        public List<BrokenItemSprite> BrokenSprites = new List<BrokenItemSprite>();
        public List<DecorativeSprite> DecorativeSprites = new List<DecorativeSprite>();
        public List<ContainedItemSprite> ContainedSprites = new List<ContainedItemSprite>();
        public Dictionary<int, List<DecorativeSprite>> DecorativeSpriteGroups = new Dictionary<int, List<DecorativeSprite>>();
        public Sprite InventoryIcon;

        //only used to display correct color in the sub editor, item instances have their own property that can be edited on a per-item basis
        [Serialize("1.0,1.0,1.0,1.0", false)]
        public Color InventoryIconColor
        {
            get;
            protected set;
        }


        [Serialize("", false)]
        public string ImpactSoundTag { get; private set; }

        public override void UpdatePlacing(Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            
            if (PlayerInput.SecondaryMouseButtonClicked())
            {
                selected = null;
                return;
            }
            
            var potentialContainer = MapEntity.GetPotentialContainer(position);

            if (!ResizeHorizontal && !ResizeVertical)
            {
                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    var item = new Item(new Rectangle((int)position.X, (int)position.Y, (int)(sprite.size.X * Scale), (int)(sprite.size.Y * Scale)), this, Submarine.MainSub)
                    {
                        Submarine = Submarine.MainSub
                    };
                    item.SetTransform(ConvertUnits.ToSimUnits(Submarine.MainSub == null ? item.Position : item.Position - Submarine.MainSub.Position), 0.0f);
                    item.FindHull();

                    if (PlayerInput.IsShiftDown())
                    {
                        if (potentialContainer?.OwnInventory?.TryPutItem(item, Character.Controlled) ?? false)
                        {
                            GUI.PlayUISound(GUISoundType.PickItem);
                        }
                    }

                    placePosition = Vector2.Zero;
                    return;
                }
            }
            else
            {
                Vector2 placeSize = size * Scale;

                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.PrimaryMouseButtonHeld()) placePosition = position;
                }
                else
                {
                    if (ResizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (ResizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, size.Y);

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
                selected = null;
                return;
            }

            if (!ResizeHorizontal && !ResizeVertical)
            {
                sprite.Draw(spriteBatch, new Vector2(position.X, -position.Y) + sprite.size / 2.0f * Scale, SpriteColor, scale: Scale);
            }
            else
            {
                Vector2 placeSize = size * Scale;
                if (placePosition != Vector2.Zero)
                {
                    if (ResizeHorizontal) { placeSize.X = Math.Max(position.X - placePosition.X, placeSize.X); }
                    if (ResizeVertical) { placeSize.Y = Math.Max(placePosition.Y - position.Y, placeSize.Y); }
                    position = placePosition;
                }
                sprite?.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, color: SpriteColor);
            }
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Rectangle placeRect, float scale = 1.0f, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (!ResizeHorizontal && !ResizeVertical)
            {
                sprite.Draw(spriteBatch, new Vector2(placeRect.Center.X, -(placeRect.Y - placeRect.Height / 2)), SpriteColor * 0.8f, scale: scale);
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(Screen.Selected.Cam, Submarine.MainSub);
                Vector2 placeSize = size * Scale;
                if (placePosition != Vector2.Zero)
                {
                    if (ResizeHorizontal) { placeSize.X = Math.Max(position.X - placePosition.X, placeSize.X); }
                    if (ResizeVertical) { placeSize.Y = Math.Max(placePosition.Y - position.Y, placeSize.Y); }
                    position = placePosition;
                }
                sprite?.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, color: SpriteColor);
            }
        }
    }
}
