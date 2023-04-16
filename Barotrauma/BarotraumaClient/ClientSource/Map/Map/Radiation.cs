#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal partial class Radiation
    {
        private static readonly LocalizedString radiationTooltip = TextManager.Get("RadiationTooltip");
        private static float spriteIndex;
        private readonly SpriteSheet sheet = GUIStyle.RadiationAnimSpriteSheet;
        private int maxFrames => sheet.FrameCount + 1;

        private bool isHovingOver;

        public void Draw(SpriteBatch spriteBatch, Rectangle container, float zoom)
        {
            if (!Enabled) { return; }

            UISprite uiSprite = GUIStyle.Radiation;
            var (offsetX, offsetY) = Map.DrawOffset * zoom;
            var (centerX, centerY) = container.Center.ToVector2();
            var (halfSizeX, halfSizeY) = new Vector2(container.Width / 2f, container.Height / 2f) * zoom;
            float viewBottom = centerY + Map.Height * zoom;
            Vector2 topLeft = new Vector2(centerX + offsetX - halfSizeX, centerY + offsetY - halfSizeY);
            Vector2 size = new Vector2((Amount - increasedAmount) * zoom + halfSizeX, viewBottom - topLeft.Y);
            if (size.X < 0) { return; }

            Vector2 spriteScale = new Vector2(zoom);

            uiSprite.Sprite.DrawTiled(spriteBatch, topLeft, size, Params.RadiationAreaColor, Vector2.Zero, textureScale: spriteScale);

            Vector2 topRight = topLeft + Vector2.UnitX * size.X;

            int index = 0;
            for (float i = 0; i <= size.Y; i += sheet.FrameSize.Y / 2f * zoom)
            {
                bool isEven = ++index % 2 == 0;
                Vector2 origin = new Vector2(0.5f, 0) * sheet.FrameSize.X;
                // every other sprite's animation is reversed to make it seem more chaotic
                int sprite = (int) MathF.Floor(isEven ? spriteIndex : maxFrames - spriteIndex);
                sheet.Draw(spriteBatch, sprite, topRight + new Vector2(0, i), Params.RadiationBorderTint, origin, 0f, spriteScale);
            }

            isHovingOver = container.Contains(PlayerInput.MousePosition) && PlayerInput.MousePosition.X < topLeft.X + size.X;
        }

        public void DrawFront(SpriteBatch spriteBatch)
        {
            if (isHovingOver)
            {
                GUIComponent.DrawToolTip(spriteBatch, radiationTooltip, PlayerInput.MousePosition + new Vector2(18 * GUI.Scale));
            }
        }

        public void MapUpdate(float deltaTime)
        {
            float spriteStep = Params.BorderAnimationSpeed * deltaTime;
            spriteIndex = (spriteIndex + spriteStep) % maxFrames;

            if (increasedAmount > 0)
            {
                increasedAmount -= (lastIncrease / Params.AnimationSpeed) * deltaTime;
            }
        }
    }
}