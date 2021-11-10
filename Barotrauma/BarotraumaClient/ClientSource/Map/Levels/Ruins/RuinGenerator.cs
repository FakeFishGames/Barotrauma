using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.RuinGeneration
{
    partial class Ruin
    {
        public void DebugDraw(SpriteBatch spriteBatch)
        {
            Rectangle drawRect = Area;
            drawRect.Y = -drawRect.Y - Area.Height;
            GUI.DrawRectangle(spriteBatch, drawRect, Color.Cyan, false, 0, 6);
        }
    }
}
