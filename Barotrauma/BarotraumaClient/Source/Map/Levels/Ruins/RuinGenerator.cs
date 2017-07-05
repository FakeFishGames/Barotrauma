using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.RuinGeneration
{
    partial class Ruin
    {
        public void Draw(SpriteBatch spriteBatch)
        {
            //foreach (BTRoom room in leaves)
            //{
            //    GUI.DrawRectangle(spriteBatch, room.Rect, Color.White);
            //}

            //foreach (Corridor corr in corridors)
            //{
            //    GUI.DrawRectangle(spriteBatch, corr.Rect, Color.Blue);
            //}

            foreach (Line line in walls)
            {
                GUI.DrawLine(spriteBatch, new Vector2(line.A.X, -line.A.Y), new Vector2(line.B.X, -line.B.Y), Color.Red, 0.0f, 10);
            }
        }
    }
}
