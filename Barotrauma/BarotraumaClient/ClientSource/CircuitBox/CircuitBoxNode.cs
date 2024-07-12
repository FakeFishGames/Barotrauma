#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal partial class CircuitBoxNode
    {
        public RectangleF DrawRect;
        private RectangleF TopDrawRect;

        protected void UpdateDrawRects()
        {
            var drawRect = new RectangleF(Position - Size / 2f, Size);
            drawRect.Y = -drawRect.Y;
            drawRect.Y -= drawRect.Height;
            DrawRect = drawRect;

            TopDrawRect = new RectangleF(drawRect.X, drawRect.Y - (CircuitBoxSizes.NodeHeaderHeight - 1), drawRect.Width, CircuitBoxSizes.NodeHeaderHeight);
        }

        public void OnUICreated()
        {
            Size = CalculateSize(Connectors);
            UpdatePositions();
        }

        public virtual void OnResized(RectangleF drawRect) { }

        public void DrawBackground(SpriteBatch spriteBatch, RectangleF drawRect, RectangleF topDrawRect, Color color)
        {
            CircuitBox.NodeFrameSprite?.Draw(spriteBatch, drawRect, color);
            CircuitBox.NodeTopSprite?.Draw(spriteBatch, topDrawRect, color);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos, Color color)
        {
            RectangleF drawRect = OverrideRectLocation(DrawRect, drawPos, Position),
                       topDrawRect = OverrideRectLocation(TopDrawRect, drawPos, Position);

            DrawBackground(spriteBatch, drawRect, topDrawRect, color);
            DrawHeader(spriteBatch, topDrawRect, color);
            DrawBody(spriteBatch, drawRect, color);

            DrawConnectors(spriteBatch, drawPos);
        }

        public void DrawHUD(SpriteBatch spriteBatch, Camera camera)
        {
            foreach (var c in Connectors)
            {
                c.DrawHUD(spriteBatch, camera);
            }
        }

        public virtual void DrawHeader(SpriteBatch spriteBatch, RectangleF rect, Color color) { }
        public virtual void DrawBody(SpriteBatch spriteBatch, RectangleF rect, Color color) { }

        public void DrawConnectors(SpriteBatch spriteBatch, Vector2 drawPos)
        {
            var color = Color.White * Opacity;
            foreach (var c in Connectors)
            {
                c.Draw(spriteBatch, drawPos, Position, color);
            }
        }

        public void DrawSelection(SpriteBatch spriteBatch, Color color)
        {
            int pad = GUI.IntScale(8);

            var rect = Rect;
            rect.Y = -rect.Y;
            rect.Y -= rect.Height;

            rect.Inflate(pad, pad);

            GUI.DrawFilledRectangle(spriteBatch, rect, color * Opacity);
        }

        /// <summary>
        /// Sets the location of the rectangle to a specific position, keeping origin intact.
        /// </summary>
        public static RectangleF OverrideRectLocation(RectangleF rect, Vector2 overridePos, Vector2 originalPos)
        {
            rect.Location -= new Vector2(originalPos.X, -originalPos.Y);
            rect.Location += new Vector2(overridePos.X, -overridePos.Y);
            return rect;
        }
    }
}