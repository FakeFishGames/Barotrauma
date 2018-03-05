using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class ItemContainer : ItemComponent, IDrawableComponent
    {
        //TODO: shouldn't this be overriding the base method?
        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            if (hideItems || (item.body != null && !item.body.Enabled)) return;

            Vector2 transformedItemPos = itemPos;
            Vector2 transformedItemInterval = itemInterval;
            float currentRotation = itemRotation;

            if (item.body == null)
            {
                transformedItemPos = new Vector2(item.Rect.X, item.Rect.Y);
                if (item.Submarine != null) transformedItemPos += item.Submarine.DrawPosition;
                transformedItemPos = transformedItemPos + itemPos;
            }
            else
            {
                //item.body.Enabled = true;

                Matrix transform = Matrix.CreateRotationZ(item.body.Rotation);

                if (item.body.Dir == -1.0f)
                {
                    transformedItemPos.X = -transformedItemPos.X;
                    transformedItemInterval.X = -transformedItemInterval.X;
                }
                transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);

                transformedItemPos += item.DrawPosition;

                currentRotation += item.body.Rotation;
            }

            foreach (Item containedItem in Inventory.Items)
            {
                if (containedItem == null) continue;

                containedItem.Sprite.Draw(
                    spriteBatch,
                    new Vector2(transformedItemPos.X, -transformedItemPos.Y),
                    containedItem.GetSpriteColor(),
                    -currentRotation,
                    1.0f,
                    (item.body != null && item.body.Dir == -1) ? SpriteEffects.FlipHorizontally : SpriteEffects.None);

                transformedItemPos += transformedItemInterval;
            }
        }

        public override void UpdateHUD(Character character)
        {
            Inventory.Update((float)Timing.Step);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            Inventory.Draw(spriteBatch);
        }
    }
}
