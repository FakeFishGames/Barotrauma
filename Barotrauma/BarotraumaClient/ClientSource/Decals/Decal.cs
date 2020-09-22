using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Decal
    {

        public void Draw(SpriteBatch spriteBatch, Hull hull, float depth)
        {
            Vector2 drawPos = position + hull.Rect.Location.ToVector2();
            if (hull.Submarine != null) { drawPos += hull.Submarine.DrawPosition; }
            drawPos.Y = -drawPos.Y;
            
            spriteBatch.Draw(Sprite.Texture, drawPos, clippedSourceRect, Color * GetAlpha(), 0, Vector2.Zero, Scale, SpriteEffects.None, depth);

            if (GameMain.DebugDraw && affectedSections != null && affectedSections.Count > 0)
            {
                Vector2 drawOffset = hull.Submarine == null ? Vector2.Zero : hull.Submarine.DrawPosition;
                Point sectionSize = affectedSections.First().Rect.Size;
                Rectangle drawPositionRect = new Rectangle((int)(drawOffset.X + hull.Rect.X), (int)(drawOffset.Y + hull.Rect.Y), sectionSize.X, sectionSize.Y);

                foreach (var section in affectedSections)
                {
                    // Draw colors
                    GUI.DrawRectangle(spriteBatch, new Vector2(drawPositionRect.X + section.Rect.X, -(drawPositionRect.Y + section.Rect.Y)), new Vector2(sectionSize.X, sectionSize.Y), Color.Red, false, 0.0f, (int)Math.Max(1.5f / Screen.Selected.Cam.Zoom, 1.0f));
                }
            }
        }
    }
}
