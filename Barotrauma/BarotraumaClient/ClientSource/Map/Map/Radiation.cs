#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal partial class Radiation
    {
        public void Draw(SpriteBatch spriteBatch, Rectangle container, float zoom)
        {
            if (!Enabled) { return; }

            UISprite uiSprite = GUI.Style.RadiationSprite;
            var (offsetX, offsetY) = Map.DrawOffset * zoom;
            var (centerX, centerY) = container.Center.ToVector2();
            var (halfSizeX, halfSizeY) = new Vector2(container.Width / 2f, container.Height / 2f) * zoom;
            float viewBottom = centerY + Map.Height * zoom;
            Vector2 topLeft = new Vector2(centerX + offsetX - halfSizeX, centerY + offsetY - halfSizeY);
            Vector2 size = new Vector2((Amount - increasedAmount) * zoom + halfSizeX, viewBottom - topLeft.Y);
            if (size.X < 0) { return; }

            uiSprite.Sprite.DrawTiled(spriteBatch, topLeft, size, GUI.Style.Red * 0.33f, Vector2.Zero, textureScale: new Vector2(zoom));

            if (container.Contains(PlayerInput.MousePosition) && PlayerInput.MousePosition.X < topLeft.X + size.X)
            {
                // TODO tooltip?
            }
        }

        public void MapUpdate(float deltaTime)
        {
            if (increasedAmount > 0)
            {
                increasedAmount -= (lastIncrease / Params.AnimationSpeed) * deltaTime;
            }
        }
    }
}