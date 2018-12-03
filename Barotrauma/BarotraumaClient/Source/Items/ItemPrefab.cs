using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class BrokenItemSprite
    {
        //sprite will be rendered if the condition of the item is below this
        public readonly float MaxCondition;
        public readonly Sprite Sprite;
        public readonly bool FadeIn;

        public BrokenItemSprite(Sprite sprite, float maxCondition, bool fadeIn)
        {
            Sprite = sprite;
            MaxCondition = MathHelper.Clamp(maxCondition, 0.0f, 100.0f);
            FadeIn = fadeIn;
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        public List<BrokenItemSprite> BrokenSprites = new List<BrokenItemSprite>();
        public List<Sprite> DecorativeSprites = new List<Sprite>();
        public Sprite InventoryIcon;

        //only used to display correct color in the sub editor, item instances have their own property that can be edited on a per-item basis
        [Serialize("1.0,1.0,1.0,1.0", false)]
        public Color InventoryIconColor
        {
            get;
            protected set;
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam, Rectangle? placeRect = null)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

            if (PlayerInput.RightButtonClicked())
            {
                selected = null;
                return;
            }

            if (!ResizeHorizontal && !ResizeVertical)
            {
                if (placeRect.HasValue)
                {
                    sprite.Draw(spriteBatch, new Vector2(placeRect.Value.Center.X, -(placeRect.Value.Y - placeRect.Value.Height / 2)), SpriteColor);
                }
                else
                {
                    sprite.Draw(spriteBatch, new Vector2(position.X, -position.Y) + sprite.size / 2.0f * Scale, SpriteColor, scale: Scale);
                }
            }
            else if (placeRect.HasValue)
            {
                if (sprite != null) sprite.DrawTiled(spriteBatch, new Vector2(placeRect.Value.X, -placeRect.Value.Y), placeRect.Value.Size.ToVector2(), null, SpriteColor);
            }
            else
            {
                Vector2 placeSize = size;
                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.LeftButtonHeld()) placePosition = position;
                }
                else
                {
                    if (ResizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (ResizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, size.Y);

                    position = placePosition;
                }

                if (sprite != null) sprite.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, color: SpriteColor);
            }
        }
    }
}
